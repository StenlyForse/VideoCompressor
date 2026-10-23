using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoCompressor.App.Services;
using VideoCompressor.Core.Compression;
using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.IO;
using VideoCompressor.Core.Media;

namespace VideoCompressor.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IPlatformShell _shell;
    private readonly FfmpegTools? _tools;

    // Guards the level <-> percent synchronization against feedback loops (US-2.4).
    private bool _syncingLevel;

    public MainViewModel(IDialogService dialogs, IPlatformShell shell, FfmpegTools? tools)
    {
        _dialogs = dialogs;
        _shell = shell;
        _tools = tools;

        SelectedLevel = Levels.First(l => l.Level == CompressionLevel.Balanced);
        StatusText = "";
    }

    public IReadOnlyList<LevelOption> Levels => LevelOption.All;

    public bool IsFfmpegMissing => _tools is null;

    public string FfmpegHintMessage => FfmpegInstallHint.Message;

    public string FfmpegHintCommand => FfmpegInstallHint.Command;

    // ---- Source video (US-1.1) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    public partial string? InputPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVideo), nameof(VideoSummary))]
    [NotifyCanExecuteChangedFor(nameof(CompressCommand))]
    public partial VideoInfo? VideoInfo { get; private set; }

    public bool HasVideo => VideoInfo is not null;

    public string? FileName => InputPath is null ? null : Path.GetFileName(InputPath);

    public string? VideoSummary => VideoInfo is null ? null : Formatting.VideoSummary(VideoInfo);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompressCommand))]
    public partial string? OutputPath { get; set; }

    // ---- Compression level (US-2) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LevelDescription))]
    public partial LevelOption? SelectedLevel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LevelDescription))]
    public partial int CompressionPercent { get; set; }

    public string LevelDescription => SelectedLevel?.Description ?? CustomLevelDescription();

    private string CustomLevelDescription()
    {
        var below = Levels.LastOrDefault(l => l.Percent < CompressionPercent);
        var above = Levels.FirstOrDefault(l => l.Percent > CompressionPercent);
        var position = (below, above) switch
        {
            (null, { } first) => $"качество выше, чем у уровня «{first.Name}»",
            ({ } b, { } a) => $"между уровнями «{b.Name}» и «{a.Name}»",
            _ => "",
        };
        return $"Своя настройка: {CompressionPercent}%, {position}.";
    }

    partial void OnSelectedLevelChanged(LevelOption? value)
    {
        // null means "custom" and is only ever set from the percent side.
        if (_syncingLevel || value is null)
        {
            return;
        }

        _syncingLevel = true;
        CompressionPercent = value.Percent;
        _syncingLevel = false;
    }

    partial void OnCompressionPercentChanged(int value)
    {
        if (_syncingLevel)
        {
            return;
        }

        _syncingLevel = true;
        SelectedLevel = Levels.FirstOrDefault(l => l.Percent == value);
        _syncingLevel = false;
    }

    // ---- Progress and result (US-1.2, US-1.3) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CompressCommand), nameof(BrowseInputCommand))]
    public partial bool IsBusy { get; private set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; }

    [ObservableProperty]
    public partial bool HasError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyCanExecuteChangedFor(nameof(RevealOutputCommand))]
    public partial CompressionResult? LastResult { get; private set; }

    public bool HasResult => LastResult is not null;

    // ---- Commands ----

    [RelayCommand(CanExecute = nameof(CanBrowseInput))]
    private async Task BrowseInputAsync()
    {
        var path = await _dialogs.PickVideoFileAsync();
        if (path is not null)
        {
            await LoadFileAsync(path);
        }
    }

    private bool CanBrowseInput() => !IsBusy && !IsFfmpegMissing;

    /// <summary>Entry point for both the file dialog and drag &amp; drop.</summary>
    public async Task LoadFileAsync(string path)
    {
        if (_tools is null || IsBusy)
        {
            return;
        }

        SetStatus("Анализ файла…");
        try
        {
            var info = await new MediaProbe(_tools).ProbeAsync(path);
            InputPath = Path.GetFullPath(path);
            VideoInfo = info;
            OutputPath = OutputPathResolver.GetDefaultOutputPath(path);
            LastResult = null;
            ProgressPercent = 0;
            SetStatus("");
        }
        catch (Exception ex) when (ex is FfmpegException or IOException or UnauthorizedAccessException)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>Drag &amp; drop of something without a local path, e.g. from a browser.</summary>
    public void ReportNonLocalDrop() => SetStatus("Перетащите файл с диска.", isError: true);

    [RelayCommand(CanExecute = nameof(CanCompress), IncludeCancelCommand = true)]
    private async Task CompressAsync(CancellationToken cancellationToken)
    {
        if (_tools is null || InputPath is null || OutputPath is null || VideoInfo is null)
        {
            return;
        }

        IsBusy = true;
        LastResult = null;
        ProgressPercent = 0;
        SetStatus("Сжатие…");
        try
        {
            var progress = new Progress<CompressionProgress>(p =>
            {
                if (!IsBusy)
                {
                    return;
                }

                ProgressPercent = p.Fraction * 100;
                StatusText = Formatting.Progress(p);
            });

            var result = await new VideoCompressionService(_tools).CompressAsync(
                InputPath, OutputPath, VideoInfo, BuildSettings(), progress, cancellationToken);

            LastResult = result;
            SetStatus(Formatting.Result(result));
        }
        catch (OperationCanceledException)
        {
            ProgressPercent = 0;
            SetStatus("Сжатие отменено.");
        }
        catch (Exception ex) when (ex is FfmpegException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ProgressPercent = 0;
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCompress() => !IsBusy && _tools is not null && VideoInfo is not null && OutputPath is not null;

    [RelayCommand(CanExecute = nameof(HasResult))]
    private Task RevealOutputAsync() =>
        LastResult is null ? Task.CompletedTask : _shell.RevealFileAsync(LastResult.OutputPath);

    private CompressionSettings BuildSettings() =>
        new(CompressionPercent, OutputFormat.Efficient, EncoderKind.Cpu);

    private void SetStatus(string text, bool isError = false)
    {
        StatusText = text;
        HasError = isError;
    }
}
