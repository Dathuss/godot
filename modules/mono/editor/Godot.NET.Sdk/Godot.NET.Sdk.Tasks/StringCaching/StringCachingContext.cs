using System;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Godot.NET.Sdk.Tasks.StringCaching;

public class StringCachingContext : IDisposable
{
    internal ModuleDefinition Module { get; private set; } = null!;

    internal string FileName { get; private set; } = null!;

    internal GodotSharpDefs? Defs { get; private set; } = null;

    internal string? GodotSharpDirectory { get; private set; } = null;

    internal TypeReference Imported_StringNameType { get; private set; } = null!;
    internal MethodReference Imported_StringName_StringCtor { get; private set; } = null!;
    internal TypeReference Imported_NodePathType { get; private set; } = null!;
    internal MethodReference Imported_NodePath_StringCtor { get; private set; } = null!;

    internal readonly CacheTypesEmitter CacheTypesEmitter;

    public StringCachingContext()
    {
        CacheTypesEmitter = new CacheTypesEmitter(this);
    }

    public int NumberOfStringNamesWritten { get; set; }
    public int NumberOfNodePathsWritten { get; set; }

    public void RunAndSave(string inputFile, string outputFile, out string? outputPdbFile)
    {
        FileName = inputFile;

        string directory = Path.GetDirectoryName(FileName) ?? throw new ArgumentException("Could not resolve directory name from module path");
        using DefaultAssemblyResolver resolver = new();
        if (GodotSharpDirectory != null)
            resolver.AddSearchDirectory(GodotSharpDirectory);
        resolver.AddSearchDirectory(directory);

        string tempDirectory, tempOutputFile;
        outputPdbFile = null;

        Module = ModuleDefinition.ReadModule(FileName, new ReaderParameters()
        {
            AssemblyResolver = resolver,
            ReadSymbols = true,
            SymbolReaderProvider = new DefaultSymbolReaderProvider(throwIfNoSymbol: false),
            ThrowIfSymbolsAreNotMatching = false
        });
        using (Module)
        {
            if (Defs == null)
            {
                Defs = GodotSharpDefs.FromReferencingModule(Module, resolver);
                GodotSharpDirectory = Path.GetDirectoryName(Defs.Module.FileName);
            }
            ImportGodotSharpReferences();
            CacheTypesEmitter.Reset();

            foreach (TypeDefinition moduleType in Module.Types)
            {
                void PatchTypeAndNestedTypes(TypeDefinition type)
                {
                    PatchType(type);
                    foreach (TypeDefinition nestedType in type.NestedTypes)
                    {
                        PatchTypeAndNestedTypes(nestedType);
                    }
                }
                PatchTypeAndNestedTypes(moduleType);
            }
            CacheTypesEmitter.EmitTypes();

            NumberOfStringNamesWritten = CacheTypesEmitter.StringNamesToCache.Count;
            NumberOfNodePathsWritten = CacheTypesEmitter.NodePathsToCache.Count;

            // Mono.Cecil will not behave correctly if you write to a module to itself
            // So we write it to a temp file first.

            // However, if a PDB file has to be emitted, it will be written relative to this temporary file too.
            // For example: for `tmp.qwerty.dll`, a PDB file named `tmp.qwerty.pdb` would be written.
            // A managed assembly holds the name of its associated PDB file, and in release builds,
            // this is the only file that the runtime will attempt to read.
            // This means we have to give the temporary file the same name as the output file,
            // so we put it in a temporary directory to give it the name we want without potentially overwriting another file.
            tempDirectory = IOUtils.CreateTempDir();
            tempOutputFile = Path.Combine(tempDirectory, Path.GetFileName(outputFile));
            if (Module.HasSymbols)
            {
                // Write DLL with optional PDB
                WriterParameters writerParameters = new()
                {
                    WriteSymbols = true,
                    SymbolWriterProvider = new DefaultSymbolWriterProvider(),
                };
                Module.Write(tempOutputFile, writerParameters);

                // Check if optional PDB was also written
                string cecilOutputPdb = GetPdbFileName(tempOutputFile);
                if (File.Exists(cecilOutputPdb))
                {
                    // Move the optional PDB to the directory where the DLL will be moved to
                    outputPdbFile = GetPdbFileName(outputFile);
                    IOUtils.MoveFileWithOverwrite(cecilOutputPdb, outputPdbFile);
                }
            }
            else
            {
                // Write DLL without PDB (since no symbols are present)
                Module.Write(tempOutputFile);
            }
        }

        IOUtils.MoveFileWithOverwrite(tempOutputFile, outputFile);
        Directory.Delete(tempDirectory, recursive: true); // Directory should be empty, but delete recursively just to make sure
    }

