using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.IO;
using System.Collections.Generic;
using Godot.NET.Sdk.Tasks.StringCaching;

namespace Godot.NET.Sdk.Tasks;

public class CacheMainAssemblyStringsTask : Task
{
#nullable disable // MSBuild arguments are not nullable
    [Required]
    public string AssemblyName { get; set; }

    [Required]
    public ITaskItem IntermediateAssembly { get; set; }

    [Required]
    public ITaskItem[] ReferencePath { get; set; }

    [Required]
    public string IntermediateOutputPath { get; set; }


    [Output]
    public ITaskItem CachedIntermediateAssembly { get; set; }

    [Output]
    public string OutputPdbFile { get; set; }

    [Output]
    public ITaskItem[] EmittedFiles { get; set; }
#nullable restore

    public override bool Execute()
    {
        string intermediateDir = StringCachingCommon.GetAndCreateCacheDir(IntermediateOutputPath);

        string? godotSharp = StringCachingCommon.GetGodotSharpFromReferencePath(ReferencePath, Log);
        if (string.IsNullOrEmpty(godotSharp))
            return false;

        List<ITaskItem> emittedFiles = [];

        string newHash = StringCachingCommon.ComputeHash(IntermediateAssembly.ItemSpec);

        string outputFile = Path.Combine(intermediateDir, Path.GetFileName(IntermediateAssembly.ItemSpec));
        string hashFile = outputFile + ".hash.cache";
        string pdbFile = StringCachingContext.GetPdbFileName(outputFile);

        CachedIntermediateAssembly = IntermediateAssembly.CloneWithNewItemSpec(outputFile);
        emittedFiles.Add(CachedIntermediateAssembly);
        emittedFiles.Add(new TaskItem(hashFile));

        if (File.Exists(outputFile) && File.Exists(hashFile) && File.ReadAllText(hashFile) == newHash)
        {
            Log.LogMessage($"Main assembly up to date");

            if (File.Exists(pdbFile))
            {
                emittedFiles.Add(new TaskItem(pdbFile));
            }

            EmittedFiles = emittedFiles.ToArray();

            return true;
        }

        using StringCachingContext ctx = new();

        ctx.OpenGodotSharp(godotSharp!);
        if (!StringCachingCommon.DoCache(ctx, IntermediateAssembly.ItemSpec, outputFile, AssemblyName, Log, out bool isPdbFileOutputted))
        {
            return false;
        }

        // Depending on the build configuration, the output PDB file may not exist.
        if (isPdbFileOutputted)
        {
            OutputPdbFile = pdbFile;
            emittedFiles.Add(new TaskItem(OutputPdbFile));
        }

        File.WriteAllText(hashFile, newHash);

        EmittedFiles = emittedFiles.ToArray();

        return true;
    }
}
