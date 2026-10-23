using System.Diagnostics;
using System.Globalization;
using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.IO;
using VideoCompressor.Core.Media;

namespace VideoCompressor.Core.Compression;

public sealed class VideoCompressionService(FfmpegTools tools)
{
    // research 002 R-4: if a target-size result still overshoots, encode once more with a corrected bitrate.
    private const int MaxTargetAttempts = 2;

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

        var outputMark = OutputFileMark.Capture(outputPath);
        var tracker = new ProgressTracker(progress, source.Duration);
        try
        {
            if (settings.TargetSizeBytes is long target)
            {
                await EncodeToTargetAsync(inputPath, outputPath, source, settings, target, tracker, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await EncodeAsync(inputPath, outputPath, source, settings, null, tracker, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            await outputMark.DeleteIfWrittenAsync().ConfigureAwait(false);
            throw;
        }

        tracker.Complete();
        return new CompressionResult(
            outputPath, new FileInfo(inputPath).Length, new FileInfo(outputPath).Length, tracker.Elapsed);
    }

    private async Task EncodeToTargetAsync(
        string inputPath, string outputPath, VideoInfo source, CompressionSettings settings, long target,
        ProgressTracker tracker, CancellationToken cancellationToken)
    {
        var plan = TargetSizePlanner.Plan(source, settings);
        if (plan.Verdict == TargetVerdict.Impossible)
        {
            throw new FfmpegException(
                $"В {Mb(target)} это видео не уместить: звук и минимум для картинки занимают больше. Выберите размер побольше.");
        }

        var kbps = plan.VideoBitrateKbps;
        for (var attempt = 1; ; attempt++)
        {
            await EncodeAsync(inputPath, outputPath, source, settings, kbps, tracker, cancellationToken)
                .ConfigureAwait(false);

            var size = new FileInfo(outputPath).Length;
            if (size <= target)
            {
                return;
            }

            if (attempt == MaxTargetAttempts)
            {
                throw new FfmpegException(
                    $"Не удалось уложиться в {Mb(target)}: получилось {Mb(size)}. " +
                    "Выберите размер побольше или уменьшите разрешение и частоту кадров.");
            }

            kbps = (int)(kbps * (double)target / size * TargetSizePlanner.BudgetShare);
            tracker.Restart();
        }
    }

    /// <summary>Runs every ffmpeg pass of one encode; two passes share the 0..1 progress range.</summary>
    private async Task EncodeAsync(
        string inputPath, string outputPath, VideoInfo source, CompressionSettings settings, int? videoBitrateKbps,
        ProgressTracker tracker, CancellationToken cancellationToken)
    {
        string? passDirectory = null;
        try
        {
            string? passLogPrefix = null;
            if (FfmpegArgumentsBuilder.NeedsTwoPasses(settings))
            {
                passDirectory = Path.Combine(Path.GetTempPath(), "VideoCompressor", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(passDirectory);
                passLogPrefix = Path.Combine(passDirectory, "pass");
            }

            var passes = FfmpegArgumentsBuilder.BuildPasses(
                inputPath, outputPath, source, settings, passLogPrefix, videoBitrateKbps);
            for (var i = 0; i < passes.Count; i++)
            {
                var start = (double)i / passes.Count;
                var span = 1.0 / passes.Count;
                var result = await ProcessRunner.RunAsync(
                    tools.FfmpegPath, passes[i], line => tracker.OnOutputLine(line, start, span),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.ExitCode != 0)
                {
                    throw CreateError(result.ErrorText);
                }
            }
        }
        finally
        {
            if (passDirectory is not null)
            {
                try
                {
                    Directory.Delete(passDirectory, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best effort: a few KB of statistics in the temp folder are not worth an error.
                }
            }
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

    private static string Mb(long bytes) =>
        string.Format(CultureInfo.GetCultureInfo("ru-RU"), "{0:0.#} МБ", bytes / (1024.0 * 1024));

    /// <summary>Turns ffmpeg's "out_time" into monotonic 0..1 progress with a time estimate.</summary>
    private sealed class ProgressTracker(IProgress<CompressionProgress>? progress, TimeSpan duration)
    {
        // Below this share the time estimate is too noisy to show.
        private const double MinFractionForEstimate = 0.01;

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private double _last;

        public TimeSpan Elapsed => _stopwatch.Elapsed;

        /// <param name="start">Where this pass begins on the overall 0..1 scale.</param>
        /// <param name="span">How much of the scale this pass covers.</param>
        public void OnOutputLine(string line, double start, double span)
        {
            if (progress is null || duration <= TimeSpan.Zero || !FfmpegProgressParser.TryParseOutTime(line, out var outTime))
            {
                return;
            }

            var fraction = start + span * Math.Clamp(outTime / duration, 0, 1);
            _last = Math.Max(_last, fraction);
            var elapsed = _stopwatch.Elapsed;
            TimeSpan? remaining = _last >= MinFractionForEstimate ? elapsed * ((1 - _last) / _last) : null;
            progress.Report(new CompressionProgress(_last, elapsed, remaining));
        }

        /// <summary>The size correction starts the encode over, so progress does too.</summary>
        public void Restart()
        {
            _last = 0;
            _stopwatch.Restart();
        }

        public void Complete() => progress?.Report(new CompressionProgress(1, _stopwatch.Elapsed, TimeSpan.Zero));
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
