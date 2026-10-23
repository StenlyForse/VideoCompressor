using System.Text.Json;
using System.Text.Json.Serialization;
using VideoCompressor.Core.Compression;

namespace VideoCompressor.App.Services;

/// <summary>The choices remembered between runs (FR-008).</summary>
/// <param name="Format">Only a format the user picked; <c>null</c> means "choose automatically" (plan §4.8).</param>
public sealed record AppSettings(
    int CompressionPercent = (int)CompressionLevel.Balanced,
    OutputFormat? Format = null,
    EncoderKind Encoder = EncoderKind.Gpu,
    int? MaxHeight = null,
    int? MaxFps = null);

/// <summary>
/// Stores <see cref="AppSettings"/> as JSON in the per-user application data folder
/// (%AppData% on Windows, ~/.config on Linux and macOS). Never throws: a missing or broken
/// file just means defaults (plan §5.5).
/// </summary>
public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoCompressor", "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(filePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            // Write-then-move, so a crash mid-write never leaves a truncated file behind.
            var temp = filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth bothering the user: the next run simply starts with the previous settings.
        }
    }
}
