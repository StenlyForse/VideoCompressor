namespace VideoCompressor.App.Services;

/// <summary>How to install FFmpeg on the current OS (FR-006, plan §4.1).</summary>
public static class FfmpegInstallHint
{
    public static string Command { get; } =
        OperatingSystem.IsWindows() ? "winget install Gyan.FFmpeg"
        : OperatingSystem.IsMacOS() ? "brew install ffmpeg"
        : "sudo apt install ffmpeg";

    public static string Message { get; } =
        "FFmpeg не найден. Установите его командой ниже и перезапустите приложение" +
        (OperatingSystem.IsLinux() ? " (или через пакетный менеджер вашего дистрибутива)." : ".");
}
