using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

public sealed class ImportTests
{
    internal static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "Samples", name);

    [Fact]
    public async Task ImportFromPostman_AddsCollectionsAndEnvironments_AndReportsEveryFile()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var dialogs = new FakeDialogs();
        using var vm = workspace.CreateViewModel(dialogs);
        var broken = Path.Combine(workspace.Root, "broken.json");
        await File.WriteAllTextAsync(broken, "{ \"hello\": 1 }", TestContext.Current.CancellationToken);
        dialogs.FilesToPick.AddRange([Sample("shop.postman_collection.json"), Sample("dev.postman_environment.json"), broken]);

        await vm.ImportFromPostmanCommand.ExecuteAsync(null);

        var shop = vm.Collections.Single(c => c.Name == "Shop API");
        shop.IsExpanded.ShouldBeTrue();
        shop.Children.Select(c => c.Name).ShouldBe(["Orders", "Customers"]);
        vm.Session.Workspace.Environments.Select(e => e.Name).ShouldContain("Development");
        workspace.Read("collections/shop-api/collection.yaml").ShouldContain("name: Shop API");

        var report = dialogs.ImportReports.ShouldHaveSingleItem();
        report.Files.Count.ShouldBe(3);
        report.Files[0].Succeeded.ShouldBeTrue();
        report.Files[0].Summary.ShouldContain("10 requests");
        report.Files[0].Issues.ShouldNotBeEmpty();
        report.Files[1].Succeeded.ShouldBeTrue();
        report.Files[2].Succeeded.ShouldBeFalse();
        report.CollectionKeys.ShouldBe(["shop-api"]);
    }

    [Fact]
    public async Task OpenApiDocuments_AreImportedLikePostmanExports()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var dialogs = new FakeDialogs();
        using var vm = workspace.CreateViewModel(dialogs);

        var report = await vm.ImportFilesAsync([Sample("petstore.openapi.yaml"), Sample("users.swagger.json")]);

        report.Files.ShouldAllBe(f => f.Succeeded);
        report.CollectionKeys.Count.ShouldBe(2);
        vm.Collections.Count(c => report.CollectionKeys.Contains(c.CollectionKey) && c.IsExpanded).ShouldBe(2);
    }

    [Fact]
    public async Task ImportingTheSameCollectionTwice_KeepsBoth()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var dialogs = new FakeDialogs();
        using var vm = workspace.CreateViewModel(dialogs);

        await vm.ImportFilesAsync([Sample("shop.postman_collection.json")]);
        await vm.ImportFilesAsync([Sample("shop.postman_collection.json")]);

        vm.Session.Workspace.Collections.Select(c => c.Key).ShouldContain("shop-api");
        vm.Session.Workspace.Collections.Select(c => c.Key).ShouldContain("shop-api-2");
    }
}
