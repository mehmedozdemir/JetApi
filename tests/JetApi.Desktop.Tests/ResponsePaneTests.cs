using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetApi.Desktop.Controls;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

/// <summary>The response pane on the right or below the request, switched from the status bar.</summary>
public sealed class ResponsePaneTests
{
    [Fact]
    public void Settings_RoundTripThePanePosition()
    {
        var host = new FakeSettingsHost(new AppSettings { ResponsePane = ResponsePanePosition.Bottom });
        var settings = new SettingsViewModel(host, []);

        settings.ResponsePane!.Value.ShouldBe(ResponsePanePosition.Bottom);
        settings.ResponsePane = settings.ResponsePaneOptions.Single(o => o.Value == ResponsePanePosition.Right);

        host.Settings.ResponsePane.ShouldBe(ResponsePanePosition.Right);
    }

    [Fact]
    public Task Toggle_FromShortcutAndStatusBar_AndRememberTheDivider() =>
        HeadlessApp.Session.Dispatch(WalkAsync, TestContext.Current.CancellationToken);

    private static async Task<bool> WalkAsync()
    {
        HeadlessApp.Log.Clear();
        using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
        var settingsFile = Path.Combine(workspace.Root, ".jetapi", "settings.json");
        using var vm = new MainWindowViewModel(new AppSettingsStore(settingsFile), new FakeDialogs(), theme => ((App)Application.Current!).ApplyTheme(theme), action => Dispatcher.UIThread.Post(action),
            settings => ((App)Application.Current!).ApplyAppearance(settings));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
        var ws = vm.Workspace!;
        ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "Create user"));
        Settle();
        await ((HttpRequestTabViewModel)ws.SelectedTab!).SendAsync();
        Settle();

        var split = VisibleSplit(window);
        split.IsStacked.ShouldBeFalse();
        split.ColumnDefinitions.Count.ShouldBe(3);

        // 1. Ctrl+Alt+V puts the response below; the setting is saved.
        window.KeyPressQwerty(PhysicalKey.V, Shortcut.Command | RawInputModifiers.Alt);
        Settle();
        vm.Settings.ResponsePane.ShouldBe(ResponsePanePosition.Bottom);
        new AppSettingsStore(settingsFile).Load().ResponsePane.ShouldBe(ResponsePanePosition.Bottom);
        split.IsStacked.ShouldBeTrue();
        split.RowDefinitions.Count.ShouldBe(3);
        ws.IsResponseBelow.ShouldBeTrue();
        ws.ResponsePaneToggleText.ShouldBe(Loc.Get("Layout_ResponseRight"));
        split.Second!.Bounds.Top.ShouldBeGreaterThan(split.First!.Bounds.Bottom - 1);
        ScreenTests.Capture(window, "ux-35-response-below");

        // 2. Dragging the divider is remembered for this layout only.
        split.RaiseEvent(new SplitShareChangedEventArgs(SplitPane.ShareChangedEvent, isStacked: true, firstShare: 0.35));
        Settle();
        vm.Settings.RequestShareBottom.ShouldBe(0.35);
        vm.Settings.RequestShareRight.ShouldBe(0.5);
        split.RowDefinitions[0].Height.Value.ShouldBe(0.35, 0.001);

        // 3. gRPC in a small window, dark and Turkish: both panes keep their minimum height.
        ((App)Application.Current!).ApplyTheme(AppTheme.Dark);
        Loc.Instance.SetLanguage("tr");
        ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "Get user"));
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Settle();
        var grpcSplit = VisibleSplit(window);
        grpcSplit.IsStacked.ShouldBeTrue();
        grpcSplit.Second!.Bounds.Height.ShouldBeGreaterThanOrEqualTo(SplitPane.MinPaneHeight - 0.5);
        ScreenTests.Capture(window, "ux-36-grpc-response-below-min-dark-tr");
        Loc.Instance.SetLanguage("en");
        ((App)Application.Current!).ApplyTheme(AppTheme.Light);
        window.Width = 1280;
        window.Height = 800;

        // 4. The status bar button brings it back to the right.
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ResponsePaneButton");
        button.Command!.Execute(null);
        Settle();
        vm.Settings.ResponsePane.ShouldBe(ResponsePanePosition.Right);
        grpcSplit.IsStacked.ShouldBeFalse();
        ScreenTests.Capture(window, "ux-37-response-right-again");

        window.Close();
        HeadlessApp.Log.Messages.ShouldBeEmpty();
        return true;
    }

    private sealed class FakeSettingsHost(AppSettings settings) : ISettingsHost
    {
        public AppSettings Settings { get; private set; } = settings;

        public void ApplySettings(AppSettings settings) => Settings = settings;
    }

    private static SplitPane VisibleSplit(Window window) =>
        window.GetVisualDescendants().OfType<SplitPane>().Single(s => s.IsEffectivelyVisible);

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
