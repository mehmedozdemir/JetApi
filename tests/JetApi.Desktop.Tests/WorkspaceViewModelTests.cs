using JetApi.Core.Model;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

public sealed class WorkspaceViewModelTests
{
    [Fact]
    public async Task Tree_ShowsCollectionsFoldersAndRequests()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        var collection = vm.Collections.ShouldHaveSingleItem();
        collection.Name.ShouldBe("Examples");
        collection.IsExpanded.ShouldBeTrue();
        collection.Children.Select(c => (c.Badge, c.Name)).ShouldBe([("GET", "Get example"), ("POST", "Post JSON")]);
        vm.EnvironmentOptions.Select(e => e.Name).ShouldBe(["No environment", "Development"]);
        vm.SelectedEnvironment!.Key.ShouldBe("dev");
    }

    [Fact]
    public async Task OpenNode_OpensATab_OnlyOnce()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        var node = vm.Collections[0].Children[0];

        vm.OpenNode(node);
        vm.OpenNode(node);

        var tab = vm.Tabs.ShouldHaveSingleItem().ShouldBeOfType<HttpRequestTabViewModel>();
        vm.SelectedTab.ShouldBe(tab);
        tab.IsDirty.ShouldBeFalse();
        tab.Url.ShouldBe("{{baseUrl}}/get");
        tab.Breadcrumb.ShouldBe("Examples  /  Get example");
    }

    [Fact]
    public async Task EditingAndSaving_UpdatesTheFile_AndDirtyState()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        vm.OpenNode(vm.Collections[0].Children[0]);
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;

        tab.Url = "{{baseUrl}}/anything";
        tab.Headers.Rows[^1].Key = "X-Test";
        tab.Headers.Rows[0].Value = "1";

        tab.IsDirty.ShouldBeTrue();
        vm.UnsavedText.ShouldBe("Unsaved: 1");
        tab.Headers.Rows.Count.ShouldBe(2);
        tab.Headers.Badge.ShouldBe("1");

        await tab.SaveAsync();

        tab.IsDirty.ShouldBeFalse();
        var file = workspace.Read("collections/examples/get-example.http.yaml");
        file.ShouldContain("url: '{{baseUrl}}/anything'");
        file.ShouldContain("name: X-Test");

        tab.Url = "{{baseUrl}}/get";
        tab.Url = "{{baseUrl}}/anything";
        tab.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task NewRequest_IsCreatedInTheChosenFolder_AndOpened()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var dialogs = new FakeDialogs();
        using var vm = workspace.CreateViewModel(dialogs);

        dialogs.Prompts.Enqueue("Admin");
        await vm.NewFolderCommand.ExecuteAsync(vm.Collections[0]);
        var folder = vm.Collections[0].Children.Single(c => c.IsFolder);
        dialogs.Prompts.Enqueue("Kullanıcı Sil");
        await vm.NewHttpRequestCommand.ExecuteAsync(folder);

        var tab = vm.SelectedTab.ShouldNotBeNull();
        tab.Path.ShouldBe(["admin", "kullanici-sil"]);
        tab.IsDirty.ShouldBeFalse();
        File.Exists(Path.Combine(workspace.Root, "collections", "examples", "admin", "kullanici-sil.http.yaml")).ShouldBeTrue();
        vm.Collections[0].Children.Single(c => c.IsFolder).Children.ShouldHaveSingleItem().Name.ShouldBe("Kullanıcı Sil");
    }

    [Fact]
    public async Task Rename_KeepsTheFile_AndUpdatesTheOpenTab()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var dialogs = new FakeDialogs();
        using var vm = workspace.CreateViewModel(dialogs);
        vm.OpenNode(vm.Collections[0].Children[0]);

        dialogs.Prompts.Enqueue("Renamed");
        await vm.RenameCommand.ExecuteAsync(vm.Collections[0].Children[0]);

        vm.SelectedTab!.Name.ShouldBe("Renamed");
        vm.SelectedTab.IsDirty.ShouldBeFalse();
        workspace.Read("collections/examples/get-example.http.yaml").ShouldContain("name: Renamed");
    }

    [Fact]
    public async Task Delete_RemovesFile_AndClosesTab()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        vm.OpenNode(vm.Collections[0].Children[0]);

        await vm.DeleteCommand.ExecuteAsync(vm.Collections[0].Children[0]);

        vm.Tabs.ShouldBeEmpty();
        vm.Collections[0].Children.ShouldHaveSingleItem();
        File.Exists(Path.Combine(workspace.Root, "collections", "examples", "get-example.http.yaml")).ShouldBeFalse();
    }

    [Fact]
    public async Task Duplicate_PlacesTheCopyAfterTheOriginal()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        await vm.DuplicateCommand.ExecuteAsync(vm.Collections[0].Children[0]);

        vm.Collections[0].Children.Select(c => c.Name).ShouldBe(["Get example", "Get example (copy)", "Post JSON"]);
        vm.SelectedTab!.Name.ShouldBe("Get example (copy)");
    }

    [Fact]
    public async Task CloseTab_AsksBeforeDiscardingChanges()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var dialogs = new FakeDialogs { ConfirmResult = false };
        using var vm = workspace.CreateViewModel(dialogs);
        vm.OpenNode(vm.Collections[0].Children[0]);
        ((HttpRequestTabViewModel)vm.SelectedTab!).Url = "changed";

        await vm.CloseTabCommand.ExecuteAsync(vm.SelectedTab);
        vm.Tabs.Count.ShouldBe(1);

        dialogs.ConfirmResult = true;
        await vm.CloseTabCommand.ExecuteAsync(vm.SelectedTab);
        vm.Tabs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Filter_HidesNonMatchingRequests()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        vm.FilterText = "post";

        vm.Collections[0].IsVisible.ShouldBeTrue();
        vm.Collections[0].Children.Select(c => c.IsVisible).ShouldBe([false, true]);
        vm.FilterText = string.Empty;
        vm.Collections[0].Children.ShouldAllBe(c => c.IsVisible);
    }

    [Fact]
    public async Task SwitchingEnvironment_UpdatesVariableHints()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        vm.OpenNode(vm.Collections[0].Children[0]);
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;

        tab.UrlHints.ShouldHaveSingleItem().ShouldBe(new VariableHint("{{baseUrl}}", "https://httpbin.org", "Development", true) { Source = JetApi.Core.Variables.VariableSource.Environment });

        vm.SelectedEnvironment = vm.EnvironmentOptions[0];

        workspace.Session.ActiveEnvironmentKey.ShouldBeNull();
        tab.UrlHints.ShouldHaveSingleItem().IsDefined.ShouldBeFalse();
    }

    [Fact]
    public async Task Palette_FindsRequestsAndRunsCommands()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        vm.OpenPalette();
        var palette = vm.Palette.ShouldNotBeNull();
        palette.Query = "post";
        palette.Results.First().Label.ShouldBe("Post JSON");
        await palette.ExecuteSelectedCommand.ExecuteAsync(null);

        vm.Palette.ShouldBeNull();
        vm.SelectedTab!.Name.ShouldBe("Post JSON");

        vm.OpenPalette();
        vm.Palette!.Query = ">environments";
        vm.Palette.Results.ShouldHaveSingleItem().Label.ShouldBe("Edit environments");
        await vm.Palette.ExecuteSelectedCommand.ExecuteAsync(null);
        vm.Environments.ShouldNotBeNull();
        vm.SidebarMode.ShouldBe(SidebarMode.Environments);
    }

    [Fact]
    public async Task ClickingACollection_OpensItsScreen_AndLeavingSavesEdits()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        var collection = vm.Collections[0];

        vm.OpenNode(collection);

        var screen = vm.NodeSettings.ShouldNotBeNull();
        collection.IsExpanded.ShouldBeTrue();
        screen.IsCollection.ShouldBeTrue();
        screen.Section.ShouldBe(NodeSettingsSection.Variables);
        screen.HasChanges.ShouldBeFalse();

        var variables = screen.Variables.ShouldNotBeNull();
        variables.Rows[^1].Name = "apiVersion";
        variables.Rows[^2].Value = "v2";
        screen.VariablesBadge.ShouldBe("1");
        screen.HasChanges.ShouldBeTrue();

        vm.OpenNode(collection.Children.First(c => c.IsRequest));

        vm.NodeSettings.ShouldBeNull();
        vm.SelectedTab.ShouldNotBeNull();
        await (screen.SaveCommand.ExecutionTask ?? Task.CompletedTask);
        workspace.Read($"collections/{collection.CollectionKey}/collection.yaml").ShouldContain("name: apiVersion");
    }

    [Fact]
    public async Task ACollectionScreen_IsNotClosedByTheOneItReplaced()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        await vm.NewCollectionCommand.ExecuteAsync(null);
        var first = vm.Collections[0];
        var second = vm.Collections[1];

        vm.OpenNode(first);
        var old = vm.NodeSettings!;
        old.Name = "Renamed";
        vm.OpenNode(second);
        var current = vm.NodeSettings.ShouldNotBeNull();
        current.CollectionKey.ShouldBe(second.CollectionKey);

        await (old.SaveCommand.ExecutionTask ?? Task.CompletedTask);
        vm.NodeSettings.ShouldBeSameAs(current);
    }

    [Fact]
    public async Task SidebarModes_OpenAndCloseTheEnvironmentEditor()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        vm.IsCollectionsMode.ShouldBeTrue();
        vm.SidebarMode = SidebarMode.Environments;
        var editor = vm.Environments.ShouldNotBeNull();
        vm.IsEnvironmentsMode.ShouldBeTrue();

        editor.Rows[^1].Name = "host";
        editor.Rows[^2].Value = "api.test";
        vm.SidebarMode = SidebarMode.History;

        vm.IsHistoryVisible.ShouldBeTrue();
        vm.Environments.ShouldBeNull();
        await (editor.SaveCommand.ExecutionTask ?? Task.CompletedTask);
        workspace.Read("environments/dev.yaml").ShouldContain("api.test");

        vm.ShowEnvironments();
        vm.IsEnvironmentsMode.ShouldBeTrue();
        vm.OpenCollectionSettings(vm.Collections[0].CollectionKey);
        vm.IsCollectionsMode.ShouldBeTrue();
        vm.Environments.ShouldBeNull();
        vm.NodeSettings.ShouldNotBeNull();
    }

    [Fact]
    public async Task Reload_PicksUpExternalChanges_InCleanTabs()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        vm.OpenNode(vm.Collections[0].Children[0]);
        var file = Path.Combine(workspace.Root, "collections", "examples", "get-example.http.yaml");

        File.WriteAllText(file, File.ReadAllText(file).Replace("/get", "/changed-by-git", StringComparison.Ordinal));
        await vm.ReloadAsync();

        ((HttpRequestTabViewModel)vm.SelectedTab!).Url.ShouldBe("{{baseUrl}}/changed-by-git");
    }

    [Fact]
    public async Task CollectionSettings_SaveNameAuthAndProto()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = workspace.CreateViewModel(new FakeDialogs());

        vm.OpenNodeSettingsCommand.Execute(vm.Collections[0]);
        var settings = vm.NodeSettings.ShouldNotBeNull();
        settings.Name = "Examples API";
        settings.Auth.SelectedKind = settings.Auth.Kinds.Single(k => k.Value == AuthKind.Bearer);
        settings.Auth.Token = "{{token}}";
        settings.ImportPathsText = "protos";
        settings.ProtoFilesText = "a/b.proto\n\n c.proto ";
        await settings.SaveCommand.ExecuteAsync(null);

        vm.NodeSettings.ShouldBeNull();
        var collection = workspace.Session.Workspace.Collections[0];
        (collection.Name, collection.Auth).ShouldBe(("Examples API", (AuthConfig)new BearerAuth("{{token}}")));
        collection.Proto.Files.ShouldBe(["a/b.proto", "c.proto"]);
        vm.Collections[0].Name.ShouldBe("Examples API");
    }
}
