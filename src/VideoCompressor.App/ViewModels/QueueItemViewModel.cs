using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoCompressor.App.Services;
using VideoCompressor.Core.Compression;
using VideoCompressor.Core.Media;

namespace VideoCompressor.App.ViewModels;

public enum QueueItemStatus
{
    Analyzing,
    Waiting,

    /// <summary>ffprobe could not read it: not a video or broken. Never compressed.</summary>
    Invalid,
    Compressing,
    Done,
    Failed,
    Cancelled,
    SkippedNotSmaller,

    /// <summary>Not compressed this time by the user's choice (e.g. already below the target size); runs next time.</summary>
    Skipped,
}

/// <summary>One file in the queue (spec 003 US-1.2, US-1.6).</summary>
public sealed partial class QueueItemViewModel(string inputPath, IPlatformShell shell) : ObservableObject
{
    public string InputPath { get; } = inputPath;

    public string FileName => Path.GetFileName(InputPath);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(IsHdr))]
    public partial VideoInfo? VideoInfo { get; private set; }

    public string? Summary => VideoInfo is null ? null : Formatting.VideoSummary(VideoInfo);

    /// <summary>HDR is not handled specially, so colors may shift (spec 001 §9).</summary>
    public bool IsHdr => VideoInfo?.IsHdr == true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompressing), nameof(IsError), nameof(IsFinished), nameof(IsRunnable))]
    public partial QueueItemStatus Status { get; private set; } = QueueItemStatus.Analyzing;

    public bool IsCompressing => Status == QueueItemStatus.Compressing;

    public bool IsError => Status is QueueItemStatus.Invalid or QueueItemStatus.Failed;

    public bool IsFinished => Status is QueueItemStatus.Done or QueueItemStatus.SkippedNotSmaller;

    /// <summary>
    /// What "Сжать" will process: never-run, cancelled and failed files; finished and broken ones are skipped.
    /// </summary>
    public bool IsRunnable =>
        VideoInfo is not null &&
        Status is QueueItemStatus.Waiting or QueueItemStatus.Cancelled or QueueItemStatus.Failed or QueueItemStatus.Skipped;

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "Анализ…";

    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultFile))]
    [NotifyCanExecuteChangedFor(nameof(RevealCommand))]
    public partial CompressionResult? Result { get; private set; }

    /// <summary>A kept result exists on disk ("пропущенный" one was deleted).</summary>
    public bool HasResultFile => Result is not null && Status == QueueItemStatus.Done;

    public string? ErrorDetails { get; private set; }

    /// <summary>Target-size remark for this file, e.g. "Уже меньше 25 МБ" (spec 002 US-2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargetNote))]
    public partial string? TargetNote { get; set; }

    public bool HasTargetNote => TargetNote is not null;

    public void SetAnalyzed(VideoInfo info)
    {
        VideoInfo = info;
        SetStatus(QueueItemStatus.Waiting, "Ожидает");
    }

    public void SetInvalid(string message, string? details)
    {
        ErrorDetails = details;
        SetStatus(QueueItemStatus.Invalid, message);
    }

    public void Apply(BatchItemUpdate update)
    {
        switch (update.State)
        {
            case BatchItemState.Compressing:
                Result = null;
                ProgressPercent = (update.Progress?.Fraction ?? 0) * 100;
                SetStatus(QueueItemStatus.Compressing,
                    update.Progress is { } p ? Formatting.Progress(p) : "Сжатие…");
                break;
            case BatchItemState.Done:
                Result = update.Result;
                SetStatus(QueueItemStatus.Done, update.Result is { } r ? Formatting.ItemResult(r) : "Готово");
                break;
            case BatchItemState.SkippedNotSmaller:
                Result = update.Result;
                SetStatus(QueueItemStatus.SkippedNotSmaller, "Пропущен: сжатый файл не меньше исходного");
                break;
            case BatchItemState.Failed:
                ErrorDetails = update.ErrorDetails;
                SetStatus(QueueItemStatus.Failed, $"Ошибка: {update.Error}");
                break;
            case BatchItemState.Cancelled:
                SetStatus(QueueItemStatus.Cancelled, "Отменено");
                break;
            case BatchItemState.Waiting:
                SetStatus(QueueItemStatus.Waiting, "Ожидает");
                break;
        }
    }

    public void MarkSkipped(string reason) => SetStatus(QueueItemStatus.Skipped, $"Пропущен: {reason}");

    /// <summary>Single-file mode: the user deleted a not-smaller result after the dialog (spec 001 §9).</summary>
    public void MarkResultDeleted() =>
        SetStatus(QueueItemStatus.SkippedNotSmaller, "Удалён: сжатый файл не меньше исходного");

    [RelayCommand(CanExecute = nameof(HasResultFile))]
    private Task RevealAsync() => Result is null ? Task.CompletedTask : shell.RevealFileAsync(Result.OutputPath);

    private void SetStatus(QueueItemStatus status, string text)
    {
        Status = status;
        StatusText = text;
        if (status != QueueItemStatus.Compressing)
        {
            ProgressPercent = 0;
        }

        OnPropertyChanged(nameof(HasResultFile));
        RevealCommand.NotifyCanExecuteChanged();
    }
}
