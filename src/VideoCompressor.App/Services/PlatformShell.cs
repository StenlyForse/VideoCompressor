using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace VideoCompressor.App.Services;

public sealed class PlatformShell(TopLevel topLevel) : IPlatformShell
{
    public async Task RevealFileAsync(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // explorer.exe parses its own command line: "/select," must be glued to the quoted path.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var startInfo = new ProcessStartInfo("open") { UseShellExecute = false };
            startInfo.ArgumentList.Add("-R");
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo);
            return;
        }

        // Linux: there is no portable way to select a file, so open its folder.
        var directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(directory));
        }
    }

    public async Task OpenUriAsync(Uri uri) => await topLevel.Launcher.LaunchUriAsync(uri);
}
