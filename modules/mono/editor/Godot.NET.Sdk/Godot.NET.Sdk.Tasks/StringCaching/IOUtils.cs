using System.IO;

namespace Godot.NET.Sdk.Tasks.StringCaching;

internal static class IOUtils
{
    public static void MoveFileWithOverwrite(string sourceFile, string destFile)
    {
        // netstandard2.0 does not yet support the overwrite parameter in File.Move
        // So we have to do it manually.
        File.Delete(destFile);
        File.Move(sourceFile, destFile);
    }

    public static string CreateTempDir()
    {
        string result;
        do
        {
            result = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        } while (Directory.Exists(result));

        Directory.CreateDirectory(result);
        return result;
    }
}
