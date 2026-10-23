namespace VideoCompressor.Core.IO;

internal static class FileCleanup
{
    // Right after ffmpeg exits a fresh file can stay locked for a moment (e.g. an antivirus
    // scanning it in the user's Videos folder), so deletion is retried.
    private const int Attempts = 30;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>Best effort: gives up silently after ~3 s rather than hide the caller's own error.</summary>
    public static async Task DeleteWithRetryAsync(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < Attempts)
            {
                await Task.Delay(RetryDelay).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
