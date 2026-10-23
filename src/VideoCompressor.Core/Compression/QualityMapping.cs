using System.Globalization;

namespace VideoCompressor.Core.Compression;

/// <summary>
/// Maps the compression percent to encoder quality parameters so that the same percent
/// gives roughly the same visual quality (VMAF) on every encoder. Calibration: research R-1.
/// </summary>
public static class QualityMapping
{
    /// <summary>Quality index at 0% — equals hevc_nvenc CQ.</summary>
    public const double MinIndex = 20;

    /// <summary>Quality index at 100% — equals hevc_nvenc CQ.</summary>
    public const double MaxIndex = 40;

    public static double QualityIndex(int percent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percent, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        return MinIndex + (MaxIndex - MinIndex) * percent / 100.0;
    }

    /// <summary>CQ for NVENC, CRF for x265/x264.</summary>
    public static double EncoderValue(int percent, OutputFormat format, EncoderKind encoder)
    {
        var index = QualityIndex(percent);
        return (encoder, format) switch
        {
            (EncoderKind.Gpu, _) => index,
            (EncoderKind.Cpu, OutputFormat.Efficient) => 0.9 * index - 8.5,
            (EncoderKind.Cpu, OutputFormat.Compatible) => 0.85 * index - 6,
            _ => throw new ArgumentOutOfRangeException(nameof(encoder), $"{encoder}/{format} is not supported."),
        };
    }

    /// <summary>Formats a value for the ffmpeg command line, always with a dot as the decimal separator.</summary>
    public static string FormatValue(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
