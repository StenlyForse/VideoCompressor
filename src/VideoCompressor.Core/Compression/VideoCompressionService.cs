using System.Diagnostics;
using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.IO;
using VideoCompressor.Core.Media;

namespace VideoCompressor.Core.Compression;

public sealed class VideoCompressionService(FfmpegTools tools)
{
    // Below this share the time estimate is too noisy to show.
    private const double MinFractionForEstimate = 0.01;

    public async Task<CompressionResult> CompressAsync(
        string inputPath,
        string outputPath,
        VideoInfo source,
        CompressionSettings settings,
        IProgress<CompressionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        inputPath = Path.GetFullPath(inputPath);
        outputPath = Path.GetFullPath(outputPath);
        if (OutputPathResolver.IsSamePath(inputPath, outputPath))
        {
            throw new ArgumentException("Путь результата совпадает с исходным файлом.", nameof(outputPath));
        }

        var arguments = FfmpegArgumentsBuilder.Build(inputPath, outputPath, source, settings);
        var outputMark = OutputFileMark.Capture(outputPath);
        var stopwatch = Stopwatch.StartNew();
        var lastFraction = 0.0;

        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(
                tools.FfmpegPath, arguments, OnOutputLine, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await outputMark.DeleteIfWrittenAsync().ConfigureAwait(false);
            throw;
        }

        if (result.ExitCode != 0)
        {
            await outputMark.DeleteIfWrittenAsync().ConfigureAwait(false);
            throw CreateError(result.ErrorText);
        }

        progress?.Report(new CompressionProgress(1, stopwatch.Elapsed, TimeSpan.Zero));
        return new CompressionResult(
            outputPath, new FileInfo(inputPath).Length, new FileInfo(outputPath).Length, stopwatch.Elapsed);

        void OnOutputLine(string line)
        {
            if (progress is null ||
                source.Duration <= TimeSpan.Zero ||
                !FfmpegProgressParser.TryParseOutTime(line, out var outTime))
            {
                return;
            }

            var fraction = Math.Max(lastFraction, Math.Clamp(outTime / source.Duration, 0, 1));
            lastFraction = fraction;
            var elapsed = stopwatch.Elapsed;
            TimeSpan? remaining = fraction >= MinFractionForEstimate ? elapsed * ((1 - fraction) / fraction) : null;
            progress.Report(new CompressionProgress(fraction, elapsed, remaining));
        }
    }

    private static FfmpegException CreateError(string details)
    {
        if (details.Contains("No space left on device", StringComparison.OrdinalIgnoreCase))
        {
            return new FfmpegException("Недостаточно места на диске.", details);
        }

        if (details.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
        {
            return new FfmpegException(
                "Видеокарта не смогла закодировать видео. Попробуйте кодирование на процессоре.", details);
        }

        return new FfmpegException("Не удалось сжать видео.", details);
    }

    /// <summary>
    /// Remembers whether the output existed before the run, so that cleanup removes only
    /// what ffmpeg wrote and never a file it did not touch.
    /// </summary>
    private readonly record struct OutputFileMark(string FilePath, DateTime? LastWriteUtc)
    {
        public static OutputFileMark Capture(string path) =>
            new(path, File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null);

        public Task DeleteIfWrittenAsync() =>
            File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != LastWriteUtc
                ? FileCleanup.DeleteWithRetryAsync(FilePath)
                : Task.CompletedTask;
    }
}
