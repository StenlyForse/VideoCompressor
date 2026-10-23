using VideoCompressor.Core.Compression;
using VideoCompressor.Core.Media;

namespace VideoCompressor.Core.Ffmpeg;

/// <summary>
/// Turns settings into an ffmpeg command line. Pure function; parameters come from research R-3.
/// </summary>
public static class FfmpegArgumentsBuilder
{
    public const int AudioBitrateKbps = 160;

    // VFR recordings report e.g. 59.99 fps; that must not count as "faster than 60".
    private const double FpsTolerance = 0.5;

    public static IReadOnlyList<string> Build(
        string inputPath, string outputPath, VideoInfo source, CompressionSettings settings)
    {
        List<string> args =
        [
            "-hide_banner", "-nostdin", "-y",
            "-i", inputPath,
            "-map", "0:v:0", "-map", "0:a?",
        ];

        var filters = BuildFilters(source, settings);
        if (filters.Count > 0)
        {
            args.AddRange(["-vf", string.Join(',', filters)]);
        }

        args.AddRange(BuildVideoCodecArguments(settings));
        args.AddRange(
        [
            "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", $"{AudioBitrateKbps}k",
            "-movflags", "+faststart",
            "-progress", "pipe:1", "-nostats",
            outputPath,
        ]);
        return args;
    }

    private static List<string> BuildFilters(VideoInfo source, CompressionSettings settings)
    {
        // fps first, so that only the remaining frames get scaled.
        List<string> filters = [];
        if (settings.MaxFps is int maxFps && source.Fps > maxFps + FpsTolerance)
        {
            filters.Add($"fps={maxFps}");
        }

        if (settings.MaxHeight is int maxHeight && source.Height > maxHeight)
        {
            filters.Add($"scale=-2:{maxHeight}:flags=lanczos");
        }

        return filters;
    }

    /// <summary>The -c:v part only; also used by the GPU check so that it tests exactly these options.</summary>
    internal static string[] BuildVideoCodecArguments(CompressionSettings settings)
    {
        var quality = QualityMapping.FormatValue(
            QualityMapping.EncoderValue(settings.CompressionPercent, settings.Format, settings.Encoder));

        return (settings.Encoder, settings.Format) switch
        {
            (EncoderKind.Gpu, OutputFormat.Efficient) =>
                ["-c:v", "hevc_nvenc", .. NvencArguments(quality), "-tag:v", "hvc1"],
            (EncoderKind.Gpu, OutputFormat.Compatible) =>
                ["-c:v", "h264_nvenc", .. NvencArguments(quality)],
            (EncoderKind.Cpu, OutputFormat.Efficient) =>
                ["-c:v", "libx265", "-preset", "medium", "-crf", quality, "-tag:v", "hvc1",
                 "-x265-params", "log-level=error"],
            (EncoderKind.Cpu, OutputFormat.Compatible) =>
                ["-c:v", "libx264", "-preset", "medium", "-crf", quality],
            _ => throw new ArgumentOutOfRangeException(
                nameof(settings), $"{settings.Encoder}/{settings.Format} is not supported."),
        };
    }

    private static string[] NvencArguments(string cq) =>
    [
        "-preset", "p6", "-tune", "hq",
        "-rc", "vbr", "-cq", cq, "-b:v", "0",
        "-bf", "4", "-b_ref_mode", "middle",
        "-spatial-aq", "1", "-temporal-aq", "1",
        "-rc-lookahead", "32",
    ];
}
