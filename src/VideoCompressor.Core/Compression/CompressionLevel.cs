namespace VideoCompressor.Core.Compression;

/// <summary>Named compression levels; each value is its point on the percent slider (research R-2).</summary>
public enum CompressionLevel
{
    Minimal = 25,
    Balanced = 50,
    Strong = 75,
    Maximum = 100,
}
