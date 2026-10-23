using VideoCompressor.Core.Platform;

namespace VideoCompressor.Core.Ffmpeg;

public sealed record FfmpegTools(string FfmpegPath, string FfprobePath)
{
    /// <summary>Finds ffmpeg and ffprobe in the same directory (plan §4.1); <c>null</c> if not installed.</summary>
    public static FfmpegTools? TryLocate() => TryLocate(SearchDirectories());

    internal static FfmpegTools? TryLocate(IEnumerable<string> directories)
    {
        foreach (var directory in directories)
        {
            var ffmpeg = Path.Combine(directory, PlatformInfo.ExecutableName("ffmpeg"));
            var ffprobe = Path.Combine(directory, PlatformInfo.ExecutableName("ffprobe"));
            if (File.Exists(ffmpeg) && File.Exists(ffprobe))
            {
                return new FfmpegTools(ffmpeg, ffprobe);
            }
        }

        return null;
    }

    internal static IEnumerable<string> SearchDirectories()
    {
        var appDirectory = AppContext.BaseDirectory;
        yield return appDirectory;
        yield return Path.Combine(appDirectory, "ffmpeg");

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return entry.Trim('"');
        }

        foreach (var directory in PlatformInfo.ExtraToolDirectories)
        {
            yield return directory;
        }
    }
}
