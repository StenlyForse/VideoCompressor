using System.Globalization;
using VideoCompressor.Core.Compression;
using VideoCompressor.Core.Media;

namespace VideoCompressor.App;

/// <summary>User-facing text for sizes, durations and results. The UI is Russian on every OS (FR-007).</summary>
public static class Formatting
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Sizes use 1024² bytes per МБ, as Windows Explorer does (spec header).</summary>
    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Format(Ru, "{0:0.0} ГБ", bytes / (double)(1L << 30)),
        >= 1L << 20 => string.Format(Ru, "{0:0.0} МБ", bytes / (double)(1L << 20)),
        _ => string.Format(Ru, "{0:0} КБ", Math.Ceiling(bytes / 1024.0)),
    };

    public static string Duration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", Ru)
            : duration.ToString(@"m\:ss", Ru);

    public static string VideoSummary(VideoInfo info)
    {
        var parts = new List<string>
        {
            $"{info.Width}×{info.Height}",
            string.Format(Ru, "{0:0.##} fps", Math.Round(info.Fps, 2)),
            Duration(info.Duration),
            Size(info.SizeBytes),
            CodecName(info.VideoCodec),
        };
        return string.Join(" · ", parts);
    }

    public static string Progress(CompressionProgress progress)
    {
        var percent = (int)(progress.Fraction * 100);
        return progress.Remaining is { } remaining
            ? $"Сжатие… {percent}% · осталось ~{Duration(remaining)}"
            : $"Сжатие… {percent}%";
    }

    public static string Result(CompressionResult result)
    {
        var change = (int)Math.Round(Math.Abs(result.SavedFraction) * 100);
        var sign = result.SavedFraction >= 0 ? "−" : "+";
        return $"Готово за {Duration(result.Elapsed)}: " +
               $"{Size(result.OriginalSize)} → {Size(result.CompressedSize)} ({sign}{change}%)";
    }

    private static string CodecName(string codec) => codec switch
    {
        "h264" => "H.264",
        "hevc" => "H.265",
        "av1" => "AV1",
        _ => codec.ToUpperInvariant(),
    };
}
