using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetApi.Core.Model;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

// Moving collections, folders and requests in the sidebar tree: drop rules, files on disk and open tabs.
public sealed class TreeDragDropTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // examples: [get-example, post-json, admin/[list-users, delete-user]]; second: [ping]
    private static async Task<TempWorkspace> CreateAsync()
    {
        var workspace = await TempWorkspace.CreateAsync();
        var session = workspace.Session;
        var admin = await session.AddNodeAsync("examples", [], new Folder { Key = "x", Name = "Admin" }, Token);
        await session.AddNodeAsync("examples", admin, new HttpRequestDefinition { Key = "x", Name = "List users" }, Token);
        await session.AddNodeAsync("examples", admin, new HttpRequestDefinition { Key = "x", Name = "Delete user", Method = "DELETE" }, Token);
        var second = await session.AddCollectionAsync("Second", Token);
        await session.AddNodeAsync(second, [], new HttpRequestDefinition { Key = "x", Name = "Ping" }, Token);
        return workspace;
    }

    private static TreeNodeViewModel Node(WorkspaceViewModel vm, string identity) =>
        vm.Collections.SelectMany(Flatten).Single(n => n.Identity == identity);

    private static IEnumerable<TreeNodeViewModel> Flatten(TreeNodeViewModel node) => node.Children.SelectMany(Flatten).Prepend(node);

    private static string[] Keys(WorkspaceViewModel vm, string collection, params string[] folder) =>
        [.. CollectionTree.ChildrenOf(vm.Session.Workspace.FindCollection(collection)!, folder).Select(c => c.Key)];

    [Fact]
    public async Task PositionAt_SplitsRowsByKind()
    {
        using var workspace = await CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        var request = Node(vm, "examples/get-example");
        var other = Node(vm, "examples/post-json");
        var folder = Node(vm, "examples/admin");
        var collection = Node(vm, "examples/");
        var second = Node(vm, "second/");

        TreeDrop.PositionAt(request, other, 0.2).ShouldBe(DropPosition.Before);
        TreeDrop.PositionAt(request, other, 0.8).ShouldBe(DropPosition.After);
        TreeDrop.PositionAt(request, folder, 0.1).ShouldBe(DropPosition.Before);
        TreeDrop.PositionAt(request, folder, 0.5).ShouldBe(DropPosition.Inside);
        TreeDrop.PositionAt(request, folder, 0.9).ShouldBe(DropPosition.After);
        TreeDrop.PositionAt(request, second, 0.1).ShouldBe(DropPosition.Inside);

        // Under an open folder the line would sit above its first child, so the lower part means "inside".
        folder.IsExpanded = true;
        TreeDrop.PositionAt(request, folder, 0.9).ShouldBe(DropPosition.Inside);

        // Collections only reorder among collections.
        TreeDrop.PositionAt(collection, second, 0.3).ShouldBe(DropPosition.Before);
        TreeDrop.PositionAt(collection, second, 0.7).ShouldBe(DropPosition.After);
        TreeDrop.PositionAt(collection, request, 0.5).ShouldBe(DropPosition.None);

        // Not onto itself or into its own folders.
        TreeDrop.PositionAt(request, request, 0.5).ShouldBe(DropPosition.None);
        TreeDrop.PositionAt(folder, Node(vm, "examples/admin/list-users"), 0.2).ShouldBe(DropPosition.None);
    }

    [Fact]
    public async Task Drop_ReordersWithinAFolder_KeepingTheFile()
    {
        using var workspace = await CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        await vm.DropAsync(Node(vm, "examples/get-example"), Node(vm, "examples/post-json"), DropPosition.After);

        Keys(vm, "examples").ShouldBe(["post-json", "get-example", "admin"]);
        workspace.Read("collections/examples/collection.yaml").ShouldContain("- post-json\n- get-example");
        File.Exists(Path.Combine(workspace.Root, "collections", "examples", "get-example.http.yaml")).ShouldBeTrue();
        Node(vm, "examples/get-example").IsSelected.ShouldBeTrue();

        await vm.DropAsync(Node(vm, "examples/admin"), Node(vm, "examples/post-json"), DropPosition.Before);
        Keys(vm, "examples").ShouldBe(["admin", "post-json", "get-example"]);
    }

    [Fact]
    public async Task Drop_IntoAndOutOfFolders_MovesFilesAndKeepsExpansion()
    {
        using var workspace = await CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        await vm.DropAsync(Node(vm, "examples/get-example"), Node(vm, "examples/admin/list-users"), DropPosition.After);

        Keys(vm, "examples", "admin").ShouldBe(["list-users", "get-example", "delete-user"]);
        File.Exists(Path.Combine(workspace.Root, "collections", "examples", "admin", "get-example.http.yaml")).ShouldBeTrue();
        File.Exists(Path.Combine(workspace.Root, "collections", "examples", "get-example.http.yaml")).ShouldBeFalse();
        Node(vm, "examples/admin").IsExpanded.ShouldBeTrue();

        // A folder moved into another folder stays open.
        await vm.NewFolderCommand.ExecuteAsync(Node(vm, "examples/"));
        var target = vm.Collections[0].Children.Single(c => c.IsFolder && c.Name != "Admin");
        await vm.DropAsync(Node(vm, "examples/admin"), target, DropPosition.Inside);
        Node(vm, $"{target.Identity}/admin").IsExpanded.ShouldBeTrue();
        Keys(vm, "examples", target.Path[0], "admin").ShouldBe(["list-users", "get-example", "delete-user"]);
    }

    [Fact]
    public async Task Drop_IntoAnotherCollection_MovesTheFolderWithItsRequests()
    {
        using var workspace = await CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        await vm.DropAsync(Node(vm, "examples/admin"), Node(vm, "second/ping"), DropPosition.Before);

        Keys(vm, "examples").ShouldBe(["get-example", "post-json"]);
        Keys(vm, "second").ShouldBe(["admin", "ping"]);
        Keys(vm, "second", "admin").ShouldBe(["list-users", "delete-user"]);
        Directory.Exists(Path.Combine(workspace.Root, "collections", "examples", "admin")).ShouldBeFalse();
        File.Exists(Path.Combine(workspace.Root, "collections", "second", "admin", "delete-user.http.yaml")).ShouldBeTrue();

        var reopened = await Services.WorkspaceSession.OpenAsync(workspace.Root, Token);
        reopened.Workspace.FindCollection("second")!.Children.Select(c => c.Key).ShouldBe(["admin", "ping"]);
    }

    [Fact]
    public async Task Drop_RenamesTheKey_WhenTheTargetFolderHasOneLikeIt()
    {
        using var workspace = await CreateAsync();
        await workspace.Session.AddNodeAsync("second", [], new HttpRequestDefinition { Key = "x", Name = "Get example" }, Token);
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        await vm.DropAsync(Node(vm, "examples/get-example"), Node(vm, "second/"), DropPosition.Inside);

        Keys(vm, "second").ShouldBe(["ping", "get-example", "get-example-2"]);
    }

    [Fact]
    public async Task Drop_OpenTabsFollowTheMovedRequests_UnsavedEditsToo()
    {
        using var workspace = await CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        vm.OpenNode(Node(vm, "examples/admin/list-users"));
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
        tab.Url = "https://example.test/users";

        await vm.DropAsync(Node(vm, "examples/admin"), Node(vm, "second/"), DropPosition.Inside);

        tab.Identity.ShouldBe("second/admin/list-users");
        tab.CollectionKey.ShouldBe("second");
        tab.Breadcrumb.ShouldBe("Second  /  Admin  /  List users");
        tab.IsDirty.ShouldBeTrue();
        await tab.SaveAsync();
        workspace.Read("collections/second/admin/list-users.http.yaml").ShouldContain("https://example.test/users");
        File.Exists(Path.Combine(workspace.Root, "collections", "examples", "admin", "list-users.http.yaml")).ShouldBeFalse();
    }

    [Fact]
    public async Task Drop_ReordersCollections()
    {
        using var workspace = await CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        await vm.DropAsync(Node(vm, "second/"), Node(vm, "examples/"), DropPosition.Before);

        vm.Collections.Select(c => c.CollectionKey).ShouldBe(["second", "examples"]);
        var reopened = await Services.WorkspaceSession.OpenAsync(workspace.Root, Token);
        reopened.Workspace.Collections.Select(c => c.Key).ShouldBe(["second", "examples"]);
    }

    [Fact]
    public async Task Drop_InPlace_WritesNothing()
    {
        using var workspace = await CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        var changes = 0;
        workspace.Session.Changed += (_, _) => changes++;

        await vm.DropAsync(Node(vm, "examples/get-example"), Node(vm, "examples/post-json"), DropPosition.Before);
        await vm.DropAsync(Node(vm, "examples/"), Node(vm, "second/"), DropPosition.Before);

        changes.ShouldBe(0);
    }

    // Drags "Create user" over rows of the real tree: the hint follows the pointer, and dropping moves the request.
    [Fact]
    public Task DraggingOverTheTree_ShowsWhereTheNodeLands() =>
        HeadlessApp.Session.Dispatch(DragWalkAsync, TestContext.Current.CancellationToken);

    private static async Task<bool> DragWalkAsync()
    {
        HeadlessApp.Log.Clear();
        var app = (App)Avalonia.Application.Current!;
        using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
        var settingsFile = Path.Combine(workspace.Root, ".jetapi", "settings.json");
        using var vm = new MainWindowViewModel(new Services.AppSettingsStore(settingsFile), new FakeDialogs(), app.ApplyTheme, action => Dispatcher.UIThread.Post(action), app.ApplyAppearance);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
        var ws = vm.Workspace!;
        Settle();
        var tree = window.GetVisualDescendants().OfType<TreeView>().Single(t => t.Name == "Tree");
        var source = ws.Collections[0].Children.Single(c => c.Name == "Create user");
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DataFormat.CreateStringApplicationFormat("jetapi-tree-node"), source.Identity));

        // Raises a drag event at a fraction of a row's height, as the pointer would.
        void DragOver(TreeNodeViewModel target, double fraction, RoutedEvent<DragEventArgs>? routedEvent = null)
        {
            var row = tree.GetVisualDescendants().OfType<TreeViewItem>().Single(i => i.DataContext == target);
            var header = row.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot");
            var args = new DragEventArgs(routedEvent ?? DragDrop.DragOverEvent, transfer, header, new Point(40, header.Bounds.Height * fraction), KeyModifiers.None);
            header.RaiseEvent(args);
            Settle();
        }

        var list = ws.Collections[0].Children.Single(c => c.Name == "List users");
        var admin = ws.Collections[0].Children.Single(c => c.Name == "Admin");
        DragOver(list, 0.2);
        list.DropHint.ShouldBe(DropPosition.Before);
        ScreenTests.Capture(window, "ux-43-drag-before");
        DragOver(admin, 0.5);
        list.DropHint.ShouldBe(DropPosition.None);
        admin.DropHint.ShouldBe(DropPosition.Inside);
        ScreenTests.Capture(window, "ux-44-drag-inside");

        ((App)Avalonia.Application.Current!).ApplyTheme(Services.AppTheme.Dark);
        DragOver(list, 0.8);
        list.DropHint.ShouldBe(DropPosition.After);
        ScreenTests.Capture(window, "ux-45-drag-after-dark");
        ((App)Avalonia.Application.Current!).ApplyTheme(Services.AppTheme.Light);

        // Dropping inside the folder moves the request there and selects it.
        DragOver(admin, 0.5, DragDrop.DropEvent);
        admin.DropHint.ShouldBe(DropPosition.None);
        for (var i = 0; i < 100 && tree.SelectedItem is null; i++)
        {
            await Task.Delay(20);
            Settle();
        }

        CollectionTree.ChildrenOf(ws.Session.Workspace.Collections[0], ["admin"]).Select(c => c.Name).ShouldBe(["Delete user", "Create user"]);
        tree.SelectedItem.ShouldBeOfType<TreeNodeViewModel>().Identity.ShouldEndWith("/admin/create-user");
        ScreenTests.Capture(window, "ux-46-dropped-inside");

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

    [Fact]
    public async Task MoveUpAndDown_StepAmongSiblings()
    {
        using var workspace = await CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        await vm.MoveDownCommand.ExecuteAsync(Node(vm, "examples/get-example"));
        Keys(vm, "examples").ShouldBe(["post-json", "get-example", "admin"]);
        await vm.MoveDownCommand.ExecuteAsync(Node(vm, "examples/get-example"));
        Keys(vm, "examples").ShouldBe(["post-json", "admin", "get-example"]);
        TreeDrop.Step(vm.Session.Workspace, Node(vm, "examples/get-example"), down: true).ShouldBeNull();

        await vm.MoveUpCommand.ExecuteAsync(Node(vm, "examples/admin/delete-user"));
        Keys(vm, "examples", "admin").ShouldBe(["delete-user", "list-users"]);

        await vm.MoveUpCommand.ExecuteAsync(Node(vm, "second/"));
        vm.Collections.Select(c => c.CollectionKey).ShouldBe(["second", "examples"]);
        TreeDrop.Step(vm.Session.Workspace, Node(vm, "second/"), down: false).ShouldBeNull();
        await vm.MoveDownCommand.ExecuteAsync(Node(vm, "second/"));
        vm.Collections.Select(c => c.CollectionKey).ShouldBe(["examples", "second"]);
    }
}
