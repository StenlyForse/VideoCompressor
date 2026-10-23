using VideoCompressor.Core.Ffmpeg;

namespace VideoCompressor.Core.Compression;

public sealed class EncoderDetector(FfmpegTools tools)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether hardware encoding (NVENC in v1) works on this machine with the options we really use (spec US-4.1).
    /// </summary>
    /// <remarks>
    /// HEVC B-frames (<c>-bf</c>, <c>-b_ref_mode</c>) need Turing or newer, so a plain <c>hevc_nvenc</c>
    /// test would pass on a GTX 10xx and the real encode would then fail.
    /// </remarks>
    public Task<bool> IsGpuAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsMacOS())
        {
            return Task.FromResult(false);
        }

        var settings = CompressionSettings.Default with { Format = OutputFormat.Efficient, Encoder = EncoderKind.Gpu };
        return IsEncoderAvailableAsync(FfmpegArgumentsBuilder.BuildVideoCodecArguments(settings), cancellationToken);
    }

    public Task<bool> IsEncoderAvailableAsync(string encoder, CancellationToken cancellationToken = default) =>
        IsEncoderAvailableAsync(["-c:v", encoder], cancellationToken);

    /// <summary>
    /// Encodes a tiny synthetic clip: an encoder listed by ffmpeg may still fail at runtime
    /// (no GPU, old driver, unsupported option), so only a real encode is a reliable answer.
    /// </summary>
    private async Task<bool> IsEncoderAvailableAsync(string[] codecArguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var result = await ProcessRunner.RunAsync(
                tools.FfmpegPath,
                ["-hide_banner", "-nostdin", "-loglevel", "error",
                 "-f", "lavfi", "-i", "color=c=black:s=256x256:d=0.1",
                 .. codecArguments, "-pix_fmt", "yuv420p", "-f", "null", "-"],
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
