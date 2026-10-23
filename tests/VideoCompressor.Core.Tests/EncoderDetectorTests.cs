using VideoCompressor.Core.Compression;
using VideoCompressor.Core.Tests.Infrastructure;

namespace VideoCompressor.Core.Tests;

[Collection(FfmpegCollection.Name)]
public class EncoderDetectorTests(FfmpegFixture fixture)
{
    [Fact]
    public async Task SoftwareEncoder_IsAvailable()
    {
        var detector = new EncoderDetector(fixture.RequireTools());
        Assert.True(await detector.IsEncoderAvailableAsync("libx264", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnknownEncoder_IsNotAvailable()
    {
        var detector = new EncoderDetector(fixture.RequireTools());
        Assert.False(await detector.IsEncoderAvailableAsync("no_such_encoder", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GpuCheck_CompletesWithoutThrowing()
    {
        // The answer depends on the machine; the check itself must never fail.
        var detector = new EncoderDetector(fixture.RequireTools());
        _ = await detector.IsGpuAvailableAsync(TestContext.Current.CancellationToken);
    }
}
