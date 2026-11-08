using JetApi.Core.Model;
using JetApi.Storage;

namespace JetApi.Import.Tests;

public sealed class PostmanImporterTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name));

    private static CollectionImport ImportShop() =>
        PostmanImporter.ImportCollection(Sample("shop.postman_collection.json"), []).Value.ShouldNotBeNull();

    private static IReadOnlyList<ImportIssue> ShopIssues() =>
        PostmanImporter.ImportCollection(Sample("shop.postman_collection.json"), []).Issues;

    private static T Node<T>(CollectionImport import, params string[] names)
        where T : CollectionNode
    {
        IReadOnlyList<CollectionNode> level = import.Collection.Children;
        CollectionNode? node = null;
        foreach (var name in names)
        {
            node = level.First(n => n.Name == name);
            level = node is Folder folder ? folder.Children : [];
        }

        return node.ShouldBeOfType<T>();
    }

    [Fact]
    public void Detect_TellsCollectionsFromEnvironments()
    {
        PostmanImporter.Detect(Sample("shop.postman_collection.json")).ShouldBe(ImportKind.PostmanCollection);
        PostmanImporter.Detect(Sample("dev.postman_environment.json")).ShouldBe(ImportKind.PostmanEnvironment);
        PostmanImporter.Detect("{\"openapi\": \"3.0.0\"}").ShouldBe(ImportKind.Unknown);
        PostmanImporter.Detect("not json").ShouldBe(ImportKind.Unknown);
    }

    [Fact]
    public void Collection_KeepsNameDescriptionAuthAndVariables()
    {
        var import = ImportShop();
        var collection = import.Collection;

        collection.Name.ShouldBe("Shop API");
        collection.Key.ShouldBe("shop-api");
        collection.Description.ShouldBe("Orders and customers.");
        collection.Auth.ShouldBe(new BearerAuth("{{token}}"));
        collection.Variables.ShouldBe([new Variable("apiVersion", "v2"), new Variable("legacy", "1", IsEnabled: false), new Variable("signingKey", "s3cret", IsSecret: true)]);
        import.RequestCount.ShouldBe(10);
        import.FolderCount.ShouldBe(2);
    }

    [Fact]
    public void CollectionKey_DoesNotClashWithTheWorkspace() =>
        PostmanImporter.ImportCollection(Sample("shop.postman_collection.json"), ["shop-api"]).Value!.Collection.Key.ShouldBe("shop-api-2");

    [Fact]
    public void Folders_KeepDescriptionsAndAuth()
    {
        var import = ImportShop();

        Node<Folder>(import, "Orders").Description.ShouldBe("Order endpoints");
        Node<Folder>(import, "Orders").Auth.ShouldBe(AuthConfig.Inherit);
        Node<Folder>(import, "Customers").Auth.ShouldBe(new ApiKeyAuth("X-Api-Key", "{{apiKey}}", ApiKeyLocation.Query));
    }

    [Fact]
    public void Url_SeparatesTheQueryAndKeepsDisabledEntries()
    {
        var list = Node<HttpRequestDefinition>(ImportShop(), "Orders", "List orders");

        list.Method.ShouldBe("GET");
        list.Url.ShouldBe("{{baseUrl}}/{{apiVersion}}/orders#top");
        list.QueryParameters.ShouldBe([new KeyValueEntry("page", "1"), new KeyValueEntry("size", "20", IsEnabled: false)]);
        list.Headers.ShouldBe([new KeyValueEntry("Accept", "application/json"), new KeyValueEntry("X-Debug", "1", IsEnabled: false, Description: "only locally")]);
        list.Auth.ShouldBe(AuthConfig.Inherit);
    }

    [Fact]
    public void PathVariables_UseTheirValueOrBecomeVariables()
    {
        Node<HttpRequestDefinition>(ImportShop(), "Orders", "Get order").Url.ShouldBe("{{baseUrl}}/orders/42/items/{{itemId}}");
        ShopIssues().ShouldContain(i => i.Location == "Orders / Get order" && i.Message.Contains(":itemId", StringComparison.Ordinal));
    }

    [Fact]
    public void RequestsWithTheSameName_GetDistinctKeys()
    {
        var orders = Node<Folder>(ImportShop(), "Orders").Children;

        orders.Where(n => n.Name == "Create order").Select(n => n.Key).ShouldBe(["create-order", "create-order-2"]);
    }

    [Fact]
    public void Bodies_MapToTheirJetApiModes()
    {
        var import = ImportShop();
        var orders = Node<Folder>(import, "Orders").Children.OfType<HttpRequestDefinition>().Where(r => r.Name == "Create order").ToList();

        orders[0].Body.Mode.ShouldBe(HttpBodyMode.Json);
        orders[0].Body.Content.ShouldContain("\"total\": 10");
        orders[0].Auth.ShouldBe(new BasicAuth("admin", "pa$$"));
        orders[1].Body.ShouldBe(HttpBody.Xml("<order />"));

        var signUp = Node<HttpRequestDefinition>(import, "Customers", "Sign up");
        signUp.Body.Mode.ShouldBe(HttpBodyMode.FormUrlEncoded);
        signUp.Body.FormFields.ShouldBe([new KeyValueEntry("email", "a@b.c"), new KeyValueEntry("promo", "x", IsEnabled: false)]);
        signUp.Settings.ShouldBe(new HttpRequestSettings(FollowRedirects: false, ValidateCertificates: false));

        var avatar = Node<HttpRequestDefinition>(import, "Customers", "Upload avatar");
        avatar.Auth.ShouldBe(AuthConfig.None);
        avatar.Body.Mode.ShouldBe(HttpBodyMode.Multipart);
        avatar.Body.MultipartFields.ShouldBe([
            new MultipartField("name", "Ayşe"),
            new MultipartField("avatar", "/Users/ayse/avatar.png", MultipartFieldKind.File, ContentType: "image/png"),
        ]);

        var file = Node<HttpRequestDefinition>(import, "Customers", "Import file");
        file.Body.Mode.ShouldBe(HttpBodyMode.File);
        file.Body.FilePath.ShouldBe("data/customers.csv");

        var search = Node<HttpRequestDefinition>(import, "Customers", "Search");
        search.Body.Mode.ShouldBe(HttpBodyMode.Json);
        search.Body.Content.ShouldContain("\"query\": \"query { customers { id } }\"");
        search.Body.Content.ShouldContain("\"first\": 10");

        var ping = Node<HttpRequestDefinition>(import, "Customers", "Ping");
        ping.Url.ShouldBe("{{baseUrl}}/ping");
        ping.QueryParameters.ShouldBe([new KeyValueEntry("verbose", string.Empty)]);

        Node<HttpRequestDefinition>(import, "Customers", "Disabled body").Body.ShouldBe(HttpBody.Empty);
    }

    [Fact]
    public void Report_ListsWhatWasNotCarriedOver()
    {
        var issues = ShopIssues();

        issues.ShouldContain(i => i.Location == "Collection" && i.Message.Contains("Pre-request", StringComparison.Ordinal));
        issues.ShouldContain(i => i.Location == "Orders / Get order" && i.Message.Contains("Test script", StringComparison.Ordinal));
        issues.ShouldContain(i => i.Location == "Orders / List orders" && i.Severity == ImportIssueSeverity.Info);
        issues.ShouldContain(i => i.Location == "Customers / Import file" && i.Message.Contains("oauth2", StringComparison.Ordinal));
        issues.ShouldContain(i => i.Location == "Customers / Upload avatar" && i.Message.Contains("/Users/ayse/avatar.png", StringComparison.Ordinal));
        issues.ShouldContain(i => i.Location == "Customers / Search" && i.Severity == ImportIssueSeverity.Info);
        // $randomUUID is supported; $randomEmail is not.
        var dynamic = issues.Single(i => i.Message.Contains("$randomEmail", StringComparison.Ordinal));
        dynamic.Message.ShouldNotContain("$randomUUID");
    }

    [Theory]
    [InlineData("{ broken", "not valid JSON")]
    [InlineData("{\"values\": []}", "not a Postman collection")]
    [InlineData("{\"info\": {\"name\": \"Old\", \"schema\": \"https://schema.getpostman.com/json/collection/v1.0.0/collection.json\"}}", "not supported")]
    public void UnusableFiles_ReturnAnErrorInsteadOfThrowing(string json, string expected)
    {
        var result = PostmanImporter.ImportCollection(json, []);

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldNotBeNull().ShouldContain(expected);
    }

    [Fact]
    public void Environment_KeepsValuesSecretsAndDisabledEntries()
    {
        var result = PostmanImporter.ImportEnvironment(Sample("dev.postman_environment.json"), ["development"]);
        var environment = result.Value.ShouldNotBeNull();

        environment.Name.ShouldBe("Development");
        environment.Key.ShouldBe("development-2");
        environment.Variables.ShouldBe([
            new Variable("baseUrl", "https://dev.shop.local"),
            new Variable("token", "dev-token", IsSecret: true),
            new Variable("retries", "3", IsEnabled: false),
        ]);
        PostmanImporter.ImportEnvironment(Sample("shop.postman_collection.json"), []).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task ImportedCollection_RoundTripsThroughTheYamlWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "jetapi-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            var collection = ImportShop().Collection;
            await WorkspaceStore.SaveAsync(root, new Workspace { Name = "Imported", Collections = [collection] }, TestContext.Current.CancellationToken);

            var loaded = await WorkspaceStore.LoadAsync(root, TestContext.Current.CancellationToken);

            loaded.Diagnostics.ShouldBeEmpty();
            var roundTripped = loaded.Workspace.Collections.ShouldHaveSingleItem();
            roundTripped.Name.ShouldBe(collection.Name);
            roundTripped.Variables.ShouldBe(collection.Variables);
            CollectionTree.EnumerateRequests(roundTripped).Count().ShouldBe(10);
            // Same YAML per request means nothing was lost on the way to disk and back.
            CollectionTree.EnumerateRequests(roundTripped).Select(r => RequestFiles.Serialize(r.Request))
                .ShouldBe(CollectionTree.EnumerateRequests(collection).Select(r => RequestFiles.Serialize(r.Request)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
