using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.Media;

namespace VideoCompressor.Core.Compression;

public enum TargetVerdict
{
    Ok,

    /// <summary>The source already fits (spec 002 US-2).</summary>
    AlreadySmaller,

    /// <summary>Fits, but the picture will be visibly bad (spec 002 US-3).</summary>
    LowQuality,

    /// <summary>Cannot fit at all, e.g. the audio alone is larger (spec 002 US-3.4).</summary>
    Impossible,
}

/// <summary>Limits to apply instead of the current ones (null height = keep the current resolution limit).</summary>
public sealed record TargetSuggestion(int? MaxHeight, int MaxFps);

public sealed record TargetPlan(
    int VideoBitrateKbps,
    double BitsPerPixel,
    TargetVerdict Verdict,
    TargetSuggestion? Suggestion = null,
    long? MinReasonableSizeBytes = null);

/// <summary>Bitrate and feasibility of a target file size (research 002 R-1, R-5, R-6).</summary>
public static class TargetSizePlanner
{
    /// <summary>Share of the target used for audio and video; the rest absorbs container overhead.</summary>
    public const double BudgetShare = 0.97;

    public const int MinVideoBitrateKbps = 50;

    private const long MiB = 1024 * 1024;

    // research 002 §3: below this many bits per pixel losses are clearly visible.
    public static double MinBitsPerPixel(OutputFormat format) => format == OutputFormat.Efficient ? 0.008 : 0.012;

    public static TargetPlan Plan(VideoInfo source, CompressionSettings settings)
    {
        if (settings.TargetSizeBytes is not long target)
        {
            throw new ArgumentException("Target size mode is off.", nameof(settings));
        }

        var kbps = VideoBitrateKbps(target, source);
        var geometry = OutputGeometry.Of(source, settings);
        var bpp = BitsPerPixel(kbps, geometry);

        if (source.SizeBytes <= target)
        {
            return new TargetPlan(kbps, bpp, TargetVerdict.AlreadySmaller);
        }

        if (kbps < MinVideoBitrateKbps)
        {
            return new TargetPlan(kbps, bpp, TargetVerdict.Impossible);
        }

        var threshold = MinBitsPerPixel(settings.Format);
        if (bpp >= threshold)
        {
            return new TargetPlan(kbps, bpp, TargetVerdict.Ok);
        }

        // Fewer frames first, then smaller frames step by step (research 002 §3).
        TargetSuggestion[] candidates =
        [
            new(settings.MaxHeight, 30),
            new(1080, 30),
            new(720, 30),
        ];
        foreach (var candidate in candidates)
        {
            var candidateGeometry = OutputGeometry.Of(source, candidate.MaxHeight, candidate.MaxFps);
            if (SameFrames(candidateGeometry, geometry) || BitsPerPixel(kbps, candidateGeometry) < threshold)
            {
                continue;
            }

            return new TargetPlan(kbps, bpp, TargetVerdict.LowQuality, Suggestion: candidate);
        }

        return new TargetPlan(kbps, bpp, TargetVerdict.LowQuality, MinReasonableSizeBytes: MinimalSize(source, geometry, threshold));
    }

    /// <summary>Video bitrate that fills <see cref="BudgetShare"/> of the target together with all audio tracks.</summary>
    public static int VideoBitrateKbps(long targetBytes, VideoInfo source)
    {
        var seconds = source.Duration.TotalSeconds;
        if (seconds <= 0)
        {
            return 0;
        }

        var budgetBitsPerSecond = targetBytes * 8.0 * BudgetShare / seconds;
        var audioBitsPerSecond = FfmpegArgumentsBuilder.AudioBitrateKbps * 1000.0 * source.AudioTrackCount;
        return (int)Math.Floor((budgetBitsPerSecond - audioBitsPerSecond) / 1000);
    }

    private static double BitsPerPixel(int kbps, OutputGeometry geometry)
    {
        var pixelsPerSecond = (double)geometry.Width * geometry.Height * geometry.Fps;
        return pixelsPerSecond > 0 ? Math.Max(kbps, 0) * 1000.0 / pixelsPerSecond : 0;
    }

    private static bool SameFrames(OutputGeometry a, OutputGeometry b) =>
        a.Width == b.Width && a.Height == b.Height && Math.Abs(a.Fps - b.Fps) < 0.5;

    /// <summary>Smallest whole-MB target that reaches the quality threshold with the current limits.</summary>
    private static long MinimalSize(VideoInfo source, OutputGeometry geometry, double threshold)
    {
        var videoBitsPerSecond = threshold * geometry.Width * geometry.Height * geometry.Fps;
        var audioBitsPerSecond = FfmpegArgumentsBuilder.AudioBitrateKbps * 1000.0 * source.AudioTrackCount;
        var bytes = (videoBitsPerSecond + audioBitsPerSecond) * source.Duration.TotalSeconds / 8 / BudgetShare;
        // One extra MB covers the rounding down of the bitrate.
        return ((long)Math.Ceiling(bytes / MiB) + 1) * MiB;
    }
}
