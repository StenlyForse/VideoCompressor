# План реализации: Video Compressor

| Поле     | Значение                                           |
|----------|----------------------------------------------------|
| Фича     | 001-video-compressor                               |
| Статус   | Утверждён (2026-10-23)                             |
| Создано  | 2026-10-23                                         |
| Обновлено| 2026-10-23 — WPF заменён на Avalonia (кросс-платформенность, spec §10 п. 10); автовыбор формата (§4.8, spec §10 п. 11) |
| Основа   | [spec.md](spec.md) (утверждена), [research.md](research.md) |

## 1. Технический контекст

| Аспект           | Решение | Обоснование |
|------------------|---------|-------------|
| Язык / платформа | C# 14, .NET 10, `net10.0` во всех проектах | Кросс-платформенно (NFR-005); SDK уже установлен; LTS-версия |
| UI               | **Avalonia 12.1.4**: `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` | Кросс-платформенный десктопный UI (Windows, macOS, Linux); XAML и привязки как у WPF; встроенный шрифт Inter с кириллицей даёт одинаковый вид на всех ОС |
| MVVM             | CommunityToolkit.Mvvm 8.4.2 | Генерация `INotifyPropertyChanged` и команд; встроенная отмена асинхронных команд (US-6) |
| Обработка видео  | FFmpeg 8 + ffprobe как **внешние процессы** | Индустриальный стандарт на всех ОС; NVENC и x265 «из коробки»; не входит в репозиторий (FR-006) |
| Тесты            | xUnit, только для ядра | Маппинг, аргументы и пути тестируются без UI и без FFmpeg; интеграционные тесты пропускаются, если FFmpeg не найден |
| Хранение настроек| JSON в `<ApplicationData>/VideoCompressor/settings.json` (System.Text.Json) | FR-008. `Environment.SpecialFolder.ApplicationData`: `%AppData%` на Windows, `~/.config` на Linux и macOS |
| Сеть             | Не используется | NFR-001 |

Почему Avalonia, а не альтернативы:
- **WPF** работает только на Windows.
- **.NET MAUI** не поддерживает Linux, а его десктопная часть уступает мобильной.
- **Uno Platform** избыточен для одного окна.

Почему FFmpeg запускается как процесс, а не через нативные обёртки:
- аргументы можно один в один воспроизвести вручную при отладке (research.md);
- не нужно поставлять нативные библиотеки под каждую ОС;
- падение FFmpeg не роняет приложение.

## 2. Принципы

1. **Ядро не знает про UI.** `VideoCompressor.Core` не ссылается на Avalonia, поэтому может стать основой для CLI или интеграции с файловым менеджером (v2).
2. **Аргументы FFmpeg — чистая функция.** Настройки и сведения о видео детерминированно превращаются в список аргументов, и это покрыто тестами.
3. **Параметры кодирования — из research.md, а не «на глаз».** Любое изменение калибровки сопровождается замером на эталоне.
4. **Минимум зависимостей.** Новая библиотека добавляется, только если без неё код становится заметно сложнее.
5. **Платформенные различия — в одном месте.** Всё, что зависит от ОС, лежит в `Platform/` (ядро) и `Services/PlatformShell` (UI). Остальной код от ОС не зависит (NFR-005).

## 3. Структура решения

