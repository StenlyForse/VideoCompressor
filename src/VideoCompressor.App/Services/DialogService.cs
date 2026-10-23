using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VideoCompressor.App.Views;

namespace VideoCompressor.App.Services;

public sealed class DialogService(Window owner) : IDialogService
{
    // FR-001: formats offered in the picker; drag & drop accepts anything and lets ffprobe decide.
    private static readonly FilePickerFileType VideoFiles = new("Видео")
    {
        Patterns = ["*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm"],
        MimeTypes = ["video/*"],
        AppleUniformTypeIdentifiers = ["public.movie"],
    };

    private static readonly FilePickerFileType Mp4 = new("Видео MP4")
    {
        Patterns = ["*.mp4"],
        MimeTypes = ["video/mp4"],
        AppleUniformTypeIdentifiers = ["public.mpeg-4"],
    };

    public async Task<IReadOnlyList<string>> PickVideoFilesAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите видео",
            AllowMultiple = true,
            FileTypeFilter = [VideoFiles, FilePickerFileTypes.All],
        });
        return [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
    }

    public async Task<string?> PickOutputFileAsync(string suggestedPath)
    {
        var folder = await owner.StorageProvider.TryGetFolderFromPathAsync(
            Path.GetDirectoryName(suggestedPath) ?? suggestedPath);
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить сжатое видео",
            SuggestedFileName = Path.GetFileName(suggestedPath),
            SuggestedStartLocation = folder,
            DefaultExtension = "mp4",
            FileTypeChoices = [Mp4],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public Task<bool> ConfirmAsync(string title, string message, string yes, string no) =>
        new ConfirmDialog(title, message, yes, no).ShowDialog<bool>(owner);
}
