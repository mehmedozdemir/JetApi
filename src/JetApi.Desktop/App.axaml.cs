using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop;

/// <summary>The Avalonia application; composes services and the main window.</summary>
public sealed partial class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(
                new AppSettingsStore(AppSettingsStore.DefaultFilePath),
                new DialogService(() => window),
                ApplyTheme,
                action => Dispatcher.UIThread.Post(action));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => viewModel.Dispose();
            window.Opened += async (_, _) => await viewModel.OpenLastWorkspaceAsync().ConfigureAwait(true);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Applies a theme preference.</summary>
    public void ApplyTheme(AppTheme theme) => RequestedThemeVariant = theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
