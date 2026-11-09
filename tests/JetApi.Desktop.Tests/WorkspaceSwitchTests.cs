using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;
using JetApi.Desktop.Views;

namespace JetApi.Desktop.Tests;

/// <summary>The workspace menu: saved workspaces, switching safely and reopening where the user left off.</summary>
public sealed class WorkspaceSwitchTests
{
    private static async Task<string> CreateOtherAsync(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "jetapi-desktop-tests", Guid.NewGuid().ToString("N"));
        await WorkspaceSession.CreateAsync(root, name);
        return root;
    }

    private static FakeWorkspaceHost HostWith(TempWorkspace workspace, params (string Path, string Name)[] others) => new()
    {
        Settings = new AppSettings
        {
            RecentWorkspaces = [new RecentWorkspace(workspace.Root, "Test Workspace", DateTimeOffset.Now), .. others.Select(o => new RecentWorkspace(o.Path, o.Name, DateTimeOffset.Now))],
        },
    };

    [Fact]
    public async Task OpenTabs_AreRememberedPerWorkspace_WithTheEnvironment()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        await workspace.Session.AddEnvironmentAsync("Staging", cancellationToken: TestContext.Current.CancellationToken);
        using (var first = workspace.CreateViewModel(new FakeDialogs()))
        {
            first.SelectedEnvironment = first.EnvironmentOptions.Single(o => o.Name == "Staging");
            first.OpenNode(first.Collections[0].Children[1]);
            first.OpenNode(first.Collections[0].Children[0]);
            first.SelectedTab = first.Tabs[1];
        }

        var session = await WorkspaceSession.OpenAsync(workspace.Root, TestContext.Current.CancellationToken);
        using var again = new WorkspaceViewModel(session, new RequestRunner(new ProtoSchemaCache()), new FakeDialogs(), new FakeWorkspaceHost(), watchFiles: false);
        again.RestoreTabs();

        again.Tabs.Select(t => t.Name).ShouldBe(["Post JSON", "Get example"]);
        again.SelectedTab!.Name.ShouldBe("Get example");
        session.ActiveEnvironment!.Name.ShouldBe("Staging");
        workspace.Read(".gitignore").ShouldContain(".jetapi/");
    }

    [Fact]
    public async Task Restoring_SkipsRequestsThatNoLongerExist()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        workspace.Session.WriteOpenTabs([new OpenTab("examples", ["gone"]), new OpenTab("examples", ["get-example"])], 0);
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        vm.RestoreTabs();

        vm.Tabs.ShouldHaveSingleItem().Name.ShouldBe("Get example");
    }

    [Fact]
    public async Task Choices_ListSavedWorkspaces_MarkingTheOpenOneAndMissingFolders()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var other = await CreateOtherAsync("Payments");
        var missing = Path.Combine(Path.GetTempPath(), "jetapi-missing-" + Guid.NewGuid().ToString("N"));
        using var vm = workspace.CreateViewModel(new FakeDialogs(), host: HostWith(workspace, (other, "Payments"), (missing, "Old")));

        var choices = vm.WorkspaceChoices();

        choices.Select(c => (c.Name, c.IsCurrent, c.Exists)).ShouldBe([("Test Workspace", true, true), ("Payments", false, true), ("Old", false, false)]);
        vm.OpenPalette();
        vm.Palette!.Query = "payments";
        vm.Palette.Results.ShouldHaveSingleItem().Label.ShouldBe(Loc.Format("Palette_SwitchWorkspace", "Payments"));
    }

    [Theory]
    [InlineData(UnsavedChangesChoice.Cancel, false, true)]
    [InlineData(UnsavedChangesChoice.Discard, true, true)]
    [InlineData(UnsavedChangesChoice.Save, true, false)]
    public async Task Switching_AsksAboutUnsavedRequests(UnsavedChangesChoice choice, bool switches, bool stillDirty)
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var other = await CreateOtherAsync("Payments");
        var dialogs = new FakeDialogs { UnsavedChoice = choice };
        var host = HostWith(workspace, (other, "Payments"));
        using var vm = workspace.CreateViewModel(dialogs, host: host);
        vm.OpenNode(vm.Collections[0].Children[0]);
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
        tab.Url = "https://example.com/changed";

        await vm.SwitchWorkspaceCommand.ExecuteAsync(other);

        dialogs.UnsavedAsked.ShouldHaveSingleItem().ShouldBe(["Examples  /  Get example"]);
        host.Opened.Count.ShouldBe(switches ? 1 : 0);
        tab.IsDirty.ShouldBe(stillDirty);
        if (choice == UnsavedChangesChoice.Save)
        {
            workspace.Read("collections/examples/get-example.http.yaml").ShouldContain("https://example.com/changed");
        }
    }

    [Fact]
    public async Task Switching_WithNothingUnsaved_DoesNotAsk_AndTheOpenWorkspaceIsANoOp()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var other = await CreateOtherAsync("Payments");
        var dialogs = new FakeDialogs();
        var host = HostWith(workspace, (other, "Payments"));
        using var vm = workspace.CreateViewModel(dialogs, host: host);

        await vm.SwitchWorkspaceCommand.ExecuteAsync(workspace.Root + Path.DirectorySeparatorChar);
        host.Opened.ShouldBeEmpty();

        await vm.SwitchWorkspaceCommand.ExecuteAsync(other);
        dialogs.UnsavedAsked.ShouldBeEmpty();
        host.Opened.ShouldBe([(other, false)]);
    }

    [Fact]
    public async Task Leaving_SavesTheCollectionScreen_AndClosingAsksToo()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var dialogs = new FakeDialogs { UnsavedChoice = UnsavedChangesChoice.Cancel };
        var host = new FakeWorkspaceHost();
        using var vm = workspace.CreateViewModel(dialogs, host: host);
        vm.OpenCollectionSettings(vm.Collections[0].CollectionKey);
        vm.NodeSettings!.Name = "Renamed";

        (await vm.PrepareToLeaveAsync()).ShouldBeTrue();
        workspace.Session.Workspace.Collections[0].Name.ShouldBe("Renamed");

        vm.OpenNode(vm.Collections[0].Children[0]);
        ((HttpRequestTabViewModel)vm.SelectedTab!).Url = "changed";
        await vm.CloseWorkspaceCommand.ExecuteAsync(null);
        host.CloseCount.ShouldBe(0);
        dialogs.UnsavedChoice = UnsavedChangesChoice.Discard;
        await vm.CloseWorkspaceCommand.ExecuteAsync(null);
        host.CloseCount.ShouldBe(1);
    }

    [Fact]
    public void TypeScale_FollowsTheInterfaceFontSize()
    {
        var scale = Appearance.TypeScale(15).ToDictionary(p => p.Key, p => p.Size);
        scale["App.FontSize.Body"].ShouldBe(15);
        scale["ControlContentThemeFontSize"].ShouldBe(15);
        scale["App.FontSize.Heading"].ShouldBe(16);
        scale["App.FontSize.Caption"].ShouldBe(12.5);
        Appearance.TypeScale(40).ToDictionary(p => p.Key, p => p.Size)["App.FontSize.Body"].ShouldBe(Appearance.MaxUiFontSize);
        Appearance.TypeScale(13).Single(p => p.Key == "App.FontSize.Title").Size.ShouldBe(18);
    }

    [Fact]
    public Task Menu_AndFontSize_InTheWindow() => HeadlessApp.Session.Dispatch(WalkAsync, TestContext.Current.CancellationToken);

    private static async Task<bool> WalkAsync()
    {
        HeadlessApp.Log.Clear();
        using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
        var other = await CreateOtherAsync("Payments API");
        var settingsFile = Path.Combine(workspace.Root, ".jetapi", "settings.json");
        new AppSettingsStore(settingsFile).Save(new AppSettings
        {
            RecentWorkspaces = [new RecentWorkspace(other, "Payments API", DateTimeOffset.Now), new RecentWorkspace(Path.Combine(Path.GetTempPath(), "jetapi-gone"), "Old demo", DateTimeOffset.Now)],
        });
        var dialogs = new FakeDialogs();
        using var vm = new MainWindowViewModel(new AppSettingsStore(settingsFile), dialogs, theme => ((App)Application.Current!).ApplyTheme(theme), action => Dispatcher.UIThread.Post(action),
            settings => ((App)Application.Current!).ApplyAppearance(settings));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
        var ws = vm.Workspace!;
        ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "Create user"));
        Settle();

        // 1. The menu lists only saved workspaces (open one checked, missing one disabled) and "Open another folder…".
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WorkspaceButton");
        var menu = WorkspaceView.BuildWorkspaceMenu(ws);
        menu.ShowAt(button);
        Settle();
        var items = menu.Items.OfType<MenuItem>().ToList();
        items.Count.ShouldBe(4);
        items.Select(i => ((WorkspaceChoice)i.Tag!).Name).Take(3).ShouldBe(["Test Workspace", "Payments API", "Old demo"]);
        items[0].Icon.ShouldNotBeNull();
        items[2].IsEnabled.ShouldBeFalse();
        items[3].Header.ShouldBe(Loc.Get("Top_OpenWorkspace"));
        ScreenTests.Capture(window, "ux-33-workspace-menu");
        if (TopLevel.GetTopLevel(items[0]) is { } popup && popup != window)
        {
            using var frame = popup.CaptureRenderedFrame();
            frame!.Save(Path.Combine(ScreenTests.ScreenshotDirectory, "ux-33-workspace-menu-popup.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        menu.Hide();

        // 2. Switching opens the other workspace; coming back reopens the request that was open.
        await ws.SwitchWorkspaceCommand.ExecuteAsync(other);
        Settle();
        vm.Workspace!.WorkspaceName.ShouldBe("Payments API");
        vm.Title.ShouldStartWith("Payments API");
        await vm.Workspace.SwitchWorkspaceCommand.ExecuteAsync(workspace.Root);
        Settle();
        vm.Workspace!.SelectedTab!.Name.ShouldBe("Create user");

        // 3. The interface font size applies at once (one step larger here).
        vm.ApplySettings(vm.Settings with { UiFontSize = 14 });
        Settle();
        ((double)Application.Current!.Resources["App.FontSize.Body"]!).ShouldBe(14);
        ScreenTests.Capture(window, "ux-34-interface-font-14");
        vm.ApplySettings(vm.Settings with { UiFontSize = AppSettings.DefaultUiFontSize });
        Settle();

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
