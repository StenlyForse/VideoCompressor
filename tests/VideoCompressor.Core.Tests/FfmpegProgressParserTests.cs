using VideoCompressor.Core.Ffmpeg;

namespace VideoCompressor.Core.Tests;

public class FfmpegProgressParserTests
{
    [Fact]
    public void OutTime_IsParsedFromMicroseconds()
    {
        Assert.True(FfmpegProgressParser.TryParseOutTime("out_time_us=12500000", out var time));
        Assert.Equal(TimeSpan.FromSeconds(12.5), time);
    }

    [Fact]
    public void NegativeOutTime_IsClampedToZero()
    {
        Assert.True(FfmpegProgressParser.TryParseOutTime("out_time_us=-23220", out var time));
        Assert.Equal(TimeSpan.Zero, time);
    }

    [Theory]
    [InlineData("out_time_us=N/A")]
    [InlineData("out_time=00:00:12.500000")]
    [InlineData("frame=120")]
    [InlineData("progress=continue")]
    [InlineData("")]
    [InlineData("garbage")]
    public void OtherLines_AreIgnored(string line) =>
        Assert.False(FfmpegProgressParser.TryParseOutTime(line, out _));

    [Theory]
    [InlineData("progress=end", true)]
    [InlineData("progress=continue", false)]
    [InlineData("out_time_us=1", false)]
    public void End_IsDetected(string line, bool expected) =>
        Assert.Equal(expected, FfmpegProgressParser.IsEnd(line));
}
