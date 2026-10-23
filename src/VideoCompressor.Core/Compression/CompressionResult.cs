namespace VideoCompressor.Core.Compression;

/// <param name="Fraction">Share of the video processed, 0..1.</param>
/// <param name="Remaining">Estimated time left; <c>null</c> until there is enough data.</param>
public readonly record struct CompressionProgress(double Fraction, TimeSpan Elapsed, TimeSpan? Remaining);

public sealed record CompressionResult(string OutputPath, long OriginalSize, long CompressedSize, TimeSpan Elapsed)
{
    /// <summary>Share of the original size saved; negative when the result is larger.</summary>
    public double SavedFraction => OriginalSize > 0 ? 1 - (double)CompressedSize / OriginalSize : 0;

    public bool IsNotSmaller => CompressedSize >= OriginalSize;
}
