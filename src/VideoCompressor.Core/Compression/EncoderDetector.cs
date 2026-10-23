using VideoCompressor.Core.Ffmpeg;

namespace VideoCompressor.Core.Compression;

public sealed class EncoderDetector(FfmpegTools tools)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Whether hardware encoding (NVENC in v1) works on this machine (spec US-4.1).</summary>
    public Task<bool> IsGpuAvailableAsync(CancellationToken cancellationToken = default) =>
        OperatingSystem.IsMacOS()
            ? Task.FromResult(false)
            : IsEncoderAvailableAsync("hevc_nvenc", cancellationToken);

    /// <summary>
    /// Encodes a tiny synthetic clip: an encoder listed by ffmpeg may still fail at runtime
    /// (no GPU, old driver), so only a real encode is a reliable answer.
    /// </summary>
    public async Task<bool> IsEncoderAvailableAsync(string encoder, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var result = await ProcessRunner.RunAsync(
                tools.FfmpegPath,
                ["-hide_banner", "-nostdin", "-loglevel", "error",
                 "-f", "lavfi", "-i", "color=c=black:s=256x256:d=0.1",
                 "-c:v", encoder, "-f", "null", "-"],
                onOutputLine: _ => { },
                cancellationToken: timeout.Token).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (FfmpegException)
        {
            return false;
        }
    }
}