    public static string GetPdbFileName(string assemblyFileName)
    {
        return Path.ChangeExtension(assemblyFileName, ".pdb");
    }

    /// <summary>
    /// Manually open the GodotSharp assembly.
    /// </summary>
    public void OpenGodotSharp(string assemblyPath)
    {
        CloseGodotSharp();
        Defs = GodotSharpDefs.FromModule(ModuleDefinition.ReadModule(assemblyPath));
        GodotSharpDirectory = Path.GetDirectoryName(assemblyPath);
    }

    /// <summary>
    /// Closes the GodotSharp assembly, which allows to load a different GodotSharp assembly
    /// with the same Context.
    /// </summary>
    public void CloseGodotSharp()
    {
        Defs?.Dispose();
        Defs = null;
        GodotSharpDirectory = null;
    }

    private void ImportGodotSharpReferences()
    {
        Imported_StringNameType = Module.ImportReference(Defs!.StringNameType);
        Imported_StringName_StringCtor = Module.ImportReference(Defs.StringName_StringCtor);
        Imported_NodePathType = Module.ImportReference(Defs.NodePathType);
        Imported_NodePath_StringCtor = Module.ImportReference(Defs.NodePath_StringCtor);
    }

    private void PatchType(TypeDefinition type)
    {
        foreach (MethodDefinition typeMethod in type.Methods)
        {
            if (typeMethod.Body == null)
                continue;

            // No need to patch if we're already in a static constructor
            if (typeMethod.Name != ".cctor")
                MatchAndPatch(typeMethod);
        }
    }

    private void MatchAndPatch(MethodDefinition method)
    {
        Collection<Instruction> instructions = method.Body.Instructions;

        // We are looking for this pattern:
        // IL ldstr "MY_CONSTANT"
        // IL call (Godot.StringName/Godot.NodePath)::op_Implicit(System.String)

        // Which we will replace with
        // IL ldsfld our_generated_field

        for (int i = 0; i < instructions.Count - 1; i++)
        {
            Instruction ldstrInstruction;
            Instruction callInstruction;

            ldstrInstruction = instructions[i];
            if (ldstrInstruction.OpCode != OpCodes.Ldstr)
                continue;

            callInstruction = instructions[i + 1];
            if (callInstruction.OpCode != OpCodes.Call)
                continue;

            MethodReference calledMethod = (MethodReference)callInstruction.Operand;

            void EditInstructions(Func<string, FieldDefinition> fieldGetter)
            {
                // Mono.Cecil has a bug where if you replace an instruction, branches that point
                // to the previous Instruction object are not updated. This will lead to the corruption of the
                // method body when rebuilding the assembly
                // The easiest and fastest way to circumvent this is to directly edit the fields
                // of the Instruction object so as not to invalidate the reference.
                ldstrInstruction.OpCode = OpCodes.Ldsfld;
                ldstrInstruction.Operand = fieldGetter((string)ldstrInstruction.Operand);
                callInstruction.OpCode = OpCodes.Nop;
                callInstruction.Operand = null;
            }

            if (IsStringToStringNameImplicitOp(calledMethod))
            {
                EditInstructions(operand => CacheTypesEmitter.AddStringName(operand));
            }
            else if (IsStringToNodePathImplicitOp(calledMethod))
            {
                EditInstructions(operand => CacheTypesEmitter.AddNodePath(operand));
            }
        }
    }

    private static bool IsStringToStringNameImplicitOp(MethodReference method)
    {
        return method.Name == "op_Implicit"
            && method.DeclaringType.FullName == "Godot.StringName"
            && method.ReturnType.FullName == "Godot.StringName"
            && method.Parameters.Count == 1
            && method.Parameters[0].ParameterType.FullName == "System.String";
    }

    private static bool IsStringToNodePathImplicitOp(MethodReference method)
    {
        return method.Name == "op_Implicit"
            && method.DeclaringType.FullName == "Godot.NodePath"
            && method.ReturnType.FullName == "Godot.NodePath"
            && method.Parameters.Count == 1
            && method.Parameters[0].ParameterType.FullName == "System.String";
    }

    public void Dispose()
    {
        Defs?.Dispose();
    }
}
