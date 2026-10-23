using VideoCompressor.Core.IO;

namespace VideoCompressor.Core.Tests;

public sealed class OutputPathResolverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vc-paths-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Default_IsNextToSourceWithSuffix()
    {
        var input = Touch("запись 1.mp4");
        Assert.Equal(Path.Combine(_dir, "запись 1_compressed.mp4"), OutputPathResolver.GetDefaultOutputPath(input));
    }

    [Fact]
    public void NonMp4Source_GetsMp4Result()
    {
        var input = Touch("clip.mkv");
        Assert.Equal(Path.Combine(_dir, "clip_compressed.mp4"), OutputPathResolver.GetDefaultOutputPath(input));
    }

    [Fact]
    public void ExistingFiles_AreNeverReused()
    {
        var input = Touch("clip.mp4");
        Touch("clip_compressed.mp4");
        Assert.Equal(Path.Combine(_dir, "clip_compressed (1).mp4"), OutputPathResolver.GetDefaultOutputPath(input));

        Touch("clip_compressed (1).mp4");
        Assert.Equal(Path.Combine(_dir, "clip_compressed (2).mp4"), OutputPathResolver.GetDefaultOutputPath(input));
    }

    [Fact]
    public void SourceThatLooksLikeResult_IsNotOverwritten()
    {
        // Compressing "x_compressed" must not produce a path equal to itself.
        var input = Touch("x_compressed.mp4");
        var output = OutputPathResolver.GetDefaultOutputPath(input);
        Assert.False(OutputPathResolver.IsSamePath(input, output));
    }

    [Fact]
    public void IsSamePath_NormalizesRelativeSegments()
    {
        var a = Path.Combine(_dir, "clip.mp4");
        var b = Path.Combine(_dir, "sub", "..", "clip.mp4");
        Assert.True(OutputPathResolver.IsSamePath(a, b));
        Assert.False(OutputPathResolver.IsSamePath(a, Path.Combine(_dir, "other.mp4")));
    }

    [Fact]
    public void IsSamePath_FollowsPlatformCaseSensitivity()
    {
        var a = Path.Combine(_dir, "Clip.mp4");
        var b = Path.Combine(_dir, "clip.MP4");
        Assert.Equal(!OperatingSystem.IsLinux(), OutputPathResolver.IsSamePath(a, b));
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, []);
        return path;
    }
}
