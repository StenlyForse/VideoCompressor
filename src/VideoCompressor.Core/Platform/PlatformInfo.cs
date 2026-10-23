namespace VideoCompressor.Core.Platform;

/// <summary>
/// The single place for OS-specific differences in the core (plan §2, principle 5).
/// </summary>
public static class PlatformInfo
{
    /// <summary>File-system paths are case-insensitive by default on Windows and macOS.</summary>
    public static StringComparison PathComparison { get; } =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Well-known tool locations that may be missing from PATH (plan §4.1):
    /// fresh winget installs on Windows, Homebrew for Finder-launched apps on macOS,
    /// a trimmed PATH when launched from a desktop menu on Linux.
    /// </summary>
    public static IReadOnlyList<string> ExtraToolDirectories { get; } = GetExtraToolDirectories();

    public static string ExecutableName(string tool) =>
        OperatingSystem.IsWindows() ? tool + ".exe" : tool;

    private static string[] GetExtraToolDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return [Path.Combine(localAppData, "Microsoft", "WinGet", "Links")];
        }

        if (OperatingSystem.IsMacOS())
        {
            return ["/opt/homebrew/bin", "/usr/local/bin"];
        }

        return ["/usr/bin", "/usr/local/bin", "/snap/bin"];
    }
}
