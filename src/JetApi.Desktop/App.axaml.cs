using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using JetApi.Core.Model;
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
            var measurement = StartupMeasurement.FromArgs(desktop.Args ?? []);
            var settingsFile = measurement is null
                ? AppSettingsStore.DefaultFilePath
                : Path.Combine(Path.GetTempPath(), "jetapi-measure-" + Guid.NewGuid().ToString("N"), "settings.json");
            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(
                new AppSettingsStore(settingsFile),
                new DialogService(() => window),
                ApplyTheme,
                action => Dispatcher.UIThread.Post(action));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => viewModel.Dispose();
            window.Opened += measurement is null
                ? async (_, _) => await viewModel.OpenLastWorkspaceAsync().ConfigureAwait(true)
                : async (_, _) => await MeasureAsync(measurement, window, viewModel, desktop).ConfigureAwait(true);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Opens the workspace, waits for its first rendered frame, idles, then reports and exits (NFR-1, NFR-2).
    private static async Task MeasureAsync(StartupMeasurement measurement, MainWindow window, MainWindowViewModel viewModel, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var windowShown = StartupMeasurement.SinceProcessStart();
        await viewModel.OpenWorkspaceAsync(measurement.Workspace, offerCreate: false).ConfigureAwait(true);
        var rendered = new TaskCompletionSource();
        window.RequestAnimationFrame(_ => rendered.TrySetResult());
        await rendered.Task.ConfigureAwait(true);
        var workspaceReady = StartupMeasurement.SinceProcessStart();

        var requests = viewModel.Workspace?.Session.Workspace.Collections.Sum(c => CollectionTree.EnumerateRequests(c).Count()) ?? 0;
        await Task.Delay(StartupMeasurement.IdleDelay).ConfigureAwait(true);
        await measurement.WriteAsync(StartupMeasurement.CreateReport(windowShown, workspaceReady, requests)).ConfigureAwait(true);
        desktop.Shutdown();
    }

    /// <summary>Applies a theme preference.</summary>
    public void ApplyTheme(AppTheme theme) => RequestedThemeVariant = theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
