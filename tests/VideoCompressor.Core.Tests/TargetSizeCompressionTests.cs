using VideoCompressor.Core.Compression;
using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.Media;
using VideoCompressor.Core.Tests.Infrastructure;

namespace VideoCompressor.Core.Tests;

[Collection(FfmpegCollection.Name)]
public class TargetSizeCompressionTests(FfmpegFixture fixture)
{
    // 20 s, 2 audio tracks: 2 MB leaves ~490 kbit/s for the video.
    private const long Target = 2L * 1024 * 1024;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string PassLogRoot => Path.Combine(Path.GetTempPath(), "VideoCompressor");

    [Theory]
    [InlineData(OutputFormat.Compatible)]
    [InlineData(OutputFormat.Efficient)]
    public async Task Cpu_TwoPasses_FitTheTarget(OutputFormat format)
    {
        var tools = fixture.RequireTools();
        var settings = CompressionSettings.Default with { Encoder = EncoderKind.Cpu, Format = format, TargetSizeBytes = Target };

        var (result, reports) = await CompressLongClipAsync(tools, settings);

        AssertFits(result);
        // Two passes report one continuous 0..1 progress.
        Assert.All(reports.Zip(reports.Skip(1)), p => Assert.True(p.Second.Fraction >= p.First.Fraction));
        Assert.Contains(reports, r => r.Fraction is > 0 and < 0.5);
        Assert.Contains(reports, r => r.Fraction is > 0.5 and < 1);
        Assert.Equal(1.0, reports[^1].Fraction);
        AssertNoPassLogsLeft();
    }

    [Fact]
    public async Task Gpu_FitsTheTarget_WhenNvencAvailable()
    {
        var tools = fixture.RequireTools();
        Assert.SkipUnless(await new EncoderDetector(tools).IsGpuAvailableAsync(Ct), "NVENC недоступен.");

        var (result, _) = await CompressLongClipAsync(tools, CompressionSettings.Default with { TargetSizeBytes = Target });

        AssertFits(result);
    }

    [Theory]
    [InlineData(0.2)]   // during pass 1
    [InlineData(0.7)]   // during pass 2
    public async Task Cancel_InEitherPass_LeavesNothing(double cancelAt)
    {
        var tools = fixture.RequireTools();
        var source = await new MediaProbe(tools).ProbeAsync(fixture.LongClip, Ct);
        var output = fixture.NewOutputPath();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var progress = new SyncProgress(p =>
        {
            if (p.Fraction >= cancelAt)
            {
                cts.Cancel();
            }
        });
        var settings = CompressionSettings.Default with { Encoder = EncoderKind.Cpu, TargetSizeBytes = Target };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new VideoCompressionService(tools)
            .CompressAsync(fixture.LongClip, output, source, settings, progress, cts.Token));

        Assert.False(File.Exists(output));
        AssertNoPassLogsLeft();
    }

    [Fact]
    public async Task ImpossibleTarget_FailsWithoutFile()
    {
        var tools = fixture.RequireTools();
        var source = await new MediaProbe(tools).ProbeAsync(fixture.LongClip, Ct);
        var output = fixture.NewOutputPath();
        var settings = CompressionSettings.Default with { Encoder = EncoderKind.Cpu, TargetSizeBytes = 10 * 1024 };

        await Assert.ThrowsAsync<FfmpegException>(() => new VideoCompressionService(tools)
            .CompressAsync(fixture.LongClip, output, source, settings, cancellationToken: Ct));

        Assert.False(File.Exists(output));
    }

    private async Task<(CompressionResult Result, List<CompressionProgress> Reports)> CompressLongClipAsync(
        FfmpegTools tools, CompressionSettings settings)
    {
        var source = await new MediaProbe(tools).ProbeAsync(fixture.LongClip, Ct);
        var reports = new List<CompressionProgress>();
        var result = await new VideoCompressionService(tools).CompressAsync(
            fixture.LongClip, fixture.NewOutputPath(), source, settings, new SyncProgress(reports.Add), Ct);
        return (result, reports);
    }

    private static void AssertFits(CompressionResult result)
    {
        Assert.True(result.CompressedSize <= Target, $"{result.CompressedSize} > {Target}");
        // The synthetic clip is easy to encode, so encoders may undershoot more than on real footage;
        // the 85% bound of spec 002 is checked on the reference recording.
        Assert.True(result.CompressedSize >= Target * 0.6, $"{result.CompressedSize} is far below {Target}");
    }

    private static void AssertNoPassLogsLeft() =>
        Assert.True(!Directory.Exists(PassLogRoot) || Directory.GetFileSystemEntries(PassLogRoot).Length == 0,
            "First-pass statistics were left in the temp folder.");

    private sealed class SyncProgress(Action<CompressionProgress> handler) : IProgress<CompressionProgress>
    {
        public void Report(CompressionProgress value) => handler(value);
    }
}
