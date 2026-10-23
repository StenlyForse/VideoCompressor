namespace VideoCompressor.Core.Ffmpeg;

/// <summary>
/// A failure of ffmpeg/ffprobe. <see cref="Exception.Message"/> is shown to the user as is,
/// <see cref="Details"/> carries the technical tail of the tool's output.
/// </summary>
public sealed class FfmpegException(string message, string? details = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string? Details { get; } = details;
}