```
VideoCompressor/
├── VideoCompressor.slnx
├── README.md                          # что это, установка FFmpeg (Win/macOS/Linux), сборка, запуск
├── specs/001-video-compressor/        # spec.md, research.md, plan.md, tasks.md
├── src/
│   ├── VideoCompressor.Core/          # net10.0, без зависимостей
│   │   ├── Ffmpeg/
│   │   │   ├── FfmpegTools.cs             # пути к ffmpeg/ffprobe + поиск
│   │   │   ├── FfmpegArgumentsBuilder.cs  # (настройки, VideoInfo) → аргументы
│   │   │   ├── FfmpegProgressParser.cs    # разбор вывода -progress
│   │   │   └── FfmpegException.cs
│   │   ├── Media/
│   │   │   ├── VideoInfo.cs
│   │   │   └── MediaProbe.cs              # ffprobe -of json → VideoInfo
│   │   ├── Compression/
│   │   │   ├── CompressionSettings.cs     # %, формат, кодировщик, разрешение, FPS
│   │   │   ├── CompressionLevel.cs        # 4 уровня ↔ проценты
│   │   │   ├── QualityMapping.cs          # % → индекс → CQ/CRF (research R-1)
│   │   │   ├── EncoderDetector.cs         # доступен ли NVENC
│   │   │   ├── VideoCompressionService.cs # запуск, прогресс, отмена, очистка
│   │   │   └── CompressionResult.cs
│   │   ├── IO/
│   │   │   └── OutputPathResolver.cs      # <имя>_compressed.mp4, (1), (2)…
│   │   └── Platform/
│   │       └── PlatformInfo.cs            # имена исполняемых файлов, доп. каталоги поиска, регистр путей
│   └── VideoCompressor.App/           # net10.0, Avalonia
│       ├── Program.cs                 # точка входа Avalonia
│       ├── App.axaml(.cs)             # тема Fluent, шрифт Inter
│       ├── Views/
│       │   ├── MainWindow.axaml(.cs)  # разметка, drag & drop, закрытие окна во время работы
│       │   └── ConfirmDialog.axaml(.cs) # простой диалог «Да / Нет» (в Avalonia нет MessageBox)
│       ├── ViewModels/MainViewModel.cs
│       ├── Services/
│       │   ├── IDialogService.cs      # выбор файлов и подтверждения — для ViewModel
│       │   ├── DialogService.cs       # реализация через StorageProvider и ConfirmDialog
│       │   ├── PlatformShell.cs       # «показать файл в папке» для каждой ОС
│       │   └── SettingsStore.cs
│       └── Formatting.cs              # МБ, длительность, ETA
└── tests/
    └── VideoCompressor.Core.Tests/    # xUnit
```

Текущий каркас `src/VideoCompressor.App` был создан из шаблона WPF. Он заменяется проектом Avalonia (`net10.0`).

## 4. Компоненты ядра

### 4.1. `FfmpegTools` + `PlatformInfo` (FR-006, NFR-005)

`static FfmpegTools? TryLocate()` ищет `ffmpeg` и `ffprobe` (на Windows — с суффиксом `.exe`) в таком порядке:
1. рядом с исполняемым файлом приложения;
2. в подпапке `ffmpeg/` рядом с приложением;
3. в каталогах из `PATH`;
4. в известных каталогах ОС, которых может не быть в `PATH`:

| ОС | Доп. каталоги | Почему |
|----|---------------|--------|
| Windows | `%LOCALAPPDATA%\Microsoft\WinGet\Links` | Сюда `winget install Gyan.FFmpeg` кладёт ссылки; `PATH` обновляется только после перезапуска сессии |
| macOS | `/opt/homebrew/bin`, `/usr/local/bin` | Приложения, запущенные из Finder, не наследуют `PATH` из shell, а Homebrew ставит именно сюда (Apple Silicon / Intel) |
| Linux | `/usr/bin`, `/usr/local/bin`, `/snap/bin` | Подстраховка на случай урезанного `PATH` при запуске из меню |

Если ничего не найдено, UI показывает инструкцию для текущей ОС и блокирует кнопку «Сжать»:
- Windows: `winget install Gyan.FFmpeg`;
- macOS: `brew install ffmpeg`;
- Linux: `sudo apt install ffmpeg` или аналог для вашего дистрибутива.

### 4.2. `MediaProbe` → `VideoInfo` (US-1.1, FR-005)

Запускает `ffprobe -v error -print_format json -show_format -show_streams <файл>` и собирает:

```csharp
record VideoInfo(
    TimeSpan Duration, long SizeBytes,
    int Width, int Height, double Fps,       // Fps из avg_frame_rate, фолбэк r_frame_rate
    string VideoCodec, int AudioTrackCount,
    bool IsHdr);                             // color_transfer ∈ {smpte2084, arib-std-b67}
```

Если видеопотока нет или ffprobe вернул ошибку, выбрасывается `FfmpegException` с понятным текстом: «Файл повреждён или не является видео».

### 4.3. `CompressionSettings`, `CompressionLevel`, `QualityMapping` (US-2, US-3, US-4)

