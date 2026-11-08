using JetApi.Core.Model;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.Tests;

public sealed class WorkspaceSessionTests
{
    [Fact]
    public async Task Create_WritesWorkspaceExampleAndGitIgnore()
    {
        using var workspace = await TempWorkspace.CreateAsync();

        workspace.Read("jetapi.yaml").ShouldContain("name: Test Workspace");
        workspace.Read(".gitignore").ShouldContain(".jetapi/");
        workspace.Session.Workspace.Collections.ShouldHaveSingleItem().Children.Count.ShouldBe(2);
        workspace.Session.ActiveEnvironmentKey.ShouldBe("dev");
        await Should.ThrowAsync<JetApi.Storage.WorkspaceStorageException>(() => WorkspaceSession.CreateAsync(workspace.Root, "again"));
    }

    [Fact]
    public async Task ActiveEnvironment_IsRememberedPerMachine()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var key = await workspace.Session.AddEnvironmentAsync("Test", cancellationToken: TestContext.Current.CancellationToken);

        workspace.Session.SetActiveEnvironment(key);
        var reopened = await WorkspaceSession.OpenAsync(workspace.Root, TestContext.Current.CancellationToken);

        reopened.ActiveEnvironmentKey.ShouldBe("test");
        File.Exists(Path.Combine(workspace.Root, ".jetapi", "state.json")).ShouldBeTrue();
        workspace.Read("jetapi.yaml").ShouldNotContain("active");
    }

    [Fact]
    public async Task NodeOperations_AreWrittenToDisk()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var session = workspace.Session;
        var changes = 0;
        session.Changed += (_, _) => changes++;

        var folder = await session.AddNodeAsync("examples", [], new Folder { Key = "x", Name = "Admin Area" }, TestContext.Current.CancellationToken);
        var request = await session.AddNodeAsync("examples", folder, new HttpRequestDefinition { Key = "x", Name = "Get Örnek" }, TestContext.Current.CancellationToken);
        request.ShouldBe(["admin-area", "get-ornek"]);
        File.Exists(Path.Combine(workspace.Root, "collections", "examples", "admin-area", "get-ornek.http.yaml")).ShouldBeTrue();

        var moved = await session.MoveNodeAsync("examples", request, [], cancellationToken: TestContext.Current.CancellationToken);
        moved.ShouldBe(["get-ornek"]);
        File.Exists(Path.Combine(workspace.Root, "collections", "examples", "admin-area", "get-ornek.http.yaml")).ShouldBeFalse();
        File.Exists(Path.Combine(workspace.Root, "collections", "examples", "get-ornek.http.yaml")).ShouldBeTrue();

        await session.DeleteNodeAsync("examples", folder, TestContext.Current.CancellationToken);
        Directory.Exists(Path.Combine(workspace.Root, "collections", "examples", "admin-area")).ShouldBeFalse();
        changes.ShouldBe(4);

        var reopened = await WorkspaceSession.OpenAsync(workspace.Root, TestContext.Current.CancellationToken);
        reopened.Workspace.Collections[0].Children.Select(c => c.Key).ShouldBe(["get-example", "post-json", "get-ornek"]);
    }

    [Fact]
    public async Task DeletingTheActiveEnvironment_LeavesNoneActive()
    {
        using var workspace = await TempWorkspace.CreateAsync();

        await workspace.Session.DeleteEnvironmentAsync("dev", TestContext.Current.CancellationToken);

        workspace.Session.ActiveEnvironmentKey.ShouldBeNull();
        File.Exists(Path.Combine(workspace.Root, "environments", "dev.yaml")).ShouldBeFalse();
        workspace.Read("jetapi.yaml").ShouldNotContain("dev");
    }

    [Fact]
    public async Task Collections_CanBeAddedAndDeleted()
    {
        using var workspace = await TempWorkspace.CreateAsync();

        var key = await workspace.Session.AddCollectionAsync("Sipariş Servisi", TestContext.Current.CancellationToken);
        key.ShouldBe("siparis-servisi");
        workspace.Read("jetapi.yaml").ShouldContain("siparis-servisi");

        await workspace.Session.DeleteCollectionAsync(key, TestContext.Current.CancellationToken);
        Directory.Exists(Path.Combine(workspace.Root, "collections", key)).ShouldBeFalse();
        workspace.Read("jetapi.yaml").ShouldNotContain("siparis-servisi");
    }
}
