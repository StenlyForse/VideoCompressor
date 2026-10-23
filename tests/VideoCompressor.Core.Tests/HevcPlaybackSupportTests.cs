using VideoCompressor.Core.Platform;

namespace VideoCompressor.Core.Tests;

public class HevcPlaybackSupportTests
{
    [Fact]
    public void Check_NeverThrows()
    {
        // The answer depends on the machine (codecs installed); the check itself must always work.
        _ = HevcPlaybackSupport.IsSupported();
    }

    [Fact]
    public void Check_IsStable()
    {
        Assert.Equal(HevcPlaybackSupport.IsSupported(), HevcPlaybackSupport.IsSupported());
    }
}