```csharp
enum OutputFormat { Efficient /* HEVC */, Compatible /* H.264 */ }
enum EncoderKind  { Gpu, Cpu }

record CompressionSettings(
    int CompressionPercent,          // 0..100
    OutputFormat Format, EncoderKind Encoder,
    int? MaxHeight, int? MaxFps);    // null = исходное (по умолчанию, US-3.1)

enum CompressionLevel { Minimal = 25, Balanced = 50, Strong = 75, Maximum = 100 }
```

`QualityMapping` реализует research R-1:
- `I = 20 + 0.2·P`;
- для NVENC (HEVC и H.264): `cq = I`;
- для x265: `crf = 0.9·I − 8.5`;
- для x264: `crf = 0.85·I − 6`.

Значения форматируются с `CultureInfo.InvariantCulture`, иначе на русской локали десятичная точка превратится в запятую.

`EncoderKind.Gpu` в v1 означает NVENC. Когда появятся VideoToolbox, AMF и QSV (spec §3), `Gpu` станет семейством аппаратных кодировщиков, у каждого из которых будет своя калибровка.

### 4.4. `FfmpegArgumentsBuilder` (research R-3)

Чистая функция `IReadOnlyList<string> Build(input, output, VideoInfo, CompressionSettings)`:

```
-hide_banner -y -i <input>
-map 0:v:0 -map 0:a?
[-vf fps=N,scale=-2:H:flags=lanczos]              # только нужные фильтры (US-3)
<кодек-специфичные аргументы>
-pix_fmt yuv420p
-c:a aac -b:a 160k
-movflags +faststart
-progress pipe:1 -nostats
<output>
```

Аргументы видеокодека:

| Формат / кодировщик | Аргументы |
|---------------------|-----------|
| HEVC / GPU | `-c:v hevc_nvenc -preset p6 -tune hq -rc vbr -cq {I} -b:v 0 -bf 4 -b_ref_mode middle -spatial-aq 1 -temporal-aq 1 -rc-lookahead 32 -tag:v hvc1` |
| H.264 / GPU | `-c:v h264_nvenc` + те же параметры, без `-tag:v hvc1` |
| HEVC / CPU | `-c:v libx265 -preset medium -crf {0.9·I−8.5} -tag:v hvc1 -x265-params log-level=error` |
| H.264 / CPU | `-c:v libx264 -preset medium -crf {0.85·I−6}` |

`-y` здесь безопасен: путь назначения всегда заранее проверен `OutputPathResolver` или явно подтверждён в диалоге сохранения.

### 4.5. `EncoderDetector` (US-4.1, US-4.2)

Делает пробное кодирование: `ffmpeg -f lavfi -i color=black:s=256x256:d=0.1 -c:v hevc_nvenc -f null -`.
- Код возврата 0 — NVENC доступен.
- Проверка запускается один раз при старте, в фоне. Тайм-аут 10 с.
- На macOS проверка пропускается: NVENC там невозможен.
- Пока проверка идёт, по умолчанию выбран CPU. Если видеокарта доступна, выбор переключается на неё, если только сохранённые настройки не говорят иначе.

### 4.6. `VideoCompressionService` (US-1.2–1.4, US-6, NFR-003, NFR-004)

```csharp
Task<CompressionResult> CompressAsync(
    string input, string output, VideoInfo info, CompressionSettings settings,
    IProgress<CompressionProgress>? progress, CancellationToken ct);
```

- **Запуск процесса:** `ProcessStartInfo.ArgumentList`, без ручного экранирования. Это решает проблему кириллицы, пробелов и кавычек на всех ОС (NFR-004). Также `CreateNoWindow`, оба потока читаются асинхронно, чтобы процесс не завис на переполненном буфере.
- **Прогресс:** `FfmpegProgressParser` разбирает строки `out_time_us=…` и `progress=end` из stdout. Доля = `out_time / Duration`, ограниченная диапазоном 0..1. Оставшееся время считается по прошедшему: `elapsed × (1 − p) / p`.
- **Ошибки:** хранятся последние ~30 строк stderr. При ненулевом коде возврата выбрасывается `FfmpegException` с понятным текстом для известных случаев: «No space left on device» → «Недостаточно места на диске». Остальные случаи показываются с технической деталью.
- **Отмена и ошибка:** `Process.Kill(entireProcessTree: true)`, работает на всех ОС; затем ожидание выхода и удаление недописанного файла.
- **Результат:** `CompressionResult(OutputPath, OriginalSize, CompressedSize, Elapsed)`.

