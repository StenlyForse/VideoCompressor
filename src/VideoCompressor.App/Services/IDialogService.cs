namespace VideoCompressor.App.Services;

/// <summary>Dialogs for the view model, so that it never touches windows directly (plan §5.2).</summary>
public interface IDialogService
{
    /// <returns>Local path of the chosen video, or <c>null</c> if cancelled.</returns>
    Task<string?> PickVideoFileAsync();

    /// <returns>Local path to save to, or <c>null</c> if cancelled.</returns>
    Task<string?> PickOutputFileAsync(string suggestedPath);

    Task<bool> ConfirmAsync(string title, string message, string yes, string no);
}
