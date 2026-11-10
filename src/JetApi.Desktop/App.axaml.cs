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
                action => Dispatcher.UIThread.Post(action),
                ApplyAppearance);
            if (measurement is null)
            {
                viewModel.Updates = CreateUpdates(viewModel, window);
            }

            window.DataContext = viewModel;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => viewModel.Dispose();
            window.Opened += measurement is null
                ? async (_, _) => await StartAsync(viewModel, desktop.Args ?? []).ConfigureAwait(true)
                : async (_, _) => await MeasureAsync(measurement, window, viewModel, desktop).ConfigureAwait(true);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Update checks talk to GitHub only when asked (or when the user turned on checking at start-up). ADR-0011.
    private static UpdatesViewModel CreateUpdates(MainWindowViewModel viewModel, MainWindow window)
    {
        var version = AppInfo.Current.Version;
        var api = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var downloads = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        downloads.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("JetApi", version.ToString()));
        return new UpdatesViewModel(new GitHubReleases(api), new UpdateInstaller(downloads), viewModel.Dialogs, version, viewModel.PrepareToLeaveAsync, window.CloseWithoutAsking);
    }

    // Reopens the last workspace; after an update, tidies up and shows what is new; checks for updates if the user asked to.
    private static async Task StartAsync(MainWindowViewModel viewModel, IReadOnlyList<string> args)
    {
        await viewModel.OpenLastWorkspaceAsync().ConfigureAwait(true);
        var updatedFrom = UpdateInstaller.UpdatedFrom(args);
        if (updatedFrom is not null || Directory.Exists(UpdateInstaller.DefaultWorkRoot))
        {
            var installDirectory = Path.GetDirectoryName(Environment.ProcessPath);
            _ = Task.Run(async () =>
            {
                // The previous process may still be closing; give it a moment to release its files.
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                UpdateInstaller.CleanUp(updatedFrom is null ? null : installDirectory, UpdateInstaller.DefaultWorkRoot);
            });
        }

        if (updatedFrom is not null)
        {
            await viewModel.ShowUpdatedNotesAsync(updatedFrom).ConfigureAwait(true);
        }
        else if (viewModel.Settings.CheckForUpdates && viewModel.Updates is { } updates)
        {
            // After start-up work, so the check never competes with opening the workspace (NFR-1).
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            await updates.CheckQuietlyAsync().ConfigureAwait(true);
        }
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

    /// <summary>Applies fonts, editor size and wrapping.</summary>
    public void ApplyAppearance(AppSettings settings) => Appearance.Apply(this, settings);

    /// <summary>Applies a theme preference.</summary>
    public void ApplyTheme(AppTheme theme) => RequestedThemeVariant = theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