### 4.7. `OutputPathResolver` (US-5)

- `<папка исходника>/<имя>_compressed.mp4`, пути строятся только через `Path.Combine`.
- Если такой файл есть, пробует `<имя>_compressed (1).mp4`, `(2)` и т.д.
- Отдельно проверяет, что путь результата не совпадает с исходником. Пути нормализуются через `Path.GetFullPath`; сравнение без учёта регистра на Windows и macOS, с учётом регистра на Linux (`PlatformInfo`).

### 4.8. `HevcPlaybackSupport`: умеет ли ОС показывать H.265 (spec US-4.4, FR-002, SC-007)

`Platform/HevcPlaybackSupport.cs`, статический `bool IsSupported()`. Вызывается один раз при старте, быстрый (миллисекунды), никогда не бросает исключений.

| ОС | Как определяется | Почему так |
|----|------------------|------------|
| Windows | `MFTEnumEx` из `mfplat.dll`: есть ли зарегистрированный декодер Media Foundation для `MFVideoFormat_HEVC` (синхронный, асинхронный, аппаратный, локальный; без «field of use» и «transcode only») | Встроенные плееры Windows («Кино и ТВ», «Медиаплеер», Windows Media Player) воспроизводят через Media Foundation. Запрос учитывает и расширение «Видео HEVC» из Store, и аппаратные декодеры (например, Intel). Проверено на машине автора: HEVC — 0 декодеров (видео без картинки), H.264 — 1, VP9 — 1 |
| macOS | Всегда `true` | QuickTime и Safari поддерживают HEVC с macOS 10.13, а .NET 10 требует более новую macOS |
| Linux | `true`, если найден плагин GStreamer `libgstlibav.so` в стандартных каталогах `gstreamer-1.0` | Стандартные плееры (Totem/«Видео») работают через GStreamer; H.265 в них даёт `gstreamer1.0-libav`. Эвристика, вживую не проверена (NFR-005) |

При ошибке запроса (нет `mfplat.dll` — например, Windows N без Media Feature Pack) возвращается `false`: при сомнении выбирается формат, который откроется везде.

Поведение в UI (`MainViewModel`):
- **Автовыбор** при старте, если формат не выбран пользователем вручную: `IsSupported() ? Efficient : Compatible`.
- Пояснение под форматом зависит от ситуации:
  - автоматически выбран H.264 → «Выбран автоматически: на этом компьютере нет кодека для H.265…» + кнопка «Установить кодек HEVC»;
  - вручную выбран H.265 без кодека → предупреждение «во встроенном плеере будет только звук» + та же кнопка.
- Кнопка открывает страницу расширения в Microsoft Store (`ms-windows-store://pdp/?ProductId=9NMZLZ57R3T7`, через `Launcher`); показывается только на Windows.
- Флаг «выбран пользователем» ставится только при изменении формата из UI, не при автовыборе и не при загрузке настроек.

## 5. Приложение (Avalonia)

### 5.1. Макет окна

```
┌─ Video Compressor ─────────────────────────────────────────────┐
│ ┌────────────────────────────────────────────────────────────┐ │
│ │   Перетащите видео сюда            [ Выбрать файл… ]       │ │
│ │   LittleKittyBigCity 2026.10.22 - 22.34.55.02.mp4          │ │
│ │   2560×1440 · 60 fps · 01:05 · 190,5 МБ · H.264            │ │
│ └────────────────────────────────────────────────────────────┘ │
│ Степень сжатия   [ Сбалансированное         ▾]                 │
│                  Оптимально для записей экрана и игр           │
│                  0% ━━━━━━━━━━●━━━━━━━━━━ 100%      50%        │
│ Формат           [ Максимальное сжатие (H.265) ▾]              │
│ Кодировщик       [ Видеокарта (NVIDIA NVENC)   ▾]              │
│ ▸ Дополнительно                                                │
│     Разрешение   [ Исходное ▾]    Частота кадров [ Исходная ▾] │
│ Сохранить в      [ …/LittleKitty…_compressed.mp4 ] [ … ]       │
│ ───────────────────────────────────────────────────────────── │
│ [████████████░░░░░░░░░░░░░]  47% · осталось ~0:23              │
│ Готово за 0:43: 190,5 МБ → 36,4 МБ (−81%)   [Показать файл]    │
│                                       [ Отмена ]  [ Сжать ]    │
└────────────────────────────────────────────────────────────────┘
```

