using JetApi.Core.Model;

namespace JetApi.Storage.Tests;

/// <summary>The <c>scripts</c> section: Postman scripts kept from an import (ADR-0010).</summary>
public sealed class ScriptsStorageTests
{
    private const string TestScript = "pm.test('custom', function () {\n    pm.expect(pm.response.json().items.length).to.be.above(0);\n});";

    [Fact]
    public void RequestScripts_AreWrittenAsBlocks_AndReadBack()
    {
        var request = new HttpRequestDefinition { Key = "r", Name = "R", Url = "https://x", Scripts = new ScriptSet("pm.variables.set('t', Date.now());", TestScript) };

        var yaml = RequestFiles.Serialize(request);
        var read = RequestFiles.Deserialize(yaml, "r", isGrpc: false);

        read.Scripts.ShouldBe(request.Scripts);
        yaml.ShouldContain("scripts:\n  preRequest: ");
        yaml.ShouldContain("  test: |-\n    pm.test('custom', function () {\n        pm.expect(");
    }

    [Fact]
    public void NoScripts_WritesNothing()
    {
        RequestFiles.Serialize(new HttpRequestDefinition { Key = "r", Name = "R" }).ShouldNotContain("scripts");
        RequestFiles.Serialize(new GrpcRequestDefinition { Key = "g", Name = "G", Scripts = new ScriptSet("  \n") }).ShouldNotContain("scripts");
    }

    [Fact]
    public void GrpcRequestScripts_RoundTrip()
    {
        var request = new GrpcRequestDefinition { Key = "g", Name = "G", Scripts = new ScriptSet(Test: TestScript) };

        RequestFiles.Deserialize(RequestFiles.Serialize(request), "g", isGrpc: true).Scripts.ShouldBe(request.Scripts);
    }

    [Fact]
    public async Task CollectionAndFolderScripts_RoundTrip()
    {
        var root = Directory.CreateTempSubdirectory("jetapi-scripts-").FullName;
        try
        {
            var collection = new ApiCollection
            {
                Key = "users",
                Name = "Users",
                Scripts = new ScriptSet("console.log('before');", TestScript),
                Children = [new Folder { Key = "admin", Name = "Admin", Scripts = new ScriptSet(Test: "pm.test('a', () => {});") }],
            };
            await WorkspaceStore.SaveAsync(root, new Workspace { Name = "W", Collections = [collection] }, TestContext.Current.CancellationToken);

            var loaded = await WorkspaceStore.LoadAsync(root, TestContext.Current.CancellationToken);

            loaded.Diagnostics.ShouldBeEmpty();
            var read = loaded.Workspace.Collections.ShouldHaveSingleItem();
            read.Scripts.ShouldBe(collection.Scripts);
            read.Children.ShouldHaveSingleItem().Scripts.ShouldBe(new ScriptSet(Test: "pm.test('a', () => {});"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
