using System.Collections.ObjectModel;
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
    private readonly SettingsStore _settingsStore;

    // The encoder from saved settings; GPU is applied only once the check confirms it works.
    private readonly EncoderKind _preferredEncoder;

    // Guards the level <-> percent synchronization against feedback loops (US-2.4).
    private bool _syncingLevel;

    public MainViewModel(IDialogService dialogs, IPlatformShell shell, FfmpegTools? tools, SettingsStore settingsStore)
    {
        _dialogs = dialogs;
        _shell = shell;
        _tools = tools;
        _settingsStore = settingsStore;

        // CPU until the GPU check finishes (plan §4.5).
        Encoders = [CpuEncoder];
        SelectedEncoder = CpuEncoder;
        StatusText = "";

        var settings = settingsStore.Load();
        _preferredEncoder = settings.Encoder;
        CompressionPercent = Math.Clamp(settings.CompressionPercent, 0, 100);
        SelectedLevel = Levels.FirstOrDefault(l => l.Percent == CompressionPercent);
        SelectedFormat = Formats.FirstOrDefault(f => f.Value == settings.Format) ?? Formats[0];
        // Unknown values (e.g. a hand-edited file) fall back to "original".
        SelectedResolution = Resolutions.FirstOrDefault(r => r.Value == settings.MaxHeight) ?? Resolutions[0];
        SelectedFrameRate = FrameRates.FirstOrDefault(r => r.Value == settings.MaxFps) ?? FrameRates[0];
    }

    /// <summary>Background checks that must not delay the window from appearing.</summary>
    public async Task InitializeAsync()
    {
        if (_tools is null)
        {
            return;
        }

        if (await new EncoderDetector(_tools).IsGpuAvailableAsync())
        {
            Encoders.Insert(0, GpuEncoder);
            if (!IsBusy && _preferredEncoder == EncoderKind.Gpu)
            {
                SelectedEncoder = GpuEncoder;
            }
        }
    }

    /// <summary>Remembers the current choices for the next run (FR-008).</summary>
    public void SaveSettings()
    {
        // If the GPU is missing this time (or not checked yet), keep the saved preference
        // instead of silently switching it to CPU for good.
        var encoder = Encoders.Contains(GpuEncoder) ? SelectedEncoder.Value : _preferredEncoder;
        _settingsStore.Save(new AppSettings(
            CompressionPercent, SelectedFormat.Value, encoder, SelectedResolution.Value, SelectedFrameRate.Value));
    }

    public IReadOnlyList<LevelOption> Levels => LevelOption.All;

    public bool IsFfmpegMissing => _tools is null;

    public string FfmpegHintMessage => FfmpegInstallHint.Message;

    public string FfmpegHintCommand => FfmpegInstallHint.Command;

    // ---- Source video (US-1.1) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(OutputError), nameof(HasOutputError))]
    public partial string? InputPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVideo), nameof(VideoSummary), nameof(IsHdr))]
    [NotifyCanExecuteChangedFor(nameof(CompressCommand), nameof(BrowseOutputCommand))]
    public partial VideoInfo? VideoInfo { get; private set; }

    public bool HasVideo => VideoInfo is not null;

    /// <summary>HDR is not handled specially in v1, so colors may shift (spec §9).</summary>
    public bool IsHdr => VideoInfo?.IsHdr == true;

    public string? FileName => InputPath is null ? null : Path.GetFileName(InputPath);

    public string? VideoSummary => VideoInfo is null ? null : Formatting.VideoSummary(VideoInfo);

    // ---- Output path (US-5) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputError), nameof(HasOutputError))]
    [NotifyCanExecuteChangedFor(nameof(CompressCommand))]
    public partial string? OutputPath { get; set; }

    /// <summary>Why the chosen output path cannot be used, or <c>null</c> if it is fine.</summary>
    public string? OutputError
    {
        get
        {
            if (OutputPath is null || InputPath is null)
            {
                return null;
            }

            if (OutputPathResolver.IsSamePath(OutputPath, InputPath))
            {
                return "Нельзя сохранить результат поверх исходного файла — выберите другое имя.";
            }

            return Directory.Exists(Path.GetDirectoryName(OutputPath)) ? null : "Папка для сохранения не найдена.";
        }
    }

    public bool HasOutputError => OutputError is not null;

    [RelayCommand(CanExecute = nameof(CanBrowseOutput))]
    private async Task BrowseOutputAsync()
    {
        if (OutputPath is null)
        {
            return;
        }

        var path = await _dialogs.PickOutputFileAsync(OutputPath);
        if (path is not null)
        {
            OutputPath = path;
        }
    }

    private bool CanBrowseOutput() => !IsBusy && HasVideo;

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

    // ---- Format and encoder (US-4) ----

    private static readonly Option<EncoderKind> GpuEncoder =
        new("Видеокарта (NVIDIA NVENC)", EncoderKind.Gpu, "Быстро: обычно быстрее, чем длится само видео.");

    private static readonly Option<EncoderKind> CpuEncoder =
        new("Процессор", EncoderKind.Cpu, "Файл на 10–20% меньше при том же качестве, но сжатие примерно в 2 раза дольше.");

    public IReadOnlyList<Option<OutputFormat>> Formats { get; } =
    [
        new("Максимальное сжатие (H.265)", OutputFormat.Efficient,
            "Самый маленький файл. Воспроизводится в современных плеерах, браузерах и Telegram."),
        new("Максимальная совместимость (H.264)", OutputFormat.Compatible,
            "Открывается где угодно, включая старые устройства, но файл примерно в 1,5–2 раза больше."),
    ];

    public ObservableCollection<Option<EncoderKind>> Encoders { get; }

    [ObservableProperty]
    public partial Option<OutputFormat> SelectedFormat { get; set; }

    [ObservableProperty]
    public partial Option<EncoderKind> SelectedEncoder { get; set; }

    // ---- Optional resolution and frame-rate limits (US-3) ----

    public IReadOnlyList<Option<int?>> Resolutions { get; } =
    [
        new("Исходное", null),
        new("1440p", 1440),
        new("1080p", 1080),
        new("720p", 720),
    ];

    public IReadOnlyList<Option<int?>> FrameRates { get; } =
    [
        new("Исходная", null),
        new("60 fps", 60),
        new("30 fps", 30),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdvancedHeader))]
    public partial Option<int?> SelectedResolution { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdvancedHeader))]
    public partial Option<int?> SelectedFrameRate { get; set; }

    /// <summary>The block is collapsed by default, so the header tells when something is changed in it.</summary>
    public string AdvancedHeader
    {
        get
        {
            var changes = new[] { SelectedResolution, SelectedFrameRate }
                .Where(o => o?.Value is not null)
                .Select(o => o!.Name)
                .ToList();
            return changes.Count == 0
                ? "Дополнительно: разрешение и частота кадров"
                : $"Дополнительно: {string.Join(", ", changes)}";
        }
    }

    // ---- Progress and result (US-1.2, US-1.3) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CompressCommand), nameof(BrowseInputCommand), nameof(BrowseOutputCommand))]
    public partial bool IsBusy { get; private set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    public partial double ProgressPercent { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; }

    [ObservableProperty]
    public partial bool HasError { get; private set; }

    /// <summary>Technical tail of the ffmpeg output for errors without a known cause (plan §4.6).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorDetails))]
    public partial string? ErrorDetails { get; private set; }

    public bool HasErrorDetails => ErrorDetails is not null;

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
            SetStatus(ex.Message, isError: true, details: (ex as FfmpegException)?.Details);
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

            SaveSettings();
            // The next run must not silently overwrite this result (US-5.2).
            OutputPath = OutputPathResolver.GetDefaultOutputPath(InputPath);

            if (result.IsNotSmaller && await ConfirmDeleteNotSmallerAsync(result))
            {
                File.Delete(result.OutputPath);
                OutputPath = OutputPathResolver.GetDefaultOutputPath(InputPath);
                SetStatus("Сжатый файл удалён: он получился не меньше исходного. " +
                          "Попробуйте более сильную степень сжатия или уменьшите разрешение.");
                return;
            }

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
            SetStatus(ex.Message, isError: true, details: (ex as FfmpegException)?.Details);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Already well-compressed sources can come out larger (spec §9).</summary>
    private Task<bool> ConfirmDeleteNotSmallerAsync(CompressionResult result) =>
        _dialogs.ConfirmAsync(
            "Файл не стал меньше",
            $"Сжатый файл ({Formatting.Size(result.CompressedSize)}) не меньше исходного " +
            $"({Formatting.Size(result.OriginalSize)}): видео, вероятно, уже хорошо сжато. Удалить сжатый файл?",
            "Удалить",
            "Оставить");

    private bool CanCompress() =>
        !IsBusy && _tools is not null && VideoInfo is not null && OutputPath is not null && OutputError is null;

    /// <summary>
    /// Called when the window is closing. While compressing, asks the user and, if confirmed,
    /// cancels and waits until the partial output is cleaned up (spec §9, plan §5.4).
    /// </summary>
    /// <returns><c>true</c> if the window may close.</returns>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!IsBusy)
        {
            return true;
        }

        var stop = await _dialogs.ConfirmAsync(
            "Сжатие не завершено",
            "Остановить сжатие и закрыть приложение? Недописанный файл будет удалён.",
            "Остановить и закрыть",
            "Продолжить сжатие");
        if (!stop)
        {
            return false;
        }

        CompressCancelCommand.Execute(null);
        if (CompressCommand.ExecutionTask is { } running)
        {
            await running;
        }

        return true;
    }

    [RelayCommand(CanExecute = nameof(HasResult))]
    private Task RevealOutputAsync() =>
        LastResult is null ? Task.CompletedTask : _shell.RevealFileAsync(LastResult.OutputPath);

    private CompressionSettings BuildSettings() =>
        new(CompressionPercent, SelectedFormat.Value, SelectedEncoder.Value,
            SelectedResolution.Value, SelectedFrameRate.Value);

    private void SetStatus(string text, bool isError = false, string? details = null)
    {
        StatusText = text;
        HasError = isError;
        ErrorDetails = string.IsNullOrWhiteSpace(details) ? null : details;
    }
}
