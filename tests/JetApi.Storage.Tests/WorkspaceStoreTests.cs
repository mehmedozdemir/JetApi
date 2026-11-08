using JetApi.Core.Model;

namespace JetApi.Storage.Tests;

public sealed class WorkspaceStoreTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SaveThenLoad_RoundTripsTheWholeWorkspace()
    {
        var original = SampleWorkspace.Create();

        await WorkspaceStore.SaveAsync(_directory.Path, original, Token);
        var result = await WorkspaceStore.LoadAsync(_directory.Path, Token);

        result.Diagnostics.ShouldBeEmpty();
        AssertEquivalent(result.Workspace, original);
    }

    [Fact]
    public async Task Save_WritesOneFilePerItem()
    {
        await WorkspaceStore.SaveAsync(_directory.Path, SampleWorkspace.Create(), Token);

        var files = Directory.EnumerateFiles(_directory.Path, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_directory.Path, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

        files.ShouldBe(
        [
            "collections/user-service/admin/create-user.http.yaml",
            "collections/user-service/admin/folder.yaml",
            "collections/user-service/admin/upload-avatar.http.yaml",
            "collections/user-service/collection.yaml",
            "collections/user-service/get-user.grpc.yaml",
            "collections/user-service/list-users.http.yaml",
            "environments/dev.yaml",
            "environments/prod.yaml",
            "jetapi.yaml",
        ]);
    }

    [Fact]
    public async Task Save_WritesReadableYaml_WithDefaultsOmitted()
    {
        await WorkspaceStore.SaveAsync(_directory.Path, SampleWorkspace.Create(), Token);

        _directory.Read("collections/user-service/admin/create-user.http.yaml").ShouldBe(
            """
            schemaVersion: 1
            name: Create user
            method: POST
            url: '{{baseUrl}}/users'
            headers:
            - name: X-Trace
              value: '{{$guid}}'
            body:
              type: json
              content: |-
                {
                  "name": "Ayşe"
                }
            settings:
              followRedirects: false
              timeoutMs: 5000

            """);
    }

    [Fact]
    public async Task Save_DoesNotRewriteUnchangedFiles()
    {
        var workspace = SampleWorkspace.Create();
        await WorkspaceStore.SaveAsync(_directory.Path, workspace, Token);
        var path = _directory.Combine("collections", "user-service", "list-users.http.yaml");
        var past = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, past);

        await WorkspaceStore.SaveAsync(_directory.Path, workspace, Token);

        File.GetLastWriteTimeUtc(path).ShouldBe(past);
    }

    [Fact]
    public async Task Save_NormalizesLineEndingsInBodies()
    {
        var workspace = new Workspace
        {
            Name = "w",
            Collections =
            [
                new ApiCollection
                {
                    Key = "c",
                    Name = "c",
                    Children = [new HttpRequestDefinition { Key = "r", Name = "r", Body = HttpBody.Text("a\r\nb\rc") }],
                },
            ],
        };

        await WorkspaceStore.SaveAsync(_directory.Path, workspace, Token);
        var loaded = await WorkspaceStore.LoadAsync(_directory.Path, Token);

        _directory.Read("collections/c/r.http.yaml").ShouldNotContain("\r");
        ((HttpRequestDefinition)loaded.Workspace.Collections[0].Children[0]).Body.Content.ShouldBe("a\nb\nc");
    }

    [Fact]
    public async Task Load_KeepsOrderFromManifest_ThenSortsUnlistedItems()
    {
        _directory.Write("jetapi.yaml", "schemaVersion: 1\nname: w\n");
        _directory.Write("collections/c/collection.yaml", "schemaVersion: 1\nname: C\norder: [zeta, folder]\n");
        _directory.Write("collections/c/alpha.http.yaml", "schemaVersion: 1\nname: Alpha\n");
        _directory.Write("collections/c/zeta.http.yaml", "schemaVersion: 1\nname: Zeta\n");
        _directory.Write("collections/c/beta.grpc.yaml", "schemaVersion: 1\nname: Beta\n");
        _directory.Write("collections/c/folder/folder.yaml", "schemaVersion: 1\nname: Folder\n");
        _directory.Write("collections/c/README.md", "not a request");

        var result = await WorkspaceStore.LoadAsync(_directory.Path, Token);

        result.Diagnostics.ShouldBeEmpty();
        result.Workspace.Collections.Single().Children.Select(c => c.Key).ShouldBe(["zeta", "folder", "alpha", "beta"]);
    }

    [Fact]
    public async Task Load_AppliesDefaults_ForMinimalFiles()
    {
        _directory.Write("jetapi.yaml", "schemaVersion: 1\nname: w\n");
        _directory.Write("collections/c/collection.yaml", "schemaVersion: 1\nname: C\n");
        _directory.Write("collections/c/r.http.yaml", "schemaVersion: 1\nname: R\nmethod: post\n");

        var result = await WorkspaceStore.LoadAsync(_directory.Path, Token);

        var collection = result.Workspace.Collections.Single();
        collection.Auth.ShouldBe(AuthConfig.None);
        var request = (HttpRequestDefinition)collection.Children.Single();
        request.Method.ShouldBe("POST");
        request.Auth.ShouldBe(AuthConfig.Inherit);
        request.Body.ShouldBe(HttpBody.Empty);
        request.Settings.ShouldBe(HttpRequestSettings.Default);
    }

    [Theory]
    [InlineData("schemaVersion: 1\nname: R\nurll: typo\n", "urll")]
    [InlineData("schemaVersion: 1\nname: R\nbody: { type: soap }\n", "Unknown body.type 'soap'")]
    [InlineData("schemaVersion: 1\nname: R\nauth: { type: digest }\n", "Unknown auth.type 'digest'")]
    [InlineData("schemaVersion: 99\nname: R\n", "newer JetApi")]
    [InlineData("name: R\n", "'schemaVersion' is missing")]
    [InlineData("schemaVersion: 1\n", "'name' is required")]
    [InlineData("schemaVersion: 1\nname: R\nsettings: { timeoutMs: -1 }\n", "positive number")]
    [InlineData("schemaVersion: 1\nname: [unclosed\n", "Invalid YAML")]
    public async Task Load_ReportsInvalidRequestFile_AndKeepsLoadingOthers(string content, string expectedMessage)
    {
        _directory.Write("jetapi.yaml", "schemaVersion: 1\nname: w\n");
        _directory.Write("collections/c/collection.yaml", "schemaVersion: 1\nname: C\n");
        _directory.Write("collections/c/bad.http.yaml", content);
        _directory.Write("collections/c/good.http.yaml", "schemaVersion: 1\nname: Good\n");

        var result = await WorkspaceStore.LoadAsync(_directory.Path, Token);

        result.Workspace.Collections.Single().Children.Select(c => c.Key).ShouldBe(["good"]);
        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
        diagnostic.FilePath.ShouldBe("collections/c/bad.http.yaml");
        diagnostic.Message.ShouldContain(expectedMessage);
    }

    [Fact]
    public async Task Load_ReportsDuplicateKeys_AcrossRequestKinds()
    {
        _directory.Write("jetapi.yaml", "schemaVersion: 1\nname: w\n");
        _directory.Write("collections/c/collection.yaml", "schemaVersion: 1\nname: C\n");
        _directory.Write("collections/c/same.http.yaml", "schemaVersion: 1\nname: Http\n");
        _directory.Write("collections/c/same.grpc.yaml", "schemaVersion: 1\nname: Grpc\n");

        var result = await WorkspaceStore.LoadAsync(_directory.Path, Token);

        result.Workspace.Collections.Single().Children.ShouldHaveSingleItem();
        result.Diagnostics.ShouldHaveSingleItem().Message.ShouldContain("already uses the key 'same'");
    }

    [Fact]
    public async Task Load_Throws_WhenDirectoryIsNotAWorkspace()
    {
        var exception = await Should.ThrowAsync<WorkspaceStorageException>(() => WorkspaceStore.LoadAsync(_directory.Path, Token));

        exception.Message.ShouldContain("jetapi.yaml");
        WorkspaceStore.IsWorkspace(_directory.Path).ShouldBeFalse();
    }

    [Fact]
    public async Task Load_Throws_WhenManifestIsInvalid()
    {
        _directory.Write("jetapi.yaml", "schemaVersion: 1\n");

        var exception = await Should.ThrowAsync<WorkspaceStorageException>(() => WorkspaceStore.LoadAsync(_directory.Path, Token));

        exception.Message.ShouldContain("'name' is required");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("CON")]
    [InlineData("trailing.")]
    [InlineData("")]
    public async Task Save_RejectsUnsafeKeys(string key)
    {
        var workspace = new Workspace
        {
            Name = "w",
            Collections = [new ApiCollection { Key = "c", Name = "c", Children = [new HttpRequestDefinition { Key = key, Name = "r" }] }],
        };

        await Should.ThrowAsync<WorkspaceStorageException>(() => WorkspaceStore.SaveAsync(_directory.Path, workspace, Token));
    }

    [Fact]
    public async Task Save_RejectsKeysThatDifferOnlyInCase()
    {
        var workspace = new Workspace
        {
            Name = "w",
            Collections =
            [
                new ApiCollection
                {
                    Key = "c",
                    Name = "c",
                    Children = [new HttpRequestDefinition { Key = "get", Name = "a" }, new Folder { Key = "GET", Name = "b" }],
                },
            ],
        };

        var exception = await Should.ThrowAsync<WorkspaceStorageException>(() => WorkspaceStore.SaveAsync(_directory.Path, workspace, Token));
        exception.Message.ShouldContain("Duplicate key");
    }

    [Fact]
    public async Task DeleteOperations_RemoveOnlyTheTargetedItems()
    {
        await WorkspaceStore.SaveAsync(_directory.Path, SampleWorkspace.Create(), Token);

        await WorkspaceStore.DeleteNodeAsync(_directory.Path, "user-service", ["admin", "create-user"]);
        await WorkspaceStore.DeleteNodeAsync(_directory.Path, "user-service", ["get-user"]);
        await WorkspaceStore.DeleteEnvironmentAsync(_directory.Path, "prod");
        var afterRequestDeletes = await WorkspaceStore.LoadAsync(_directory.Path, Token);

        var collection = afterRequestDeletes.Workspace.Collections.Single();
        collection.Children.Select(c => c.Key).ShouldBe(["list-users", "admin"]);
        ((Folder)collection.Children[1]).Children.Select(c => c.Key).ShouldBe(["upload-avatar"]);
        afterRequestDeletes.Workspace.Environments.Select(e => e.Key).ShouldBe(["dev"]);

        await WorkspaceStore.DeleteNodeAsync(_directory.Path, "user-service", ["admin"]);
        await WorkspaceStore.DeleteCollectionAsync(_directory.Path, "user-service");
        (await WorkspaceStore.LoadAsync(_directory.Path, Token)).Workspace.Collections.ShouldBeEmpty();
    }

    private static void AssertEquivalent(Workspace actual, Workspace expected)
    {
        actual.Name.ShouldBe(expected.Name);
        actual.Description.ShouldBe(expected.Description);
        actual.GlobalVariables.ShouldBe(expected.GlobalVariables);
        actual.Environments.Count.ShouldBe(expected.Environments.Count);
        for (var i = 0; i < expected.Environments.Count; i++)
        {
            actual.Environments[i].Key.ShouldBe(expected.Environments[i].Key);
            actual.Environments[i].Name.ShouldBe(expected.Environments[i].Name);
            actual.Environments[i].Variables.ShouldBe(expected.Environments[i].Variables);
        }

        actual.Collections.Count.ShouldBe(expected.Collections.Count);
        for (var i = 0; i < expected.Collections.Count; i++)
        {
            var (a, e) = (actual.Collections[i], expected.Collections[i]);
            (a.Key, a.Name, a.Description, a.Auth).ShouldBe((e.Key, e.Name, e.Description, e.Auth));
            a.Variables.ShouldBe(e.Variables);
            a.Proto.ImportPaths.ShouldBe(e.Proto.ImportPaths);
            a.Proto.Files.ShouldBe(e.Proto.Files);
            AssertNodes(a.Children, e.Children);
        }
    }

    private static void AssertNodes(IReadOnlyList<CollectionNode> actual, IReadOnlyList<CollectionNode> expected)
    {
        actual.Select(n => n.Key).ShouldBe(expected.Select(n => n.Key));
        for (var i = 0; i < expected.Count; i++)
        {
            switch (expected[i])
            {
                case Folder folder:
                    var actualFolder = actual[i].ShouldBeOfType<Folder>();
                    (actualFolder.Name, actualFolder.Auth).ShouldBe((folder.Name, folder.Auth));
                    AssertNodes(actualFolder.Children, folder.Children);
                    break;
                case HttpRequestDefinition http:
                    var actualHttp = actual[i].ShouldBeOfType<HttpRequestDefinition>();
                    (actualHttp.Name, actualHttp.Method, actualHttp.Url, actualHttp.Auth, actualHttp.Settings)
                        .ShouldBe((http.Name, http.Method, http.Url, http.Auth, http.Settings));
                    actualHttp.QueryParameters.ShouldBe(http.QueryParameters);
                    actualHttp.Headers.ShouldBe(http.Headers);
                    (actualHttp.Body.Mode, actualHttp.Body.Content, actualHttp.Body.FilePath).ShouldBe((http.Body.Mode, http.Body.Content, http.Body.FilePath));
                    actualHttp.Body.FormFields.ShouldBe(http.Body.FormFields);
                    actualHttp.Body.MultipartFields.ShouldBe(http.Body.MultipartFields);
                    break;
                case GrpcRequestDefinition grpc:
                    var actualGrpc = actual[i].ShouldBeOfType<GrpcRequestDefinition>();
                    (actualGrpc.Name, actualGrpc.ServerUrl, actualGrpc.Service, actualGrpc.Method, actualGrpc.Message, actualGrpc.Auth, actualGrpc.Settings)
                        .ShouldBe((grpc.Name, grpc.ServerUrl, grpc.Service, grpc.Method, grpc.Message, grpc.Auth, grpc.Settings));
                    actualGrpc.Metadata.ShouldBe(grpc.Metadata);
                    break;
            }
        }
    }
}
