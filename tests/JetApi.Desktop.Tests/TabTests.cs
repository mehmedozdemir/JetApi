using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetApi.Core.Model;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;
using JetApi.Desktop.Views;

namespace JetApi.Desktop.Tests;

// Pinned tabs and closing tabs in bulk (the tab menu).
public sealed class TabTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // examples: [get-example, post-json, third, fourth]
    private static async Task<TempWorkspace> CreateAsync()
    {
        var workspace = await TempWorkspace.CreateAsync();
        await workspace.Session.AddNodeAsync("examples", [], new HttpRequestDefinition { Key = "x", Name = "Third" }, Token);
        await workspace.Session.AddNodeAsync("examples", [], new HttpRequestDefinition { Key = "x", Name = "Fourth" }, Token);
        return workspace;
    }

    private static void OpenAll(WorkspaceViewModel vm)
    {
        foreach (var node in vm.Collections[0].Children)
        {
            vm.OpenNode(node);
        }
    }

    private static string[] Names(WorkspaceViewModel vm) => [.. vm.Tabs.Select(t => t.Name)];

    [Fact]
    public async Task Pinning_MovesTheTabFirst_AndBulkClosingLeavesItOpen()
    {
        using var workspace = await CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        OpenAll(vm);
        Names(vm).ShouldBe(["Get example", "Post JSON", "Third", "Fourth"]);
        var third = vm.Tabs[2];
        vm.SelectedTab = vm.Tabs[1];

        // Pinning moves the tab after the other pinned ones; the active tab stays active.
        third.TogglePinCommand.Execute(null);
        third.IsPinned.ShouldBeTrue();
        Names(vm).ShouldBe(["Third", "Get example", "Post JSON", "Fourth"]);
        vm.SelectedTab!.Name.ShouldBe("Post JSON");
        vm.Tabs[3].TogglePinCommand.Execute(null);
        Names(vm).ShouldBe(["Third", "Fourth", "Get example", "Post JSON"]);

        // Unpinning puts it right after the pinned tabs.
        vm.SetTabPinned(third, false);
        Names(vm).ShouldBe(["Fourth", "Third", "Get example", "Post JSON"]);
        vm.SetTabPinned(third, true);
        Names(vm).ShouldBe(["Fourth", "Third", "Get example", "Post JSON"]);

        // Close others: the tab becomes active; pinned tabs stay.
        await vm.CloseOtherTabsCommand.ExecuteAsync(vm.Tabs[2]);
        Names(vm).ShouldBe(["Fourth", "Third", "Get example"]);
        vm.SelectedTab!.Name.ShouldBe("Get example");

        // Close to the right.
        vm.OpenNode(vm.Collections[0].Children[1]);
        vm.OpenNode(vm.Collections[0].Children[0]);
        await vm.CloseTabsToRightCommand.ExecuteAsync(vm.Tabs[0]);
        Names(vm).ShouldBe(["Fourth", "Third"]);

        // Close all closes only unpinned tabs; a single close (Ctrl+W, the menu) closes a pinned one too.
        vm.OpenNode(vm.Collections[0].Children[1]);
        await vm.CloseAllTabsCommand.ExecuteAsync(null);
        Names(vm).ShouldBe(["Fourth", "Third"]);
        await vm.CloseTabCommand.ExecuteAsync(vm.Tabs[0]);
        Names(vm).ShouldBe(["Third"]);
    }

    [Fact]
    public async Task PinnedTabs_AreRemembered()
    {
        using var workspace = await CreateAsync();
        using (var vm = workspace.CreateViewModel(new FakeDialogs()))
        {
            OpenAll(vm);
            vm.SetTabPinned(vm.Tabs[3], true);
            vm.SelectedTab = vm.Tabs[2];
        }

        using var reopened = workspace.CreateViewModel(new FakeDialogs());
        reopened.RestoreTabs();
        Names(reopened).ShouldBe(["Fourth", "Get example", "Post JSON", "Third"]);
        reopened.Tabs.Select(t => t.IsPinned).ShouldBe([true, false, false, false]);
        reopened.SelectedTab!.Name.ShouldBe("Post JSON");
    }

    [Theory]
    [InlineData(UnsavedChangesChoice.Cancel)]
    [InlineData(UnsavedChangesChoice.Discard)]
    [InlineData(UnsavedChangesChoice.Save)]
    public async Task ClosingInBulk_AsksOnceAboutUnsavedTabs(UnsavedChangesChoice choice)
    {
        using var workspace = await CreateAsync();
        var dialogs = new FakeDialogs { UnsavedChoice = choice };
        using var vm = workspace.CreateViewModel(dialogs);
        OpenAll(vm);
        ((HttpRequestTabViewModel)vm.Tabs[0]).Url = "https://example.com/a";
        ((HttpRequestTabViewModel)vm.Tabs[3]).Url = "https://example.com/d";

        // Tabs without changes close without asking.
        await vm.CloseSavedTabsCommand.ExecuteAsync(null);
        dialogs.UnsavedAsked.ShouldBeEmpty();
        Names(vm).ShouldBe(["Get example", "Fourth"]);

        await vm.CloseAllTabsCommand.ExecuteAsync(null);
        dialogs.UnsavedAsked.ShouldHaveSingleItem().ShouldBe(["Examples  /  Get example", "Examples  /  Fourth"]);
        vm.Tabs.Count.ShouldBe(choice == UnsavedChangesChoice.Cancel ? 2 : 0);
        if (choice == UnsavedChangesChoice.Save)
        {
            workspace.Read("collections/examples/get-example.http.yaml").ShouldContain("https://example.com/a");
        }
    }

    [Fact]
    public Task TabMenu_InTheWindow() => HeadlessApp.Session.Dispatch(WalkAsync, Token);

    private static async Task<bool> WalkAsync()
    {
        HeadlessApp.Log.Clear();
        using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
        using var vm = new MainWindowViewModel(new AppSettingsStore(Path.Combine(workspace.Root, ".jetapi", "settings.json")), new FakeDialogs(), _ => { }, action => Dispatcher.UIThread.Post(action));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
        var ws = vm.Workspace!;
        foreach (var name in new[] { "Create user", "List users", "Get user" })
        {
            ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == name));
        }

        Settle();

        // 1. The menu of the middle tab: every item is there; pin is last.
        var middle = ws.Tabs[1];
        var menu = WorkspaceView.BuildTabMenu(ws, middle);
        var items = menu.ItemsSource!.OfType<MenuItem>().ToList();
        items.Select(i => (string)i.Header!).ShouldBe(["Close", "Close others", "Close tabs to the right", "Close tabs without changes", "Close all", "Pin tab"]);
        items.ShouldAllBe(i => i.IsEnabled);
        var item = window.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.DataContext == middle);
        menu.Open(item);
        Settle();
        ScreenTests.Capture(window, "ux-61-tab-menu");
        menu.Close();

        // 2. Pinned: first, with a pin in place of the close button; a middle click does not close it.
        items[^1].Command!.Execute(items[^1].CommandParameter);
        Settle();
        ws.Tabs[0].ShouldBe(middle);
        item = window.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.DataContext == middle);
        var buttons = item.GetVisualDescendants().OfType<Button>().ToList();
        buttons.Single(b => b.Classes.Contains("tab-close")).IsVisible.ShouldBeFalse();
        buttons.Single(b => b.Classes.Contains("tab-pin")).IsVisible.ShouldBeTrue();
        var point = item.TranslatePoint(new Point(4, item.Bounds.Height - 4), window)!.Value;
        window.MouseDown(point, MouseButton.Middle);
        window.MouseUp(point, MouseButton.Middle);
        Settle();
        ws.Tabs.Count.ShouldBe(3);
        ScreenTests.Capture(window, "ux-62-tab-pinned");

        // 3. The last tab has nothing to its right; the pinned one shows "Unpin".
        WorkspaceView.BuildTabMenu(ws, ws.Tabs[2]).ItemsSource!.OfType<MenuItem>().Single(i => (string)i.Header! == "Close tabs to the right").IsEnabled.ShouldBeFalse();
        WorkspaceView.BuildTabMenu(ws, middle).ItemsSource!.OfType<MenuItem>().Last().Header.ShouldBe("Unpin tab");

        // 4. Dark theme, Turkish.
        ((App)Application.Current!).ApplyTheme(AppTheme.Dark);
        Loc.Instance.SetLanguage("tr");
        menu = WorkspaceView.BuildTabMenu(ws, ws.Tabs[1]);
        menu.Open(window.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.DataContext == ws.Tabs[1]));
        Settle();
        ScreenTests.Capture(window, "ux-63-tab-menu-dark-tr");
        menu.Close();

        // Close all leaves the pinned tab.
        await ws.CloseAllTabsCommand.ExecuteAsync(null);
        Settle();
        ws.Tabs.ShouldHaveSingleItem().ShouldBe(middle);
        ScreenTests.Capture(window, "ux-64-tab-close-all-dark-tr");
        Loc.Instance.SetLanguage("en");
        ((App)Application.Current!).ApplyTheme(AppTheme.Light);

        window.Close();
        HeadlessApp.Log.Messages.ShouldBeEmpty();
        return true;
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
