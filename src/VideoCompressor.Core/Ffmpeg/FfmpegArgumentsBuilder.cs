using VideoCompressor.Core.Compression;
using VideoCompressor.Core.Media;

namespace VideoCompressor.Core.Ffmpeg;

/// <summary>
/// Turns settings into an ffmpeg command line. Pure function; parameters come from research 001 R-3
/// and, for a target size, research 002 R-2, R-3.
/// </summary>
public static class FfmpegArgumentsBuilder
{
    public const int AudioBitrateKbps = 160;

    /// <summary>CPU encoders need two passes to hit a target size (research 002 R-3).</summary>
    public static bool NeedsTwoPasses(CompressionSettings settings) =>
        settings.TargetSizeBytes is not null && settings.Encoder == EncoderKind.Cpu;

    /// <summary>A single-pass command line. For a two-pass encode use <see cref="BuildPasses"/>.</summary>
    /// <param name="videoBitrateKbps">Target mode only: overrides the planned bitrate (used for the size correction).</param>
    public static IReadOnlyList<string> Build(
        string inputPath, string outputPath, VideoInfo source, CompressionSettings settings, int? videoBitrateKbps = null)
    {
        if (NeedsTwoPasses(settings))
        {
            throw new InvalidOperationException("This encode needs two passes; use BuildPasses.");
        }

        return BuildFinal(inputPath, outputPath, source, settings, videoBitrateKbps, passArguments: []);
    }

    /// <summary>All ffmpeg runs for the encode, in order: one, or two for a CPU target-size encode.</summary>
    /// <param name="passLogPrefix">Where the encoder keeps its first-pass statistics; required for two passes.</param>
    public static IReadOnlyList<IReadOnlyList<string>> BuildPasses(
        string inputPath, string outputPath, VideoInfo source, CompressionSettings settings,
        string? passLogPrefix = null, int? videoBitrateKbps = null)
    {
        if (!NeedsTwoPasses(settings))
        {
            return [Build(inputPath, outputPath, source, settings, videoBitrateKbps)];
        }

        ArgumentNullException.ThrowIfNull(passLogPrefix);
        var kbps = videoBitrateKbps ?? TargetBitrate(source, settings);

        // Pass 1 only analyses the video: no audio, no file written.
        List<string> first =
        [
            "-hide_banner", "-nostdin", "-y",
            "-i", inputPath,
            "-map", "0:v:0",
            .. FilterArguments(source, settings),
            .. BuildVideoCodecArguments(settings, kbps),
            "-pass", "1", "-passlogfile", passLogPrefix,
            "-an", "-pix_fmt", "yuv420p",
            "-progress", "pipe:1", "-nostats",
            "-f", "null", OperatingSystem.IsWindows() ? "NUL" : "/dev/null",
        ];
        var second = BuildFinal(inputPath, outputPath, source, settings, kbps,
            passArguments: ["-pass", "2", "-passlogfile", passLogPrefix]);
        return [first, second];
    }

    private static List<string> BuildFinal(
        string inputPath, string outputPath, VideoInfo source, CompressionSettings settings,
        int? videoBitrateKbps, string[] passArguments)
    {
        int? kbps = settings.TargetSizeBytes is null ? null : videoBitrateKbps ?? TargetBitrate(source, settings);
        return
        [
            "-hide_banner", "-nostdin", "-y",
            "-i", inputPath,
            "-map", "0:v:0", "-map", "0:a?",
            .. FilterArguments(source, settings),
            .. BuildVideoCodecArguments(settings, kbps),
            .. passArguments,
            "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", $"{AudioBitrateKbps}k",
            "-movflags", "+faststart",
            "-progress", "pipe:1", "-nostats",
            outputPath,
        ];
    }

    private static int TargetBitrate(VideoInfo source, CompressionSettings settings) =>
        TargetSizePlanner.VideoBitrateKbps(settings.TargetSizeBytes!.Value, source);

    private static string[] FilterArguments(VideoInfo source, CompressionSettings settings)
    {
        // fps first, so that only the remaining frames get scaled.
        var geometry = OutputGeometry.Of(source, settings);
        List<string> filters = [];
        if (geometry.ChangesFps)
        {
            filters.Add($"fps={settings.MaxFps}");
        }

        if (geometry.Scales)
        {
            filters.Add($"scale=-2:{settings.MaxHeight}:flags=lanczos");
        }

        return filters.Count > 0 ? ["-vf", string.Join(',', filters)] : [];
    }

    /// <summary>The -c:v part only; also used by the GPU check so that it tests exactly these options.</summary>
    /// <param name="targetKbps">Video bitrate in target-size mode; ignored in quality mode.</param>
    internal static string[] BuildVideoCodecArguments(CompressionSettings settings, int? targetKbps = null)
    {
        if (settings.TargetSizeBytes is not null && targetKbps is null)
        {
            throw new ArgumentException("Target-size mode needs a bitrate.", nameof(targetKbps));
        }

        var quality = QualityMapping.FormatValue(
            QualityMapping.EncoderValue(settings.CompressionPercent, settings.Format, settings.Encoder));
        string[] cpuRate = targetKbps is int k ? ["-b:v", $"{k}k"] : ["-crf", quality];

        return (settings.Encoder, settings.Format) switch
        {
            (EncoderKind.Gpu, OutputFormat.Efficient) =>
                ["-c:v", "hevc_nvenc", .. NvencArguments(quality, targetKbps), "-tag:v", "hvc1"],
            (EncoderKind.Gpu, OutputFormat.Compatible) =>
                ["-c:v", "h264_nvenc", .. NvencArguments(quality, targetKbps)],
            (EncoderKind.Cpu, OutputFormat.Efficient) =>
                ["-c:v", "libx265", "-preset", "medium", .. cpuRate, "-tag:v", "hvc1",
                 "-x265-params", "log-level=error"],
            (EncoderKind.Cpu, OutputFormat.Compatible) =>
                ["-c:v", "libx264", "-preset", "medium", .. cpuRate],
            _ => throw new ArgumentOutOfRangeException(
                nameof(settings), $"{settings.Encoder}/{settings.Format} is not supported."),
        };
    }

    private static string[] NvencArguments(string cq, int? targetKbps) =>
    [
        "-preset", "p6", "-tune", "hq",
        // Target size: VBR capped at the average bitrate — the only NVENC mode that never overshot (research 002 §2).
        .. targetKbps is int k
            ? new[] { "-rc", "vbr", "-b:v", $"{k}k", "-maxrate", $"{k}k", "-bufsize", $"{2 * k}k", "-multipass", "fullres" }
            : ["-rc", "vbr", "-cq", cq, "-b:v", "0"],
        "-bf", "4", "-b_ref_mode", "middle",
        "-spatial-aq", "1", "-temporal-aq", "1",
        "-rc-lookahead", "32",
    ];
}
