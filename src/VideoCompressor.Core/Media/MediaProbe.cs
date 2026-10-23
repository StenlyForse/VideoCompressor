using System.Globalization;
using System.Text.Json;
using VideoCompressor.Core.Ffmpeg;

namespace VideoCompressor.Core.Media;

public sealed class MediaProbe(FfmpegTools tools)
{
    internal const string NotAVideoMessage = "Файл повреждён или не является видео.";

    private static readonly string[] HdrTransfers = ["smpte2084", "arib-std-b67"];

    public async Task<VideoInfo> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var result = await ProcessRunner.RunAsync(
            tools.FfprobePath,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", fullPath],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new FfmpegException(NotAVideoMessage, result.ErrorText);
        }

        return Parse(result.Output, new FileInfo(fullPath).Length);
    }

    internal static VideoInfo Parse(string json, long fileSize)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var streams = root.TryGetProperty("streams", out var s) ? s.EnumerateArray().ToList() : [];

            // Cover art in MP4/MKV is a "video" stream too, marked as attached_pic.
            var video = streams.FirstOrDefault(st =>
                GetString(st, "codec_type") == "video" && !IsAttachedPicture(st));
            if (video.ValueKind == JsonValueKind.Undefined)
            {
                throw new FfmpegException(NotAVideoMessage, "No video stream.");
            }

            var duration =
                ParseSeconds(root.TryGetProperty("format", out var format) ? GetString(format, "duration") : null)
                ?? ParseSeconds(GetString(video, "duration"))
                ?? throw new FfmpegException(NotAVideoMessage, "Unknown duration.");

            var fps = ParseRate(GetString(video, "avg_frame_rate")) ?? ParseRate(GetString(video, "r_frame_rate")) ?? 0;

            return new VideoInfo(
                Duration: TimeSpan.FromSeconds(duration),
                SizeBytes: fileSize,
                Width: video.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
                Height: video.TryGetProperty("height", out var h) ? h.GetInt32() : 0,
                Fps: fps,
                VideoCodec: GetString(video, "codec_name") ?? "?",
                AudioTrackCount: streams.Count(st => GetString(st, "codec_type") == "audio"),
                IsHdr: GetString(video, "color_transfer") is { } transfer && HdrTransfers.Contains(transfer));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new FfmpegException(NotAVideoMessage, ex.Message, ex);
        }
    }

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var d) &&
        d.TryGetProperty("attached_pic", out var a) &&
        a.GetInt32() == 1;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? ParseSeconds(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? seconds
            : null;

    /// <summary>"60000/1001" → 59.94; "0/0" → null.</summary>
    private static double? ParseRate(string? value)
    {
        var parts = value?.Split('/');
        if (parts is not [var num, var den] ||
            !double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ||
            !double.TryParse(den, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ||
            n <= 0 || d <= 0)
        {
            return null;
        }

        return n / d;
    }
}