- Разрешение и FPS спрятаны в свёрнутый блок «Дополнительно» (`Expander`), потому что это опциональные настройки (US-3.1).
- Во время сжатия все настройки заблокированы.
- Тема Fluent следует системной светлой или тёмной теме.

### 5.2. `MainViewModel`

ViewModel не ссылается на типы окон Avalonia. Диалоги и действия ОС приходят через `IDialogService` и `PlatformShell`, поэтому ViewModel можно тестировать без UI.

| Свойство / команда | Назначение |
|--------------------|------------|
| `SelectedLevel` (уровень или «Своя настройка») ↔ `CompressionPercent` (0..100) | Синхронизация по US-2.4. Флаг-«защёлка» не даёт обновлениям зациклиться |
| `SelectedFormat`, `SelectedEncoder`, `SelectedResolution`, `SelectedFps` | Варианты из US-3 и US-4. Видеокарта убирается из списка, если NVENC недоступен |
| `VideoInfo`, `VideoSummary` | US-1.1 |
| `OutputPath` | US-5 |
| `Progress`, `StatusText`, `IsBusy`, `LastResult` | US-1.2 и US-1.3 |
| `BrowseInputCommand`, `BrowseOutputCommand` | `IDialogService` → `StorageProvider.OpenFilePickerAsync` / `SaveFilePickerAsync` (нативные диалоги ОС) |
| `CompressCommand` (`AsyncRelayCommand`, `IncludeCancelCommand = true`) → `CompressCancelCommand` | US-1.2 и US-6 |
| `RevealOutputCommand` | `PlatformShell.RevealInFileManager` (US-1.3), см. §5.3 |
| `LoadFileAsync(path)` | Вызывается из диалога и из drag & drop |

### 5.3. `PlatformShell`: «показать файл в папке» (US-1.3)

| ОС | Реализация |
|----|------------|
| Windows | `explorer.exe /select,"<файл>"` — открывает папку и выделяет файл |
| macOS | `open -R "<файл>"` — показывает файл в Finder |
| Linux | `Launcher.LaunchDirectoryInfoAsync(<папка>)` из Avalonia — открывает папку в файловом менеджере по умолчанию; выделить файл переносимо нельзя |

### 5.4. Граничные случаи (spec §9)

| Случай | Поведение |
|--------|-----------|
| Результат ≥ исходника | После завершения: `ConfirmDialog` «Сжатый файл не меньше исходного. Удалить его?» (Да / Нет) |
| Закрытие окна во время сжатия | `Window.Closing`: подтверждение. При «Да» — отмена закрытия, отмена команды, ожидание очистки, затем повторное закрытие |
| HDR на входе | Под параметрами видео — предупреждение «HDR-видео: цвета могут исказиться» |
| Нет звука | `-map 0:a?` не падает, когда звуковых дорожек нет |
| Путь результата = исходник | Кнопка «Сжать» недоступна, под путём — сообщение об ошибке |
| Не видео / повреждено | `FfmpegException` из `MediaProbe` → понятное сообщение, файл не выбирается |
| FFmpeg не найден | Баннер с инструкцией для текущей ОС, «Сжать» недоступна (FR-006) |
| Drag & drop не локального файла (например, из браузера) | Если у элемента нет локального пути, он игнорируется с сообщением «Перетащите файл с диска» |

### 5.5. `SettingsStore` (FR-008)

- Сохраняет `CompressionPercent`, `Format`, `Encoder`, `MaxHeight` и `MaxFps` в `settings.json`.
- `Format` хранится только если пользователь выбрал его вручную; `null` означает «автовыбор» (§4.8).
- Читает при старте, пишет после успешного сжатия и при закрытии окна.
- Повреждённый или отсутствующий файл заменяется настройками по умолчанию, без ошибки.

## 6. Тестирование

