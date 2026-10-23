namespace VideoCompressor.Core.Media;

public sealed record VideoInfo(
    TimeSpan Duration,
    long SizeBytes,
    int Width,
    int Height,
    double Fps,
    string VideoCodec,
    int AudioTrackCount,
    bool IsHdr);
