using System.Globalization;

namespace VideoCompressor.Core.Ffmpeg;

/// <summary>Parses the key=value lines that <c>ffmpeg -progress pipe:1</c> writes to stdout.</summary>
public static class FfmpegProgressParser
{
    private const string OutTimeKey = "out_time_us=";

    public static bool TryParseOutTime(string line, out TimeSpan time)
    {
        time = default;
        if (!line.StartsWith(OutTimeKey, StringComparison.Ordinal) ||
            !long.TryParse(line.AsSpan(OutTimeKey.Length), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var microseconds))
        {
            return false;
        }

        // ffmpeg reports small negative values before the first frame.
        time = TimeSpan.FromMicroseconds(Math.Max(0, microseconds));
        return true;
    }

    public static bool IsEnd(string line) => line == "progress=end";
}
