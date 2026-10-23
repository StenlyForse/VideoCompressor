using VideoCompressor.Core.Compression;
using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.Media;

namespace VideoCompressor.Core.Tests;

public class FfmpegArgumentsBuilderTests
{
    private const string Input = "/videos/моя запись.mp4";
    private const string Output = "/videos/моя запись_compressed.mp4";

    // The reference recording from spec §4.
    private static readonly VideoInfo Source1440p60 = new(
        TimeSpan.FromSeconds(65.6), 199_792_913, 2560, 1440, 59.99, "h264", 1, IsHdr: false);

    private static readonly CompressionSettings Balanced = CompressionSettings.Default;

    private static IReadOnlyList<string> Build(CompressionSettings settings, VideoInfo? source = null) =>
        FfmpegArgumentsBuilder.Build(Input, Output, source ?? Source1440p60, settings);

    [Fact]
    public void GpuHevc_MatchesPlan()
    {
        var args = Build(Balanced with { Format = OutputFormat.Efficient, Encoder = EncoderKind.Gpu });

        AssertContainsSequence(args,
            "-c:v", "hevc_nvenc", "-preset", "p6", "-tune", "hq", "-rc", "vbr", "-cq", "30", "-b:v", "0",
            "-bf", "4", "-b_ref_mode", "middle", "-spatial-aq", "1", "-temporal-aq", "1",
            "-rc-lookahead", "32", "-tag:v", "hvc1");
    }

    [Fact]
    public void GpuH264_UsesSameNvencParametersWithoutHevcTag()
    {
        var args = Build(Balanced with { Format = OutputFormat.Compatible, Encoder = EncoderKind.Gpu });

        AssertContainsSequence(args,
            "-c:v", "h264_nvenc", "-preset", "p6", "-tune", "hq", "-rc", "vbr", "-cq", "30", "-b:v", "0",
            "-bf", "4", "-b_ref_mode", "middle", "-spatial-aq", "1", "-temporal-aq", "1",
            "-rc-lookahead", "32");
        Assert.DoesNotContain("hvc1", args);
    }

    [Fact]
    public void CpuHevc_UsesX265Calibration()
    {
        var args = Build(Balanced with { Format = OutputFormat.Efficient, Encoder = EncoderKind.Cpu });

        AssertContainsSequence(args,
            "-c:v", "libx265", "-preset", "medium", "-crf", "18.5", "-tag:v", "hvc1",
            "-x265-params", "log-level=error");
    }

    [Fact]
    public void CpuH264_UsesX264Calibration()
    {
        var args = Build(Balanced with { Format = OutputFormat.Compatible, Encoder = EncoderKind.Cpu });

        AssertContainsSequence(args, "-c:v", "libx264", "-preset", "medium", "-crf", "19.5");
        Assert.DoesNotContain("hvc1", args);
    }

    [Fact]
    public void CommonArguments_CoverAudioCompatibilityAndProgress()
    {
        var args = Build(Balanced);

        AssertContainsSequence(args, "-i", Input);
        AssertContainsSequence(args, "-map", "0:v:0", "-map", "0:a?");
        AssertContainsSequence(args, "-pix_fmt", "yuv420p");
        AssertContainsSequence(args, "-c:a", "aac", "-b:a", "160k");
        AssertContainsSequence(args, "-movflags", "+faststart");
        AssertContainsSequence(args, "-progress", "pipe:1", "-nostats");
        Assert.Contains("-nostdin", args);
        Assert.Contains("-y", args);
        Assert.Equal(Output, args[^1]);
    }

    [Fact]
    public void NoFilters_WhenResolutionAndFpsAreOriginal()
    {
        Assert.DoesNotContain("-vf", Build(Balanced));
    }

    [Fact]
    public void Scale_OnlyWhenSourceIsTaller()
    {
        var args = Build(Balanced with { MaxHeight = 1080 });
        AssertContainsSequence(args, "-vf", "scale=-2:1080:flags=lanczos");

        Assert.DoesNotContain("-vf", Build(Balanced with { MaxHeight = 1440 }));

        var source720p = Source1440p60 with { Width = 1280, Height = 720 };
        Assert.DoesNotContain("-vf", Build(Balanced with { MaxHeight = 1080 }, source720p));
    }

    [Fact]
    public void Fps_OnlyWhenSourceIsFaster_WithVfrTolerance()
    {
        // 59.99 fps VFR source must not be "reduced" to 60.
        Assert.DoesNotContain("-vf", Build(Balanced with { MaxFps = 60 }));

        AssertContainsSequence(Build(Balanced with { MaxFps = 30 }), "-vf", "fps=30");

        var source30 = Source1440p60 with { Fps = 30 };
        Assert.DoesNotContain("-vf", Build(Balanced with { MaxFps = 60 }, source30));
    }

    [Fact]
    public void Filters_FpsBeforeScale()
    {
        var args = Build(Balanced with { MaxHeight = 720, MaxFps = 30 });
        AssertContainsSequence(args, "-vf", "fps=30,scale=-2:720:flags=lanczos");
    }

    [Fact]
    public void Paths_ArePassedAsSeparateArguments()
    {
        var args = Build(Balanced);
        Assert.Single(args, a => a == Input);
        Assert.Single(args, a => a == Output);
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
