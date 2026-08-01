
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Godot.NET.Sdk.Tasks.StringCaching;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Godot.NET.Sdk.Tasks;

internal static class StringCachingCommon
{
    public static string? GetGodotSharpFromReferencePath(ITaskItem[] referencePath, TaskLoggingHelper log)
    {
        foreach (ITaskItem reference in referencePath)
        {
            string fileName = reference.GetMetadata("FileName");
            if (fileName == "GodotSharp")
            {
                string fullPath = reference.GetMetadata("FullPath");
                return fullPath;
            }
        }

        log.LogError("No GodotSharp reference found in the project. Make sure you reference it or that you use Godot.NET.Sdk.");
        return null;
    }

    public static bool DoCache(StringCachingContext ctx, string inputPath, string outputPath, string assemblyName, TaskLoggingHelper log, out bool isPdbFileOutputted)
    {
        isPdbFileOutputted = false;
        log.LogMessage($"{assemblyName}: Caching Godot strings...");
        try
        {
            ctx.RunAndSave(inputPath, outputPath, out string? outputPdbFile);
            isPdbFileOutputted = outputPdbFile != null;
            log.LogMessage($"{assemblyName}: StringNames cached: {ctx.NumberOfStringNamesWritten}");
            log.LogMessage($"{assemblyName}: NodePaths cached: {ctx.NumberOfNodePathsWritten}");
        }
        catch (NoGodotSharpReferenceExeption ex)
        {
            log.LogWarning($"{assemblyName}: {ex}");
        }
        catch (IOException ex)
        {
            log.LogError($"{assemblyName}: An IO error occurred: {ex}");
            return false;
        }
        catch (Exception ex)
        {
            if (ex.InnerException is IOException)
                log.LogError($"{assemblyName}: An IO error occurred: {ex}");
            else
                log.LogError($"{assemblyName}: An unhandled exception occurred: {ex}");
            return false;
        }
        return true;
    }

    public static string GetAndCreateCacheDir(string intermediateOutputPath)
    {
        string intermediateDir = Path.Combine(intermediateOutputPath, "string-cache");
        Directory.CreateDirectory(intermediateDir);
        return intermediateDir;
    }

    /// <summary>
    /// Computes a unique hash that takes into account the input file timestamp,
    /// and the current version
    /// </summary>
    public static string ComputeHash(string inputFile)
    {
        using SHA256 hash = SHA256.Create();

        void Hash(byte[] buffer, bool isFinalBlock = false)
        {
            if (isFinalBlock)
            {
                hash.TransformFinalBlock(buffer, 0, buffer.Length);
            }
            else
            {
                hash.TransformBlock(buffer, 0, buffer.Length, buffer, 0);
            }
        }
        void HashString(string str, bool isFinalBlock = false) => Hash(Encoding.UTF8.GetBytes(str), isFinalBlock);
        void HashLong(long value, bool isFinalBlock = false) => Hash(BitConverter.GetBytes(value), isFinalBlock);

        HashString(typeof(StringCachingCommon).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);

        HashLong(File.GetLastWriteTimeUtc(inputFile).ToBinary(),
            isFinalBlock: true);

        return string.Concat(hash.Hash.Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
