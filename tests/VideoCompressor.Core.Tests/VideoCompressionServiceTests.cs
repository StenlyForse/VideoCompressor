using VideoCompressor.Core.Compression;
using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.Media;
using VideoCompressor.Core.Tests.Infrastructure;

namespace VideoCompressor.Core.Tests;

[Collection(FfmpegCollection.Name)]
public class VideoCompressionServiceTests(FfmpegFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(OutputFormat.Efficient, "hevc")]
    [InlineData(OutputFormat.Compatible, "h264")]
    public async Task Cpu_ProducesValidFileWithAllAudioTracks(OutputFormat format, string expectedCodec)
    {
        var tools = fixture.RequireTools();
        var result = await CompressAsync(tools, fixture.ShortClip, CompressionSettings.Default with
        {
            Format = format,
            Encoder = EncoderKind.Cpu,
        });

        await AssertValidResultAsync(tools, fixture.ShortClip, result, expectedCodec);
    }

    [Fact]
    public async Task Gpu_ProducesValidFile_WhenNvencAvailable()
    {
        var tools = fixture.RequireTools();
        Assert.SkipUnless(await new EncoderDetector(tools).IsGpuAvailableAsync(Ct), "NVENC недоступен.");

        var result = await CompressAsync(tools, fixture.ShortClip, CompressionSettings.Default);

        await AssertValidResultAsync(tools, fixture.ShortClip, result, "hevc");
    }

    [Fact]
    public async Task Limits_DownscaleAndReduceFps_KeepingAspectAndDuration()
    {
        var tools = fixture.RequireTools();
        // LongClip is 640x360 @ 30 fps.
        var result = await CompressAsync(tools, fixture.LongClip, CompressionSettings.Default with
        {
            Encoder = EncoderKind.Cpu,
            MaxHeight = 240,
            MaxFps = 15,
        });

        var probe = new MediaProbe(tools);
        var original = await probe.ProbeAsync(fixture.LongClip, Ct);
        var compressed = await probe.ProbeAsync(result.OutputPath, Ct);
        Assert.Equal((426, 240), (compressed.Width, compressed.Height));
        Assert.Equal(15.0, compressed.Fps, tolerance: 0.01);
        Assert.Equal(original.Duration.TotalSeconds, compressed.Duration.TotalSeconds, tolerance: 0.1);
    }

    [Fact]
    public async Task Limits_AboveSource_NeverUpscale()
    {
        var tools = fixture.RequireTools();
        // ShortClip is 320x240 @ 30 fps; 1080p / 60 fps limits must leave it as is (US-3.2, US-3.3).
        var result = await CompressAsync(tools, fixture.ShortClip, CompressionSettings.Default with
        {
            Encoder = EncoderKind.Cpu,
            MaxHeight = 1080,
            MaxFps = 60,
        });

        var compressed = await new MediaProbe(tools).ProbeAsync(result.OutputPath, Ct);
        Assert.Equal((320, 240), (compressed.Width, compressed.Height));
        Assert.Equal(30.0, compressed.Fps, tolerance: 0.01);
    }

    [Fact]
    public async Task Progress_IsMonotonicAndEndsAtOne()
    {
        var tools = fixture.RequireTools();
        var reports = new List<CompressionProgress>();

        await CompressAsync(tools, fixture.LongClip, CompressionSettings.Default with { Encoder = EncoderKind.Cpu },
            new SyncProgress(reports.Add));

        Assert.True(reports.Count >= 2, $"Only {reports.Count} progress reports.");
        Assert.All(reports.Zip(reports.Skip(1)), pair => Assert.True(pair.Second.Fraction >= pair.First.Fraction));
        Assert.Equal(1.0, reports[^1].Fraction);
        Assert.Contains(reports, r => r.Remaining is not null);
    }

    [Fact]
    public async Task Cancel_DeletesPartialOutput()
    {
        var tools = fixture.RequireTools();
        var output = fixture.NewOutputPath();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var service = new VideoCompressionService(tools);
        var source = await new MediaProbe(tools).ProbeAsync(fixture.LongClip, Ct);

        // Cancel as soon as encoding has visibly started.
        var progress = new SyncProgress(p =>
        {
            if (p.Fraction > 0)
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CompressAsync(
            fixture.LongClip, output, source, CompressionSettings.Default with { Encoder = EncoderKind.Cpu },
            progress, cts.Token));

        Assert.False(File.Exists(output), "Partial output was left behind.");
    }

    [Fact]
    public async Task FfmpegFailure_ThrowsAndLeavesNoFile()
    {
        var tools = fixture.RequireTools();
        var output = Path.Combine(fixture.Directory, "нет такой папки", "out.mp4");
        var source = await new MediaProbe(tools).ProbeAsync(fixture.ShortClip, Ct);

        await Assert.ThrowsAsync<FfmpegException>(() => new VideoCompressionService(tools).CompressAsync(
            fixture.ShortClip, output, source, CompressionSettings.Default with { Encoder = EncoderKind.Cpu },
            cancellationToken: Ct));

        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task SameInputAndOutput_IsRejected()
    {
        var tools = fixture.RequireTools();
        var source = await new MediaProbe(tools).ProbeAsync(fixture.ShortClip, Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => new VideoCompressionService(tools).CompressAsync(
            fixture.ShortClip, fixture.ShortClip, source, CompressionSettings.Default, cancellationToken: Ct));
    }

    private async Task<CompressionResult> CompressAsync(
        FfmpegTools tools, string input, CompressionSettings settings, IProgress<CompressionProgress>? progress = null)
    {
        var source = await new MediaProbe(tools).ProbeAsync(input, Ct);
        return await new VideoCompressionService(tools).CompressAsync(
            input, fixture.NewOutputPath(), source, settings, progress, Ct);
    }

    private static async Task AssertValidResultAsync(
        FfmpegTools tools, string input, CompressionResult result, string expectedCodec)
    {
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal(new FileInfo(input).Length, result.OriginalSize);
        Assert.Equal(new FileInfo(result.OutputPath).Length, result.CompressedSize);

        var probe = new MediaProbe(tools);
        var original = await probe.ProbeAsync(input, Ct);
        var compressed = await probe.ProbeAsync(result.OutputPath, Ct);

        Assert.Equal(expectedCodec, compressed.VideoCodec);
        Assert.Equal(original.Duration.TotalSeconds, compressed.Duration.TotalSeconds, tolerance: 0.1); // FR-004
        Assert.Equal(original.AudioTrackCount, compressed.AudioTrackCount);                             // FR-003
        Assert.Equal((original.Width, original.Height), (compressed.Width, compressed.Height));
    }

    /// <summary>Reports synchronously, unlike <see cref="Progress{T}"/>, so tests see every update in order.</summary>
    private sealed class SyncProgress(Action<CompressionProgress> handler) : IProgress<CompressionProgress>
    {
        public void Report(CompressionProgress value) => handler(value);
    }
}
