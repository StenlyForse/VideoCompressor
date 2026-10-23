using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.Platform;

namespace VideoCompressor.Core.Tests;

public sealed class FfmpegToolsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vc-tools-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void FindsBothToolsInOneDirectory()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;
        var bin = Directory.CreateDirectory(Path.Combine(_dir, "bin")).FullName;
        Touch(bin, "ffmpeg");
        Touch(bin, "ffprobe");

        var tools = FfmpegTools.TryLocate([empty, bin]);

        Assert.NotNull(tools);
        Assert.Equal(Path.Combine(bin, PlatformInfo.ExecutableName("ffmpeg")), tools.FfmpegPath);
        Assert.Equal(Path.Combine(bin, PlatformInfo.ExecutableName("ffprobe")), tools.FfprobePath);
    }

    [Fact]
    public void IgnoresDirectoryWithoutFfprobe()
    {
        Touch(_dir, "ffmpeg");
        Assert.Null(FfmpegTools.TryLocate([_dir]));
    }

    [Fact]
    public void SearchOrder_AppDirectoryThenPathThenWellKnown()
    {
        var directories = FfmpegTools.SearchDirectories().ToList();

        Assert.Equal(AppContext.BaseDirectory, directories[0]);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "ffmpeg"), directories[1]);

        var firstPathEntry = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (firstPathEntry is not null)
        {
            Assert.Equal(firstPathEntry.Trim('"'), directories[2]);
        }

        Assert.Equal(PlatformInfo.ExtraToolDirectories, directories.TakeLast(PlatformInfo.ExtraToolDirectories.Count));
    }

    private static void Touch(string dir, string tool) =>
        File.WriteAllBytes(Path.Combine(dir, PlatformInfo.ExecutableName(tool)), []);
}
