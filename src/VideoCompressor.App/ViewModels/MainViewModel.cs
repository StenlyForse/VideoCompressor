using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
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

    // Whether the OS's built-in player can show H.265 (plan §4.8).
    private readonly bool _hevcPlayable;

    // Guards the level <-> percent synchronization against feedback loops (US-2.4).
    private bool _syncingLevel;

    // Only a format picked in the UI is remembered; otherwise it is auto-selected on every start (US-4.6).
    private bool _formatChosenByUser;
    private bool _settingFormatInCode;

    public MainViewModel(
        IDialogService dialogs, IPlatformShell shell, FfmpegTools? tools, SettingsStore settingsStore, bool hevcPlayable)
    {
        _dialogs = dialogs;
        _shell = shell;
        _tools = tools;
        _settingsStore = settingsStore;
        _hevcPlayable = hevcPlayable;

        // CPU until the GPU check finishes (plan §4.5).
        Encoders = [CpuEncoder];
        SelectedEncoder = CpuEncoder;
        StatusText = "";

        var settings = settingsStore.Load();
        _preferredEncoder = settings.Encoder;
        CompressionPercent = Math.Clamp(settings.CompressionPercent, 0, 100);
        SelectedLevel = Levels.FirstOrDefault(l => l.Percent == CompressionPercent);
        // US-4.4: H.265 only if the built-in player can show it, so the default result never plays without picture.
        var savedFormat = Formats.FirstOrDefault(f => f.Value == settings.Format);
        _formatChosenByUser = savedFormat is not null;
        SetFormatInCode(savedFormat ?? Formats.First(f =>
            f.Value == (hevcPlayable ? OutputFormat.Efficient : OutputFormat.Compatible)));
        // Unknown values (e.g. a hand-edited file) fall back to "original".
        SelectedResolution = Resolutions.FirstOrDefault(r => r.Value == settings.MaxHeight) ?? Resolutions[0];
        SelectedFrameRate = FrameRates.FirstOrDefault(r => r.Value == settings.MaxFps) ?? FrameRates[0];

        Items.CollectionChanged += OnItemsChanged;
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
        var format = _formatChosenByUser ? SelectedFormat.Value : (OutputFormat?)null;
        _settingsStore.Save(new AppSettings(
            CompressionPercent, format, encoder, SelectedResolution.Value, SelectedFrameRate.Value));
    }

    public IReadOnlyList<LevelOption> Levels => LevelOption.All;

    public bool IsFfmpegMissing => _tools is null;

    public string FfmpegHintMessage => FfmpegInstallHint.Message;

    public string FfmpegHintCommand => FfmpegInstallHint.Command;

    // ---- Files (spec 003 US-1) ----

    public ObservableCollection<QueueItemViewModel> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    /// <summary>One file behaves as in v1: own output path and a question about a not-smaller result.</summary>
    public bool IsSingleFile => Items.Count == 1;

    public bool HasFinishedItems => Items.Any(i => i.IsFinished);

    public string FilesHeader => Items.Count switch
    {
        0 => "Перетащите видео сюда",
        1 => "1 файл",
        var n => $"Файлов: {n}",
    };

    private QueueItemViewModel? SingleItem => IsSingleFile ? Items[0] : null;

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsSingleFile));
        OnPropertyChanged(nameof(FilesHeader));
        OnPropertyChanged(nameof(HasFinishedItems));
        // "Сохранить в" exists only for a single file; with more files results go next to their sources.
        OutputPath = SingleItem is { } single ? OutputPathResolver.GetDefaultOutputPath(single.InputPath) : null;
        RefreshCommands();
    }

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task BrowseFilesAsync() => await AddFilesAsync(await _dialogs.PickVideoFilesAsync());

    /// <summary>Entry point for both the file dialog and drag &amp; drop.</summary>
    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (_tools is null || IsBusy)
        {
            return;
        }

        var added = new List<QueueItemViewModel>();
        var duplicates = 0;
        foreach (var path in paths.Select(Path.GetFullPath))
        {
            // FR-002: the same file is never queued twice.
            if (Items.Concat(added).Any(i => OutputPathResolver.IsSamePath(i.InputPath, path)))
            {
                duplicates++;
                continue;
            }

            var item = new QueueItemViewModel(path, _shell);
            added.Add(item);
            Items.Add(item);
        }

        SetStatus(duplicates > 0 ? $"Уже в списке: {duplicates}" : "");

        foreach (var item in added)
        {
            try
            {
                item.SetAnalyzed(await new MediaProbe(_tools).ProbeAsync(item.InputPath));
            }
            catch (Exception ex) when (ex is FfmpegException or IOException or UnauthorizedAccessException)
            {
                item.SetInvalid(ex.Message, (ex as FfmpegException)?.Details);
            }
        }

        RefreshCommands();
    }

    /// <summary>Drag &amp; drop of something without a local path, e.g. from a browser.</summary>
    public void ReportNonLocalDrop() => SetStatus("Перетащите файл с диска.", isError: true);

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private void RemoveItem(QueueItemViewModel item) => Items.Remove(item);

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private void Clear()
    {
        Items.Clear();
        SetStatus("");
        ProgressPercent = 0;
    }

    [RelayCommand(CanExecute = nameof(CanClearFinished))]
    private void ClearFinished()
    {
        foreach (var item in Items.Where(i => i.IsFinished).ToList())
        {
            Items.Remove(item);
        }
    }

    private bool CanEditList() => !IsBusy && !IsFfmpegMissing;

    private bool CanClearFinished() => CanEditList() && HasFinishedItems;

    // ---- Output path for a single file (spec 001 US-5) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputError), nameof(HasOutputError))]
    [NotifyCanExecuteChangedFor(nameof(CompressCommand), nameof(BrowseOutputCommand))]
    public partial string? OutputPath { get; set; }

    /// <summary>Why the chosen output path cannot be used, or <c>null</c> if it is fine.</summary>
    public string? OutputError
    {
        get
        {
            if (OutputPath is null || SingleItem is not { } single)
            {
                return null;
            }

            if (OutputPathResolver.IsSamePath(OutputPath, single.InputPath))
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

    private bool CanBrowseOutput() => !IsBusy && OutputPath is not null;

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
            "Самый маленький файл. VLC, браузеры, Telegram и macOS показывают его сразу; " +
            "встроенному плееру Windows нужен кодек HEVC."),
        new("Максимальная совместимость (H.264)", OutputFormat.Compatible,
            "Открывается где угодно, включая старые устройства, но файл примерно в 1,5–2 раза больше."),
    ];

    public ObservableCollection<Option<EncoderKind>> Encoders { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatNote), nameof(HasFormatNote), nameof(IsFormatNoteWarning), nameof(CanInstallHevcCodec))]
    public partial Option<OutputFormat> SelectedFormat { get; set; }

    partial void OnSelectedFormatChanged(Option<OutputFormat> value)
    {
        if (!_settingFormatInCode)
        {
            _formatChosenByUser = true;
        }
    }

    private void SetFormatInCode(Option<OutputFormat> format)
    {
        _settingFormatInCode = true;
        SelectedFormat = format;
        _settingFormatInCode = false;
    }

    /// <summary>Why the format is what it is, when the OS cannot show H.265 (US-4.4, US-4.5).</summary>
    public string? FormatNote => (_hevcPlayable, SelectedFormat.Value, _formatChosenByUser) switch
    {
        (true, _, _) => null,
        (false, OutputFormat.Efficient, _) =>
            "На этом компьютере встроенный плеер покажет такой файл без картинки, только со звуком: " +
            "нет кодека HEVC. В VLC, браузере и Telegram видео будет. " +
            "Установите кодек или выберите «Максимальная совместимость».",
        (false, OutputFormat.Compatible, false) =>
            "Выбран автоматически: на этом компьютере нет кодека для H.265, поэтому используется формат, " +
            "который откроется везде. После установки кодека HEVC приложение само будет выбирать H.265 — файлы станут меньше.",
        _ => null,
    };

    public bool HasFormatNote => FormatNote is not null;

    public bool IsFormatNoteWarning => !_hevcPlayable && SelectedFormat.Value == OutputFormat.Efficient;

    /// <summary>The codec can only be installed this way on Windows (Microsoft Store).</summary>
    public bool CanInstallHevcCodec => OperatingSystem.IsWindows() && HasFormatNote;

    [RelayCommand]
    private Task InstallHevcCodecAsync() =>
        _shell.OpenUriAsync(new Uri("ms-windows-store://pdp/?ProductId=9NMZLZ57R3T7"));

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

    // ---- Progress and result ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; private set; }

    partial void OnIsBusyChanged(bool value) => RefreshCommands();

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

    // ---- Compression ----

    [RelayCommand(CanExecute = nameof(CanCompress), IncludeCancelCommand = true)]
    private async Task CompressAsync(CancellationToken cancellationToken)
    {
        var runnable = Items.Where(i => i.IsRunnable).ToList();
        if (_tools is null || runnable.Count == 0)
        {
            return;
        }

        var single = IsSingleFile;
        var batch = runnable
            .Select(i => new BatchItem(i.InputPath, i.VideoInfo!, single ? OutputPath : null))
            .ToList();

        IsBusy = true;
        ProgressPercent = 0;
        SetStatus("Сжатие…");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var itemProgress = new Progress<BatchItemUpdate>(u => runnable[u.Index].Apply(u));
            var batchProgress = new Progress<BatchProgress>(p =>
            {
                if (!IsBusy)
                {
                    return;
                }

                ProgressPercent = p.Fraction * 100;
                StatusText = single
                    ? Formatting.Progress(new CompressionProgress(p.Fraction, p.Elapsed, p.Remaining))
                    : Formatting.BatchProgress(p);
            });

            // US-4: a queue never stops to ask; a single file asks as in v1.
            var results = await new BatchCompressionService(_tools).RunAsync(
                batch, BuildSettings(), deleteNotSmaller: !single, itemProgress, batchProgress, cancellationToken);
            for (var i = 0; i < results.Count; i++)
            {
                runnable[i].Apply(results[i]);
            }

            SaveSettings();
            if (single)
            {
                await FinishSingleAsync(runnable[0], results[0]);
            }
            else
            {
                var notVideo = Items.Count(i => i.Status == QueueItemStatus.Invalid);
                SetStatus(Formatting.BatchSummary(results, notVideo, stopwatch.Elapsed),
                    isError: results.All(r => r.State == BatchItemState.Failed));
            }
        }
        catch (OperationCanceledException)
        {
            ProgressPercent = 0;
            SetStatus("Сжатие отменено.");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasFinishedItems));
        }
    }

    private async Task FinishSingleAsync(QueueItemViewModel item, BatchItemUpdate final)
    {
        // The next run must not silently overwrite this result (spec 001 US-5.2).
        OutputPath = OutputPathResolver.GetDefaultOutputPath(item.InputPath);

        if (final.State == BatchItemState.Failed)
        {
            ProgressPercent = 0;
            SetStatus(final.Error ?? "Не удалось сжать видео.", isError: true, details: final.ErrorDetails);
            return;
        }

        if (final is not { State: BatchItemState.Done, Result: { } result })
        {
            return;
        }

        if (result.IsNotSmaller && await ConfirmDeleteNotSmallerAsync(result))
        {
            await FileCleanup.DeleteWithRetryAsync(result.OutputPath);
            item.MarkResultDeleted();
            OutputPath = OutputPathResolver.GetDefaultOutputPath(item.InputPath);
            SetStatus("Сжатый файл удалён: он получился не меньше исходного. " +
                      "Попробуйте более сильную степень сжатия или уменьшите разрешение.");
            return;
        }

        SetStatus(Formatting.Result(result));
    }

    /// <summary>Already well-compressed sources can come out larger (spec 001 §9).</summary>
    private Task<bool> ConfirmDeleteNotSmallerAsync(CompressionResult result) =>
        _dialogs.ConfirmAsync(
            "Файл не стал меньше",
            $"Сжатый файл ({Formatting.Size(result.CompressedSize)}) не меньше исходного " +
            $"({Formatting.Size(result.OriginalSize)}): видео, вероятно, уже хорошо сжато. Удалить сжатый файл?",
            "Удалить",
            "Оставить");

    private bool CanCompress() =>
        !IsBusy && _tools is not null && Items.Any(i => i.IsRunnable) && OutputError is null;

    /// <summary>
    /// Called when the window is closing. While compressing, asks the user and, if confirmed,
    /// cancels and waits until the partial output is cleaned up (spec 001 §9, spec 003 US-3.2).
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

    private CompressionSettings BuildSettings() =>
        new(CompressionPercent, SelectedFormat.Value, SelectedEncoder.Value,
            SelectedResolution.Value, SelectedFrameRate.Value);

    private void RefreshCommands()
    {
        CompressCommand.NotifyCanExecuteChanged();
        BrowseFilesCommand.NotifyCanExecuteChanged();
        RemoveItemCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        ClearFinishedCommand.NotifyCanExecuteChanged();
        BrowseOutputCommand.NotifyCanExecuteChanged();
    }

    private void SetStatus(string text, bool isError = false, string? details = null)
    {
        StatusText = text;
        HasError = isError;
        ErrorDetails = string.IsNullOrWhiteSpace(details) ? null : details;
    }
}
