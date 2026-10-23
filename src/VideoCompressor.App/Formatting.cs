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

    /// <summary>Short result for a row of the queue.</summary>
    public static string ItemResult(CompressionResult result) =>
        $"Готово: {Size(result.OriginalSize)} → {Size(result.CompressedSize)} ({SavedPercent(result.OriginalSize, result.CompressedSize)})";

    public static string BatchProgress(BatchProgress progress)
    {
        var text = $"Файл {progress.CurrentIndex + 1} из {progress.Count} · {(int)(progress.Fraction * 100)}%";
        return progress.Remaining is { } remaining ? $"{text} · осталось ~{Duration(remaining)}" : text;
    }

    /// <summary>"Сжато 4 из 5: 812,3 МБ → 151,0 МБ (−81%), пропущено: 1, с ошибками: 0" (spec 003 US-1.8).</summary>
    /// <param name="notVideoCount">Files that never entered the run because they are not videos; they count as errors.</param>
    public static string BatchSummary(IReadOnlyList<BatchItemUpdate> results, int notVideoCount, TimeSpan elapsed)
    {
        var done = results.Where(r => r.State == BatchItemState.Done && r.Result is not null).ToList();
        var original = done.Sum(r => r.Result!.OriginalSize);
        var compressed = done.Sum(r => r.Result!.CompressedSize);
        var text = $"Готово за {Duration(elapsed)}: сжато {done.Count} из {results.Count + notVideoCount}";
        if (done.Count > 0)
        {
            text += $", {Size(original)} → {Size(compressed)} ({SavedPercent(original, compressed)})";
        }

        var skipped = results.Count(r => r.State == BatchItemState.SkippedNotSmaller);
        var failed = results.Count(r => r.State == BatchItemState.Failed) + notVideoCount;
        if (skipped > 0)
        {
            text += $", пропущено: {skipped}";
        }

        if (failed > 0)
        {
            text += $", с ошибками: {failed}";
        }

        return text;
    }

    private static string SavedPercent(long original, long compressed)
    {
        var saved = original > 0 ? 1 - (double)compressed / original : 0;
        var change = (int)Math.Round(Math.Abs(saved) * 100);
        return $"{(saved >= 0 ? "−" : "+")}{change}%";
    }

    private static string CodecName(string codec) => codec switch
    {
        "h264" => "H.264",
        "hevc" => "H.265",
        "av1" => "AV1",
        _ => codec.ToUpperInvariant(),
    };
}
