namespace VideoCompressor.App.Services;

public interface IPlatformShell
{
    /// <summary>Opens the file manager at the file, selecting it where the OS allows (plan §5.3).</summary>
    Task RevealFileAsync(string path);

    /// <summary>Opens a link with its default handler (browser, Microsoft Store, …).</summary>
    Task OpenUriAsync(Uri uri);
}
