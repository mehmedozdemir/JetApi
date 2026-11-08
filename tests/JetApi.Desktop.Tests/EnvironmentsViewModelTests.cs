using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

public sealed class EnvironmentsViewModelTests
{
    [Fact]
    public async Task EditingVariables_ResolvesAndSaves()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var closed = false;
        var vm = new EnvironmentsViewModel(workspace.Session, new FakeDialogs(), () => closed = true);

        vm.SelectedScope!.Name.ShouldBe("Development");
        vm.Rows[^1].Name = "host";
        vm.Rows[^2].Value = "api.test";
        vm.Rows[^1].Name = "url";
        vm.Rows[^2].Value = "https://{{host}}/v1";
        vm.Rows[^1].Name = "token";
        vm.Rows[^2].Value = "abc";
        vm.Rows[^2].IsSecret = true;

        vm.IsDirty.ShouldBeTrue();
        vm.Rows.Single(r => r.Name == "url").Resolved.ShouldBe("https://api.test/v1");
        vm.Rows.Single(r => r.Name == "token").Resolved.ShouldBe("••••••••");
        vm.RevealSecrets = true;
        vm.Rows.Single(r => r.Name == "token").Resolved.ShouldBe("abc");

        await vm.CloseCommand.ExecuteAsync(null);

        closed.ShouldBeTrue();
        var file = workspace.Read("environments/dev.yaml");
        file.ShouldContain("value: https://{{host}}/v1");
        file.ShouldContain("secret: true");
    }

    [Fact]
    public async Task ItemCommands_ActOnTheirOwnEnvironment()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var dialogs = new FakeDialogs();
        var vm = new EnvironmentsViewModel(workspace.Session, dialogs, () => { });
        dialogs.Prompts.Enqueue("Staging");
        await vm.AddEnvironmentCommand.ExecuteAsync(null);
        vm.SelectCommand.Execute(vm.Environments.Single(e => e.Name == "Development"));
        var staging = vm.Environments.Single(e => e.Name == "Staging");

        vm.Environments.Single(e => e.Name == "Development").IsSelected.ShouldBeTrue();
        staging.IsSelected.ShouldBeFalse();
        staging.CanMakeActive.ShouldBeTrue();

        dialogs.Prompts.Enqueue("Pre-prod");
        await staging.RenameCommand!.ExecuteAsync(null);
        vm.Environments.Select(e => e.Name).ShouldContain("Pre-prod");
        vm.SelectedScope!.Name.ShouldBe("Development");
        workspace.Read("environments/staging.yaml").ShouldContain("name: Pre-prod");

        await vm.Environments.Single(e => e.Name == "Pre-prod").DuplicateCommand!.ExecuteAsync(null);
        vm.Environments.Select(e => e.Name).ShouldContain("Pre-prod (copy)");

        vm.Environments.Single(e => e.Name == "Pre-prod").MakeActiveCommand!.Execute(null);
        workspace.Session.ActiveEnvironmentKey.ShouldBe("staging");

        await vm.Environments.Single(e => e.Name == "Pre-prod (copy)").DeleteCommand!.ExecuteAsync(null);
        vm.Environments.Select(e => e.Name).ShouldNotContain("Pre-prod (copy)");
        vm.SelectedScope.ShouldNotBeNull();
        vm.SelectedScope.IsSelected.ShouldBeTrue();
        vm.OtherScopes.ShouldAllBe(s => s.RenameCommand == null);
    }

    [Fact]
    public async Task EnvironmentsCanBeAddedDuplicatedActivatedAndDeleted()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var dialogs = new FakeDialogs();
        var vm = new EnvironmentsViewModel(workspace.Session, dialogs, () => { });

        dialogs.Prompts.Enqueue("Production");
        await vm.AddEnvironmentCommand.ExecuteAsync(null);
        vm.SelectedScope!.Key.ShouldBe("production");
        vm.ShowProductionWarning.ShouldBeTrue();
        vm.MakeActiveCommand.Execute(null);
        workspace.Session.ActiveEnvironmentKey.ShouldBe("production");

        vm.SelectCommand.Execute(vm.Environments[0]);
        await vm.DuplicateCommand.ExecuteAsync(null);
        vm.SelectedScope!.Name.ShouldBe("Development (copy)");
        vm.Rows.ShouldContain(r => r.Name == "baseUrl");

        await vm.DeleteEnvironmentCommand.ExecuteAsync(null);
        vm.Environments.Select(e => e.Name).ShouldBe(["Development", "Production"]);
    }

    [Fact]
    public async Task GlobalAndCollectionScopes_AreEditable()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var vm = new EnvironmentsViewModel(workspace.Session, new FakeDialogs(), () => { });

        vm.SelectCommand.Execute(vm.OtherScopes[0]);
        vm.Rows[^1].Name = "region";
        vm.Rows[^2].Value = "eu";
        await vm.SaveAsync();
        workspace.Read("jetapi.yaml").ShouldContain("name: region");

        // Collection variables are edited on the collection screen, not in this list.
        vm.OtherScopes.ShouldHaveSingleItem().Kind.ShouldBe(ScopeKind.Global);

        var collection = new EnvironmentsViewModel(workspace.Session, new FakeDialogs(), () => { }, "examples");
        collection.Environments.ShouldBeEmpty();
        collection.SelectedScope!.Kind.ShouldBe(ScopeKind.Collection);
        collection.FileName.ShouldBe("collections/examples/collection.yaml");
        collection.Rows[^1].Name = "version";
        collection.Rows[^1].Name = "baseUrl";
        collection.Rows.Single(r => r.Name == "baseUrl").Note.ShouldContain("Development");
        collection.Rows.Single(r => r.Name == "version").Note.ShouldBeEmpty();
        await collection.SaveAsync();
        workspace.Read("collections/examples/collection.yaml").ShouldContain("name: version");
    }
}
