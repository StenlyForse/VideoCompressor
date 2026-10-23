using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VideoCompressor.App.Services;
using VideoCompressor.App.ViewModels;
using VideoCompressor.App.Views;
using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.Platform;

namespace VideoCompressor.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainViewModel(
                new DialogService(window),
                new PlatformShell(window),
                FfmpegTools.TryLocate(),
                new SettingsStore(SettingsStore.DefaultPath),
                HevcPlaybackSupport.IsSupported());
            window.DataContext = viewModel;
            window.Opened += async (_, _) => await viewModel.InitializeAsync();
            window.Closed += (_, _) => viewModel.SaveSettings();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
