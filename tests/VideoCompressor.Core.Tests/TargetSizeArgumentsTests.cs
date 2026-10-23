using VideoCompressor.Core.Compression;
using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.Media;

namespace VideoCompressor.Core.Tests;

public class TargetSizeArgumentsTests
{
    private const string Input = "/videos/запись.mp4";
    private const string Output = "/videos/запись_compressed.mp4";
    private const string PassLog = "/tmp/vc/pass";

    private static readonly VideoInfo Reference = new(
        TimeSpan.FromSeconds(65.617), 199_792_913, 2560, 1440, 59.9993, "h264", 1, IsHdr: false);

    private static readonly CompressionSettings Target25 =
        CompressionSettings.Default with { TargetSizeBytes = 25L * 1024 * 1024 };

    [Fact]
    public void Gpu_CapsBitrateInsteadOfQuality()
    {
        var args = FfmpegArgumentsBuilder.Build(Input, Output, Reference, Target25);

        AssertContainsSequence(args, "-rc", "vbr", "-b:v", "2940k", "-maxrate", "2940k", "-bufsize", "5880k",
            "-multipass", "fullres");
        Assert.DoesNotContain("-cq", args);
        Assert.DoesNotContain("-pass", args);
        Assert.Equal(Output, args[^1]);
    }

    [Fact]
    public void Gpu_IsSinglePass()
    {
        var passes = FfmpegArgumentsBuilder.BuildPasses(Input, Output, Reference, Target25);
        Assert.Single(passes);
        Assert.False(FfmpegArgumentsBuilder.NeedsTwoPasses(Target25));
    }

    [Fact]
    public void BitrateOverride_IsUsed()
    {
        var args = FfmpegArgumentsBuilder.Build(Input, Output, Reference, Target25, videoBitrateKbps: 2500);
        AssertContainsSequence(args, "-b:v", "2500k", "-maxrate", "2500k");
    }

    [Theory]
    [InlineData(OutputFormat.Efficient, "libx265")]
    [InlineData(OutputFormat.Compatible, "libx264")]
    public void Cpu_UsesTwoPasses(OutputFormat format, string encoder)
    {
        var settings = Target25 with { Encoder = EncoderKind.Cpu, Format = format };
        Assert.True(FfmpegArgumentsBuilder.NeedsTwoPasses(settings));
        Assert.Throws<InvalidOperationException>(() => FfmpegArgumentsBuilder.Build(Input, Output, Reference, settings));

        var passes = FfmpegArgumentsBuilder.BuildPasses(Input, Output, Reference, settings, PassLog);

        Assert.Equal(2, passes.Count);
        var (first, second) = (passes[0], passes[1]);

        // Pass 1: analysis only — no audio, no output file.
        AssertContainsSequence(first, "-c:v", encoder);
        AssertContainsSequence(first, "-b:v", "2940k");
        AssertContainsSequence(first, "-pass", "1", "-passlogfile", PassLog);
        Assert.Contains("-an", first);
        AssertContainsSequence(first, "-f", "null");
        Assert.Equal(OperatingSystem.IsWindows() ? "NUL" : "/dev/null", first[^1]);
        Assert.DoesNotContain(Output, first);

        // Pass 2: the real file, with audio.
        AssertContainsSequence(second, "-b:v", "2940k");
        AssertContainsSequence(second, "-pass", "2", "-passlogfile", PassLog);
        AssertContainsSequence(second, "-map", "0:a?");
        Assert.Equal(Output, second[^1]);
        Assert.DoesNotContain("-crf", second);
    }

    [Fact]
    public void Cpu_TwoPasses_ApplySameFilters()
    {
        var settings = Target25 with { Encoder = EncoderKind.Cpu, MaxHeight = 1080, MaxFps = 30 };
        var passes = FfmpegArgumentsBuilder.BuildPasses(Input, Output, Reference, settings, PassLog);
        Assert.All(passes, p => AssertContainsSequence(p, "-vf", "fps=30,scale=-2:1080:flags=lanczos"));
    }

    [Fact]
    public void Cpu_TwoPasses_RequirePassLog()
    {
        var settings = Target25 with { Encoder = EncoderKind.Cpu };
        Assert.Throws<ArgumentNullException>(() => FfmpegArgumentsBuilder.BuildPasses(Input, Output, Reference, settings));
    }

    [Fact]
    public void QualityMode_IsUnchanged()
    {
        var passes = FfmpegArgumentsBuilder.BuildPasses(Input, Output, Reference, CompressionSettings.Default);
        Assert.Single(passes);
        AssertContainsSequence(passes[0], "-rc", "vbr", "-cq", "30", "-b:v", "0");
    }

    private static void AssertContainsSequence(IReadOnlyList<string> args, params string[] expected)
    {
        for (var start = 0; start <= args.Count - expected.Length; start++)
        {
            if (args.Skip(start).Take(expected.Length).SequenceEqual(expected))
            {
                return;
            }
        }

        Assert.Fail($"Expected sequence [{string.Join(' ', expected)}] in [{string.Join(' ', args)}]");
    }
}