| Уровень | Что проверяем | Как |
|---------|---------------|-----|
| Юнит (xUnit) | `QualityMapping`: границы (0% и 100%), уровни, монотонность, формат с точкой в русской локали | Без FFmpeg |
| Юнит | `FfmpegArgumentsBuilder`: аргументы для 4 комбинаций формата и кодировщика; фильтры только при необходимости; не увеличивать разрешение и FPS; допуск FPS 59,99 | Без FFmpeg |
| Юнит | `FfmpegProgressParser`: `out_time_us`, `N/A`, `progress=end` | Без FFmpeg |
| Юнит | `OutputPathResolver`: суффиксы `(1)`, `(2)`; совпадение с исходником | Временная папка |
| Интеграция | `MediaProbe` и `VideoCompressionService` на сгенерированном 2-секундном клипе (`lavfi testsrc2 + sine`) с кириллицей и пробелами в пути; отмена удаляет файл; длительность сохраняется | Пропускается, если FFmpeg не найден |
| Сборка (SC-006) | `dotnet publish -r <rid>` для `win-x64`, `osx-arm64`, `osx-x64`, `linux-x64` | Локально на Windows: кросс-публикация .NET не требует целевой ОС |
| Ручная приёмка | SC-001…SC-005 на эталонной записи (Windows + NVIDIA); визуальная оценка SC-002 | Чек-лист в `tasks.md` |

**Рекомендация после MVP.** В GitHub Actions можно запускать юнит- и интеграционные тесты на `windows-latest`, `macos-latest` и `ubuntu-latest`, ставя FFmpeg на раннерах. Это даст первую реальную проверку ядра на macOS и Linux без собственного железа (кодирование на процессоре). Для публичного репозитория это бесплатно. В v1 не входит, кандидат в tasks P3.

## 7. Трассировка требований

| Требование | Где реализуется |
|------------|-----------------|
| US-1 | `MediaProbe`, `VideoCompressionService`, `MainViewModel`, `MainWindow` (drag & drop), `PlatformShell` |
| US-2 | `CompressionLevel`, `QualityMapping`, `MainViewModel` (синхронизация) |
| US-3 | `FfmpegArgumentsBuilder` (фильтры), «Дополнительно» в UI |
| US-4 | `EncoderDetector`, `FfmpegArgumentsBuilder` |
| US-5 | `OutputPathResolver`, `BrowseOutputCommand` |
| US-6 | `CompressCancelCommand`, `VideoCompressionService` (kill + удаление) |
| FR-001 | Фильтр диалога: `*.mp4;*.mkv;*.mov;*.avi;*.webm`. Drag & drop принимает любой файл, проверку делает ffprobe |
| FR-002 | `-movflags +faststart`, `yuv420p`, `hvc1` |
| FR-003 | `-map 0:a?` |
| FR-004 | Режим VFR по умолчанию (research R-3), интеграционный тест длительности |
| FR-005, FR-006 | `FfmpegException`, `FfmpegTools.TryLocate`, баннер с инструкцией для ОС |
| FR-007 | Все строки UI на русском |
| FR-008 | `SettingsStore` |
| NFR-001 | Нет сетевого кода |
| NFR-002 | Пресеты `p6` и `medium` (research §2.2, R-3) |
| NFR-003 | Асинхронный запуск процесса, `IProgress<T>` |
| NFR-004 | `ArgumentList` + интеграционный тест с кириллицей |
| NFR-005 | Avalonia, `net10.0`, `PlatformInfo`, `PlatformShell`; SC-006 — кросс-публикация |

## 8. Риски

| Риск | Митигация |
|------|-----------|
| Калибровка сделана на одной игровой записи; на записях рабочего стола размеры будут другими | Это ожидаемо (spec US-2.5). Шкала монотонна, и при необходимости уровни можно перекалибровать по `research.md` |
| Калибровка x264 приблизительная (2 точки) | Вариант CPU + H.264 редкий; допустимо для v1 |
| macOS и Linux не проверены вживую | Платформенный код изолирован (принцип 5) и минимален; SC-006 гарантирует сборку; CI-матрица — следующий шаг (§6) |
| macOS: неподписанное приложение блокируется Gatekeeper | Относится к развёртыванию, вне рамок v1 (spec §3). В README — запуск через `dotnet run` |
| Linux: drag & drop и нативные диалоги зависят от окружения рабочего стола | Avalonia поддерживает X11 и XWayland; кнопка «Выбрать файл…» остаётся запасным путём |
| Старый драйвер или видеокарта без NVENC HEVC | `EncoderDetector` откатывается на CPU (US-4.2) |
| На чужом ПК FFmpeg без NVENC или без x265 | Детектор + понятное сообщение; полноценное развёртывание — после MVP |
