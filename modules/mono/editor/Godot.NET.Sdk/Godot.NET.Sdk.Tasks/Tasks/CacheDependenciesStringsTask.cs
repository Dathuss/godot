using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot.NET.Sdk.Tasks.StringCaching;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Godot.NET.Sdk.Tasks;

/// <summary>
/// Patches references, caches them, and replaces their MSBuild reference paths with the cached path
/// </summary>
public class CacheDependenciesStringsTask : Task
{
#nullable disable // MSBuild arguments are not nullable
    [Required]
    public string IntermediateOutputPath { get; set; }

    [Required]
    public ITaskItem[] ReferencePath { get; set; }

    [Required]
    public ITaskItem[] ReferenceCopyLocalPaths { get; set; }

    [Required]
    public ITaskItem[] PackageReference { get; set; }


    [Required]
    public ITaskItem[] CacheStrings { get; set; }


    [Output]
    public ITaskItem[] RemovedReferencePath { get; set; }

    [Output]
    public ITaskItem[] AddedReferencePath { get; set; }

    [Output]
    public ITaskItem[] RemovedReferenceCopyLocalPaths { get; set; }

    [Output]
    public ITaskItem[] AddedReferenceCopyLocalPaths { get; set; }

    [Output]
    public ITaskItem[] EmittedFiles { get; set; }
#nullable restore

    public override bool Execute()
    {
        string intermediateDir = StringCachingCommon.GetAndCreateCacheDir(IntermediateOutputPath);

        Dictionary<string, ITaskItem> packagesToPatch = PackageReference.Where(x => x.GetBoolMetadata("CacheStrings")).ToDictionary(x => x.ItemSpec);
        Dictionary<string, ITaskItem> assemblyNamesToPatch = CacheStrings.ToDictionary(x => x.ItemSpec);

        List<ITaskItem> removedReferencePath = [];
        List<ITaskItem> addedReferencePath = [];

        List<ITaskItem> removedReferenceCopyLocalPaths = [];
        List<ITaskItem> addedReferenceCopyLocalPaths = [];

        List<ITaskItem> emittedFiles = [];

        StringCachingContext? ctx = null;

        try
        {
            foreach (ITaskItem reference in ReferencePath)
            {
                string fileName = reference.GetMetadata("FileName");
                if (fileName == "GodotSharp")
                    continue;

                ITaskItem assemblyTaskItem;

                // Checks for <ProjectReference> and <Reference>
                if (reference.GetBoolMetadata("CacheStrings")) { assemblyTaskItem = reference; }
                // Checks for <PackageReference>
                else if (reference.TryGetMetadata("NuGetPackageId", out string? nuGetPackageId) && packagesToPatch.TryGetValue(nuGetPackageId!, out assemblyTaskItem)) { }
                // Checks for <CacheStrings>
                else if (assemblyNamesToPatch.TryGetValue(fileName, out assemblyTaskItem)) { }
                else continue;

                if (ctx == null)
                {
                    string? godotSharp = StringCachingCommon.GetGodotSharpFromReferencePath(ReferencePath, Log);
                    if (string.IsNullOrEmpty(godotSharp))
                        return false;

                    ctx = new StringCachingContext();
                    ctx.OpenGodotSharp(godotSharp!);
                }

                string fullPath = reference.GetMetadata("FullPath");
                string newHash = StringCachingCommon.ComputeHash(fullPath);

                string outputFile = Path.Combine(intermediateDir, Path.GetFileName(fullPath));
                string hashFile = outputFile + ".hash.cache";
                string pdbFile = StringCachingContext.GetPdbFileName(outputFile);

                // Replace ReferencePath and ReferenceCopyLocalPaths to the cached path
                removedReferencePath.Add(reference);

                TaskItem cachedReference = reference.CloneWithNewItemSpec(outputFile);
                addedReferencePath.Add(cachedReference);
                emittedFiles.Add(cachedReference);

                ITaskItem referenceOfReferenceCopyLocalPaths = ReferenceCopyLocalPaths.First(x => x.GetMetadata("FileName") == fileName && x.GetMetadata("Extension") == ".dll");
                removedReferenceCopyLocalPaths.Add(referenceOfReferenceCopyLocalPaths);

                TaskItem cachedReferenceForCopy = referenceOfReferenceCopyLocalPaths.CloneWithNewItemSpec(outputFile);
                addedReferenceCopyLocalPaths.Add(cachedReferenceForCopy);

                // Try to replace symbol file of ReferenceCopyLocalPaths
                ITaskItem pdbOfReferenceCopyLocalPaths = ReferenceCopyLocalPaths.FirstOrDefault(x => x.GetMetadata("FileName") == fileName && x.GetMetadata("Extension") == ".pdb");
                if (pdbOfReferenceCopyLocalPaths != null)
                {
                    removedReferenceCopyLocalPaths.Add(pdbOfReferenceCopyLocalPaths);

                    TaskItem cachedPdbForCopy = pdbOfReferenceCopyLocalPaths.CloneWithNewItemSpec(pdbFile);
                    addedReferenceCopyLocalPaths.Add(cachedPdbForCopy);
                    emittedFiles.Add(cachedPdbForCopy);
                }

                emittedFiles.Add(new TaskItem(hashFile));

                if (File.Exists(outputFile) && File.Exists(hashFile) && File.ReadAllText(hashFile) == newHash)
                {
                    Log.LogMessage($"Assembly {fileName} up to date");

                    if (File.Exists(pdbFile))
                    {
                        emittedFiles.Add(new TaskItem(pdbFile));
                    }

                    continue;
                }

                if (!StringCachingCommon.DoCache(ctx, fullPath, outputFile, fileName, Log, out bool isPdbFileOutputted))
                {
                    return false;
                }

                if (pdbOfReferenceCopyLocalPaths != null && !isPdbFileOutputted)
                {
                    // Unlikely to happen, but it's better to record it
                    Log.LogWarning($"Dependency {fileName} was supposed to output a PDB file when patched, but it didn't. This shouldn't happen.");
                    emittedFiles.RemoveAll(x => x.ItemSpec == pdbFile);
                }

                File.WriteAllText(hashFile, newHash);
            }
        }
        finally
        {
            ctx?.Dispose();
        }

        RemovedReferencePath = removedReferencePath.ToArray();
        AddedReferencePath = addedReferencePath.ToArray();

        RemovedReferenceCopyLocalPaths = removedReferenceCopyLocalPaths.ToArray();
        AddedReferenceCopyLocalPaths = addedReferenceCopyLocalPaths.ToArray();

        EmittedFiles = emittedFiles.ToArray();

        return true;
    }
}
