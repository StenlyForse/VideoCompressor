using VideoCompressor.Core.Media;

namespace VideoCompressor.Core.Compression;

/// <summary>
/// The frame size and rate the result will have after the optional limits (spec 001 US-3).
/// Shared by the ffmpeg filters and the target-size estimate, so the two never disagree.
/// </summary>
public readonly record struct OutputGeometry(int Width, int Height, double Fps, bool Scales, bool ChangesFps)
{
    // VFR recordings report e.g. 59.99 fps; that must not count as "faster than 60".
    private const double FpsTolerance = 0.5;

    public static OutputGeometry Of(VideoInfo source, int? maxHeight, int? maxFps)
    {
        var changesFps = maxFps is int f && source.Fps > f + FpsTolerance;
        var scales = maxHeight is int h && source.Height > h;

        var height = scales ? maxHeight!.Value : source.Height;
        // Mirrors "scale=-2:H": keep the aspect ratio and round the width to an even number.
        var width = scales && source.Height > 0
            ? (int)Math.Round(source.Width * (double)height / source.Height / 2) * 2
            : source.Width;

        return new OutputGeometry(width, height, changesFps ? maxFps!.Value : source.Fps, scales, changesFps);
    }

    public static OutputGeometry Of(VideoInfo source, CompressionSettings settings) =>
        Of(source, settings.MaxHeight, settings.MaxFps);
}
