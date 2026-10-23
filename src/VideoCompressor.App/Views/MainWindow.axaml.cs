using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using VideoCompressor.App.ViewModels;

namespace VideoCompressor.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DragDrop.AddDragEnterHandler(this, OnDragEnter);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDragLeaveHandler(this, OnDragLeave);
        DragDrop.AddDropHandler(this, OnDrop);
    }

    // Set once the user agreed to close while compressing, so the second Close() goes through.
    private bool _closeConfirmed;

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || ViewModel is not { IsBusy: true } viewModel)
        {
            return;
        }

        // Closing cannot be awaited: cancel it now and close again once cleanup is done.
        e.Cancel = true;
        if (await viewModel.ConfirmCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private static bool HasFiles(DragEventArgs e) => e.DataTransfer.Contains(DataFormat.File);

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (HasFiles(e) && ViewModel is { IsIdle: true })
        {
            DropZone.Classes.Add("dragOver");
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = HasFiles(e) && ViewModel is { IsIdle: true } ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDragLeave(object? sender, DragEventArgs e) => DropZone.Classes.Remove("dragOver");

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Remove("dragOver");
        if (ViewModel is not { IsIdle: true } viewModel)
        {
            return;
        }

        var items = e.DataTransfer.TryGetFiles() ?? [];
        if (items.Length == 0)
        {
            return;
        }

        // Spec 003 US-1.1: every dropped local file is added; folders and remote items are skipped.
        var paths = items.Select(i => i.TryGetLocalPath()).OfType<string>().Where(File.Exists).ToList();
        if (paths.Count > 0)
        {
            await viewModel.AddFilesAsync(paths);
        }
        else
        {
            viewModel.ReportNonLocalDrop();
        }
    }
}
