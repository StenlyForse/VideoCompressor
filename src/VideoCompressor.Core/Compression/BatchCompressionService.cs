using System.Diagnostics;
using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.IO;
using VideoCompressor.Core.Media;

namespace VideoCompressor.Core.Compression;

/// <param name="OutputPath">Set only for a single file the user chose a path for; otherwise the default next to the source.</param>
public sealed record BatchItem(string InputPath, VideoInfo Source, string? OutputPath = null);

public enum BatchItemState
{
    Waiting,
    Compressing,
    Done,
    Failed,
    Cancelled,
    SkippedNotSmaller,
}

public sealed record BatchItemUpdate(
    int Index,
    BatchItemState State,
    CompressionProgress? Progress = null,
    CompressionResult? Result = null,
    string? Error = null,
    string? ErrorDetails = null);

/// <param name="Fraction">Share of the whole queue done, weighted by video duration (plan 003 §2).</param>
public readonly record struct BatchProgress(int CurrentIndex, int Count, double Fraction, TimeSpan Elapsed, TimeSpan? Remaining);

/// <summary>Compresses files one after another with shared settings (spec 003).</summary>
public sealed class BatchCompressionService(FfmpegTools tools)
{
    // Below this share the time estimate is too noisy to show.
    private const double MinFractionForEstimate = 0.01;

    /// <param name="deleteNotSmaller">
    /// Remove results that came out not smaller than the source instead of asking (spec 003 US-4).
    /// </param>
    /// <returns>The final update of every item.</returns>
    /// <exception cref="OperationCanceledException">
    /// After the current file is cleaned up and every unfinished item is reported as cancelled.
    /// </exception>
    public async Task<IReadOnlyList<BatchItemUpdate>> RunAsync(
        IReadOnlyList<BatchItem> items,
        CompressionSettings settings,
        bool deleteNotSmaller,
        IProgress<BatchItemUpdate>? itemProgress = null,
        IProgress<BatchProgress>? batchProgress = null,
        CancellationToken cancellationToken = default)
    {
        var finals = items.Select((_, i) => new BatchItemUpdate(i, BatchItemState.Waiting)).ToArray();
        var weights = items.Select(item => Math.Max(item.Source.Duration.TotalSeconds, 0)).ToArray();
        var totalWeight = weights.Sum();
        var stopwatch = Stopwatch.StartNew();
        var compressor = new VideoCompressionService(tools);
        var doneWeight = 0.0;
        var lastFraction = 0.0;

        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var item = items[i];
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(new BatchItemUpdate(index, BatchItemState.Compressing, new CompressionProgress(0, TimeSpan.Zero, null)));
                ReportBatch(index, 0);

                var output = item.OutputPath ?? OutputPathResolver.GetDefaultOutputPath(item.InputPath);
                var fileProgress = new InlineProgress<CompressionProgress>(p =>
                {
                    Report(new BatchItemUpdate(index, BatchItemState.Compressing, p));
                    ReportBatch(index, p.Fraction);
                });
                var result = await compressor.CompressAsync(
                    item.InputPath, output, item.Source, settings, fileProgress, cancellationToken).ConfigureAwait(false);

                if (deleteNotSmaller && result.IsNotSmaller)
                {
                    await FileCleanup.DeleteWithRetryAsync(result.OutputPath).ConfigureAwait(false);
                    Report(new BatchItemUpdate(index, BatchItemState.SkippedNotSmaller, Result: result));
                }
                else
                {
                    Report(new BatchItemUpdate(index, BatchItemState.Done, Result: result));
                }
            }
            catch (OperationCanceledException)
            {
                for (var rest = index; rest < items.Count; rest++)
                {
                    Report(new BatchItemUpdate(rest, BatchItemState.Cancelled));
                }

                throw;
            }
            catch (Exception ex) when (ex is FfmpegException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                Report(new BatchItemUpdate(index, BatchItemState.Failed,
                    Error: ex.Message, ErrorDetails: (ex as FfmpegException)?.Details));
            }

            doneWeight += weights[index];
        }

        if (items.Count > 0)
        {
            batchProgress?.Report(new BatchProgress(items.Count - 1, items.Count, 1, stopwatch.Elapsed, TimeSpan.Zero));
        }

        return finals;

        void Report(BatchItemUpdate update)
        {
            finals[update.Index] = update;
            itemProgress?.Report(update);
        }

        void ReportBatch(int index, double fileFraction)
        {
            if (batchProgress is null)
            {
                return;
            }

            var fraction = totalWeight > 0
                ? (doneWeight + fileFraction * weights[index]) / totalWeight
                : (index + fileFraction) / items.Count;
            fraction = lastFraction = Math.Max(lastFraction, Math.Clamp(fraction, 0, 1));
            var elapsed = stopwatch.Elapsed;
            TimeSpan? remaining = fraction >= MinFractionForEstimate ? elapsed * ((1 - fraction) / fraction) : null;
            batchProgress.Report(new BatchProgress(index, items.Count, fraction, elapsed, remaining));
        }
    }

    /// <summary>Forwards immediately on the reporting thread; the caller's own IProgress decides where it lands.</summary>
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
