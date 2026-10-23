using System.Globalization;
using VideoCompressor.Core.Compression;

namespace VideoCompressor.Core.Tests;

public class QualityMappingTests
{
    [Theory]
    [InlineData(0, 20.0)]
    [InlineData(25, 25.0)]
    [InlineData(50, 30.0)]
    [InlineData(75, 35.0)]
    [InlineData(100, 40.0)]
    public void QualityIndex_IsLinearFrom20To40(int percent, double expected) =>
        Assert.Equal(expected, QualityMapping.QualityIndex(percent), precision: 6);

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void QualityIndex_RejectsOutOfRangePercent(int percent) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => QualityMapping.QualityIndex(percent));

    [Theory]
    // research R-1 / R-2: NVENC uses the index directly for both codecs.
    [InlineData(CompressionLevel.Balanced, OutputFormat.Efficient, EncoderKind.Gpu, 30.0)]
    [InlineData(CompressionLevel.Balanced, OutputFormat.Compatible, EncoderKind.Gpu, 30.0)]
    // x265: 0.9 * I - 8.5
    [InlineData(CompressionLevel.Minimal, OutputFormat.Efficient, EncoderKind.Cpu, 14.0)]
    [InlineData(CompressionLevel.Balanced, OutputFormat.Efficient, EncoderKind.Cpu, 18.5)]
    [InlineData(CompressionLevel.Maximum, OutputFormat.Efficient, EncoderKind.Cpu, 27.5)]
    // x264: 0.85 * I - 6
    [InlineData(CompressionLevel.Balanced, OutputFormat.Compatible, EncoderKind.Cpu, 19.5)]
    [InlineData(CompressionLevel.Maximum, OutputFormat.Compatible, EncoderKind.Cpu, 28.0)]
    public void EncoderValue_MatchesCalibration(
        CompressionLevel level, OutputFormat format, EncoderKind encoder, double expected) =>
        Assert.Equal(expected, QualityMapping.EncoderValue((int)level, format, encoder), precision: 6);

    [Theory]
    [InlineData(OutputFormat.Efficient, EncoderKind.Gpu)]
    [InlineData(OutputFormat.Compatible, EncoderKind.Gpu)]
    [InlineData(OutputFormat.Efficient, EncoderKind.Cpu)]
    [InlineData(OutputFormat.Compatible, EncoderKind.Cpu)]
    public void EncoderValue_GrowsStrictlyWithPercent(OutputFormat format, EncoderKind encoder)
    {
        // Higher CQ/CRF means a smaller file, so this is spec US-2.6.
        for (var percent = 1; percent <= 100; percent++)
        {
            Assert.True(
                QualityMapping.EncoderValue(percent, format, encoder) >
                QualityMapping.EncoderValue(percent - 1, format, encoder),
                $"Not increasing at {percent}%");
        }
    }

    [Fact]
    public void FormatValue_UsesDotUnderRussianCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            Assert.Equal("18.5", QualityMapping.FormatValue(18.5));
            Assert.Equal("30", QualityMapping.FormatValue(30.0));
            Assert.Equal("20.2", QualityMapping.FormatValue(QualityMapping.QualityIndex(1)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
