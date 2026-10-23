using VideoCompressor.Core.Platform;

namespace VideoCompressor.Core.IO;

public static class OutputPathResolver
{
    public const string Suffix = "_compressed";
    public const string Extension = ".mp4";

    /// <summary>
    /// <c>&lt;name&gt;_compressed.mp4</c> next to the source; never an existing file (spec US-5).
    /// </summary>
    public static string GetDefaultOutputPath(string inputPath)
    {
        var fullInput = Path.GetFullPath(inputPath);
        var directory = Path.GetDirectoryName(fullInput)!;
        var baseName = Path.GetFileNameWithoutExtension(fullInput) + Suffix;

        var candidate = Path.Combine(directory, baseName + Extension);
        for (var n = 1; File.Exists(candidate) || IsSamePath(candidate, fullInput); n++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({n}){Extension}");
        }

        return candidate;
    }

    public static bool IsSamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), PlatformInfo.PathComparison);
}
