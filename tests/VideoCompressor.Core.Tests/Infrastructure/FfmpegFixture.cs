using System.Diagnostics;
using VideoCompressor.Core.Ffmpeg;

namespace VideoCompressor.Core.Tests.Infrastructure;

/// <summary>
/// Locates ffmpeg once and generates test clips in a folder whose name has Cyrillic letters
/// and spaces (NFR-004). Tests using it skip themselves when ffmpeg is not installed.
/// </summary>
public sealed class FfmpegFixture : IDisposable
{
    public FfmpegFixture()
    {
        Tools = FfmpegTools.TryLocate();
        Directory = System.IO.Directory.CreateTempSubdirectory("vc тесты ").FullName;
        if (Tools is null)
        {
            return;
        }

        // 2 s, two audio tracks (like system sound + microphone): fast checks.
        ShortClip = Generate("короткий клип.mp4", seconds: 2, size: "320x240");
        // 20 s: long enough to observe progress and cancel mid-way.
        LongClip = Generate("длинный клип.mp4", seconds: 20, size: "640x360");
        // Squeezed hard (very low video quality, 32k audio): re-compressing it can only make it larger.
        AlreadyCompressedClip = Generate("уже сжатый клип.mp4", seconds: 2, size: "320x240",
            ["-c:v", "libx265", "-crf", "45", "-x265-params", "log-level=error", "-b:a", "32k"]);
    }

    public FfmpegTools? Tools { get; }

    public string Directory { get; }

    public string ShortClip { get; } = "";

    public string LongClip { get; } = "";

    public string AlreadyCompressedClip { get; } = "";

    /// <summary>Call at the start of every test that needs ffmpeg.</summary>
    public FfmpegTools RequireTools()
    {
        Assert.SkipWhen(Tools is null, "FFmpeg не найден — интеграционный тест пропущен.");
        return Tools!;
    }

    public string NewOutputPath(string name = "результат.mp4") =>
        Path.Combine(Directory, $"{Path.GetFileNameWithoutExtension(name)} {Guid.NewGuid():N}{Path.GetExtension(name)}");

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Generate(string name, int seconds, string size, string[]? codecArgs = null)
    {
        codecArgs ??= ["-c:v", "libx264", "-preset", "ultrafast"];
        var path = Path.Combine(Directory, name);
        var startInfo = new ProcessStartInfo(Tools!.FfmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        string[] args =
        [
            "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", $"testsrc2=s={size}:r=30:d={seconds}",
            "-f", "lavfi", "-i", $"sine=f=440:d={seconds}",
            "-f", "lavfi", "-i", $"sine=f=880:d={seconds}",
            "-map", "0", "-map", "1", "-map", "2",
            .. codecArgs, "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-shortest", path,
        ];
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Could not generate {name}: {error}");
        }

        return path;
    }
}

[CollectionDefinition(Name)]
public sealed class FfmpegCollection : ICollectionFixture<FfmpegFixture>
{
    public const string Name = "ffmpeg";
}
