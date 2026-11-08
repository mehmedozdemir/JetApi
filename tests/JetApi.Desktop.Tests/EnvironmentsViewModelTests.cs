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

        vm.SelectCommand.Execute(vm.OtherScopes[1]);
        vm.FileName.ShouldBe("collections/examples/collection.yaml");
        vm.Rows[^1].Name = "version";
        await vm.SaveAsync();
        workspace.Read("collections/examples/collection.yaml").ShouldContain("name: version");
    }
}
