namespace VideoCompressor.Core.Compression;

public enum OutputFormat
{
    /// <summary>HEVC (H.265): smallest file.</summary>
    Efficient,

    /// <summary>H.264: plays everywhere, larger file.</summary>
    Compatible,
}

public enum EncoderKind
{
    /// <summary>Hardware encoding; NVIDIA NVENC in v1.</summary>
    Gpu,
    Cpu,
}

/// <param name="CompressionPercent">0 = best quality, 100 = smallest file (spec US-2.3).</param>
/// <param name="MaxHeight">Downscale limit; <c>null</c> keeps the source resolution.</param>
/// <param name="MaxFps">Frame-rate limit; <c>null</c> keeps the source frame rate.</param>
public sealed record CompressionSettings(
    int CompressionPercent,
    OutputFormat Format,
    EncoderKind Encoder,
    int? MaxHeight = null,
    int? MaxFps = null)
{
    public static CompressionSettings Default { get; } =
        new((int)CompressionLevel.Balanced, OutputFormat.Efficient, EncoderKind.Gpu);
}
