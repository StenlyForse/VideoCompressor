# Video Compressor

Локальная кросс-платформенная утилита для сжатия видео без заметной потери качества. Задумана для тяжёлых записей экрана, например из NVIDIA App (~200 МБ на минуту), как замена онлайн-сервисам сжатия.

- Работает полностью локально, без загрузки файлов в сеть.
- Четыре уровня сжатия и точная настройка ползунком в процентах.
- Быстрое кодирование на видеокарте NVIDIA (NVENC) или более компактное на процессоре.

> **Статус:** в разработке (v1, MVP). Проверено и откалибровано только на Windows с видеокартой NVIDIA. На macOS и Linux приложение собирается, но ещё не проверялось.

## Требования

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [FFmpeg](https://ffmpeg.org/) 6 или новее, вместе с `ffprobe`. В репозиторий FFmpeg не входит, его нужно установить отдельно (см. ниже).

### Установка FFmpeg

| ОС | Команда |
|----|---------|
| Windows | `winget install Gyan.FFmpeg` |
| macOS   | `brew install ffmpeg` |
| Linux (Debian/Ubuntu) | `sudo apt install ffmpeg` |

Приложение ищет `ffmpeg` и `ffprobe` в таком порядке: рядом со своим исполняемым файлом, в подпапке `ffmpeg/`, затем в `PATH`.

## Сборка и запуск

```sh
dotnet build
dotnet run --project src/VideoCompressor.App
```

Тесты:

```sh
dotnet test
```

## Структура

| Путь | Назначение |
|------|------------|
| `src/VideoCompressor.Core` | Ядро: работа с FFmpeg, параметры сжатия. Не зависит от UI |
| `src/VideoCompressor.App` | Оконное приложение на [Avalonia](https://avaloniaui.net/) |
| `tests/VideoCompressor.Core.Tests` | Тесты ядра (xUnit v3) |
| `specs/` | Спецификации: требования, исследование параметров кодирования, план, задачи |

Проект ведётся по принципу spec-driven development: подробности — в [specs/001-video-compressor](specs/001-video-compressor/).
