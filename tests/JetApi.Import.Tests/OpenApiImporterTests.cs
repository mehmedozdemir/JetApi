using System.Text.Json.Nodes;
using JetApi.Core.Model;
using JetApi.Storage;

namespace JetApi.Import.Tests;

public sealed class OpenApiImporterTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name));

    private static ImportResult<CollectionImport> Import(string name) => OpenApiImporter.Import(Sample(name), []);

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

    private static JsonNode Json(string content) => JsonNode.Parse(content).ShouldNotBeNull();

    [Fact]
    public void LooksLikeOpenApi_RecognizesOpenApiAndSwaggerOnly()
    {
        OpenApiImporter.LooksLikeOpenApi(Sample("petstore.openapi.yaml")).ShouldBeTrue();
        OpenApiImporter.LooksLikeOpenApi(Sample("library.openapi.json")).ShouldBeTrue();
        OpenApiImporter.LooksLikeOpenApi(Sample("users.swagger.json")).ShouldBeTrue();
        OpenApiImporter.LooksLikeOpenApi(Sample("shop.postman_collection.json")).ShouldBeFalse();
        OpenApiImporter.LooksLikeOpenApi("title: swagger fans").ShouldBeFalse();
        OpenApiImporter.LooksLikeOpenApi("{ \"openapi\": ").ShouldBeFalse();
        OpenApiImporter.LooksLikeOpenApi(string.Empty).ShouldBeFalse();
    }

    [Fact]
    public void OpenApi30Yaml_CollectionHasNameDescriptionServerAndAuth()
    {
        var result = Import("petstore.openapi.yaml");
        var import = result.Value.ShouldNotBeNull();
        var collection = import.Collection;

        collection.Name.ShouldBe("Pet Store");
        collection.Key.ShouldBe("pet-store");
        collection.Description.ShouldBe("Pets, orders and the people who buy them.\n\nVersion: 1.0.0");
        collection.Auth.ShouldBe(new BearerAuth("{{token}}"));
        collection.Variables.ShouldBe([
            new Variable("baseUrl", "https://eu.petstore.example.com/v2"),
            new Variable("token", string.Empty, IsSecret: true),
            new Variable("petId", "42"),
            new Variable("apiKey", string.Empty, IsSecret: true),
        ]);
        import.RequestCount.ShouldBe(8);
        import.FolderCount.ShouldBe(2);
        result.Issues.ShouldContain(i => i.Location == "Collection" && i.Message.Contains("https://staging.petstore.example.com/v2", StringComparison.Ordinal)
            && i.Severity == ImportIssueSeverity.Info);
    }

    [Fact]
    public void OpenApi30Yaml_FoldersFollowTheTagOrderAndUntaggedStayAtTheRoot()
    {
        var import = Import("petstore.openapi.yaml").Value.ShouldNotBeNull();
        var children = import.Collection.Children;

        children.Select(c => c.Name).ShouldBe(["store", "pet", "Health check", "GET /legacy"]);
        children[0].ShouldBeOfType<Folder>().Description.ShouldBe("Orders");
        children[1].ShouldBeOfType<Folder>().Children.Select(c => c.Name)
            .ShouldBe(["List pets", "Add a pet", "getPetById", "Delete a pet", "Upload a photo"]);
        children[1].ShouldBeOfType<Folder>().Children.Select(c => c.Key)
            .ShouldBe(["list-pets", "add-a-pet", "getpetbyid", "delete-a-pet", "upload-a-photo"]);
        children[3].Key.ShouldBe("get-legacy");
    }

    [Fact]
    public void OpenApi30Yaml_ParametersBecomeQueryHeadersCookiesAndPathVariables()
    {
        var import = Import("petstore.openapi.yaml").Value.ShouldNotBeNull();

        var list = Node<HttpRequestDefinition>(import, "pet", "List pets");
        list.Method.ShouldBe("GET");
        list.Url.ShouldBe("{{baseUrl}}/pets");
        list.Auth.ShouldBe(AuthConfig.Inherit);
        list.QueryParameters.ShouldBe([
            new KeyValueEntry("status", "available", Description: "Filter by status"),
            new KeyValueEntry("limit", "20", IsEnabled: false),
            new KeyValueEntry("tags", "dog", IsEnabled: false),
            new KeyValueEntry("tags", "cat", IsEnabled: false),
        ]);
        list.Headers.ShouldBe([new KeyValueEntry("X-Trace-Id", "trace-1")]);

        var get = Node<HttpRequestDefinition>(import, "pet", "getPetById");
        get.Url.ShouldBe("{{baseUrl}}/pets/{{petId}}");
        get.Description.ShouldBe("Returns a single pet.");
        get.Auth.ShouldBe(AuthConfig.None);
        // Accept is left to the client, as OpenAPI says.
        get.Headers.ShouldBe([
            new KeyValueEntry("X-Request-Id", "0b6f3c1e-5a43-4f4e-9d0a-1c3f2e9b7a10", IsEnabled: false),
            new KeyValueEntry("Cookie", "session=abc123"),
        ]);

        var delete = Node<HttpRequestDefinition>(import, "pet", "Delete a pet");
        delete.Method.ShouldBe("DELETE");
        delete.Description.ShouldBe("Deprecated.\n\nUse status \"sold\" instead.");
        delete.Auth.ShouldBe(new ApiKeyAuth("X-API-Key", "{{apiKey}}"));
    }

    [Fact]
    public void OpenApi30Yaml_BodiesAreGeneratedFromSchemas()
    {
        var result = Import("petstore.openapi.yaml");
        var import = result.Value.ShouldNotBeNull();

        var add = Node<HttpRequestDefinition>(import, "pet", "Add a pet");
        add.Body.Mode.ShouldBe(HttpBodyMode.Json);
        add.Headers.ShouldBeEmpty();
        // allOf merged, read-only category id skipped, enum and formats filled in.
        JsonNode.DeepEquals(Json(add.Body.Content), Json("""
            {
              "name": "Rex",
              "status": "available",
              "born": "2025-01-01",
              "category": { "name": "Dogs" },
              "photoUrls": ["https://example.com"],
              "ownerEmail": "user@example.com",
              "chipId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
              "vaccinated": true
            }
            """)).ShouldBeTrue(add.Body.Content);
        result.Issues.ShouldContain(i => i.Location == "pet / Add a pet" && i.Message.Contains("application/xml", StringComparison.Ordinal));

        var photo = Node<HttpRequestDefinition>(import, "pet", "Upload a photo");
        photo.Body.Mode.ShouldBe(HttpBodyMode.Multipart);
        photo.Body.MultipartFields.ShouldBe([
            new MultipartField("caption", "On the beach", IsEnabled: false),
            new MultipartField("file", string.Empty, MultipartFieldKind.File, ContentType: "image/png"),
            new MultipartField("meta", "{\"width\":640}", IsEnabled: false, ContentType: "application/json"),
        ]);
        result.Issues.ShouldContain(i => i.Location == "pet / Upload a photo" && i.Message.Contains("\"file\"", StringComparison.Ordinal) && i.IsWarning);

        var order = Node<HttpRequestDefinition>(import, "store", "Place an order");
        order.Body.Mode.ShouldBe(HttpBodyMode.FormUrlEncoded);
        order.Body.FormFields.ShouldBe([new KeyValueEntry("petId", "7"), new KeyValueEntry("quantity", "1", IsEnabled: false)]);
    }

    [Fact]
    public void OpenApi30Yaml_OAuth2BecomesNoAuthWithAnIssue()
    {
        var result = Import("petstore.openapi.yaml");

        Node<HttpRequestDefinition>(result.Value!, "store", "Place an order").Auth.ShouldBe(AuthConfig.None);
        result.Issues.ShouldContain(i => i.Location == "store / Place an order" && i.Message.Contains("petstore_auth", StringComparison.Ordinal)
            && i.Message.Contains("oauth2", StringComparison.Ordinal) && i.IsWarning);
    }

    [Fact]
    public void OpenApi31Json_HandlesExamplesTypeListsAndMediaTypes()
    {
        var result = Import("library.openapi.json");
        var import = result.Value.ShouldNotBeNull();
        var collection = import.Collection;

        collection.Description.ShouldBe("Version: 2024-05");
        // The OAuth alternative is skipped in favor of basic auth, which JetApi can send.
        collection.Auth.ShouldBe(new BasicAuth("{{username}}", "{{password}}"));
        collection.Variables.ShouldBe([
            new Variable("baseUrl", "/api"),
            new Variable("username", string.Empty),
            new Variable("password", string.Empty, IsSecret: true),
            new Variable("apiKey", string.Empty, IsSecret: true),
            new Variable("isbn", "978-0441013593"),
        ]);
        collection.Children.Select(c => c.Name).ShouldBe(["books", "covers", "authors"]);

        var search = Node<HttpRequestDefinition>(import, "books", "searchBooks");
        search.Auth.ShouldBe(new ApiKeyAuth("api_key", "{{apiKey}}", ApiKeyLocation.Query));
        search.QueryParameters.ShouldBe([
            new KeyValueEntry("q", "Dune"),
            new KeyValueEntry("fields", "title,isbn", IsEnabled: false),
            new KeyValueEntry("lang", "en", IsEnabled: false),
        ]);

        var create = Node<HttpRequestDefinition>(import, "books", "createBook");
        create.Body.Mode.ShouldBe(HttpBodyMode.Json);
        JsonNode.DeepEquals(Json(create.Body.Content), Json("""{"data": {"type": "books", "attributes": {"title": "Dune", "year": 1965}}}""")).ShouldBeTrue();
        create.Headers.ShouldBe([new KeyValueEntry("Content-Type", "application/vnd.api+json")]);

        var cover = Node<HttpRequestDefinition>(import, "covers", "Replace the cover");
        cover.Url.ShouldBe("{{baseUrl}}/books/{{isbn}}/cover");
        cover.Body.Mode.ShouldBe(HttpBodyMode.File);
        cover.Body.FilePath.ShouldBe("body.png");
        cover.Headers.ShouldBe([new KeyValueEntry("Content-Type", "image/png")]);

        var note = Node<HttpRequestDefinition>(import, "books", "Add a note");
        note.Body.ShouldBe(HttpBody.Text("Great book"));
        note.Headers.ShouldBeEmpty();

        result.Issues.ShouldContain(i => i.Location == "Collection" && i.Message.Contains("\"/api\"", StringComparison.Ordinal) && i.IsWarning);
        result.Issues.ShouldContain(i => i.Location == "Collection" && i.Message.Contains("1 webhook", StringComparison.Ordinal));
        result.Issues.ShouldContain(i => i.Location == "covers / Replace the cover" && i.Message.Contains("image/png", StringComparison.Ordinal));
        result.Issues.ShouldContain(i => i.Location == "authors / Add an author" && i.Message.Contains("Callbacks", StringComparison.Ordinal));
        result.Issues.ShouldNotContain(i => i.Message.Contains("oauth", StringComparison.Ordinal));
    }

    [Fact]
    public void RecursiveSchemas_StopWhereTheyRepeat()
    {
        var result = Import("library.openapi.json");
        var author = Node<HttpRequestDefinition>(result.Value!, "authors", "Add an author");

        JsonNode.DeepEquals(Json(author.Body.Content), Json("""
            { "name": "Frank Herbert", "nickname": "string", "born": "2025-01-01T12:00:00Z", "books": [], "kind": 3 }
            """)).ShouldBeTrue(author.Body.Content);
        result.Issues.ShouldContain(i => i.Location == "authors / Add an author" && i.Message.Contains("\"Author\"", StringComparison.Ordinal)
            && i.Severity == ImportIssueSeverity.Info);
    }

    [Fact]
    public void Swagger20_MapsHostBodiesFormsAndSecurityDefinitions()
    {
        var result = Import("users.swagger.json");
        var import = result.Value.ShouldNotBeNull();
        var collection = import.Collection;

        collection.Name.ShouldBe("Users API");
        collection.Description.ShouldBe("Manage users.\n\nVersion: 1.4");
        collection.Auth.ShouldBe(new ApiKeyAuth("X-Key", "{{apiKey}}"));
        collection.Variables.ShouldBe([
            new Variable("baseUrl", "https://api.users.example.com/v1"),
            new Variable("apiKey", string.Empty, IsSecret: true),
            new Variable("username", string.Empty),
            new Variable("password", string.Empty, IsSecret: true),
            new Variable("userId", "u-1"),
            new Variable("session", string.Empty, IsSecret: true),
        ]);
        import.RequestCount.ShouldBe(4);
        import.FolderCount.ShouldBe(1);

        var list = Node<HttpRequestDefinition>(import, "users", "List users");
        list.QueryParameters.ShouldBe([
            new KeyValueEntry("role", "admin", IsEnabled: false),
            new KeyValueEntry("role", "editor", IsEnabled: false),
            new KeyValueEntry("page", "2"),
        ]);

        var create = Node<HttpRequestDefinition>(import, "users", "Create user");
        create.Auth.ShouldBe(new BasicAuth("{{username}}", "{{password}}"));
        JsonNode.DeepEquals(Json(create.Body.Content), Json("""{"email": "user@example.com", "roles": ["admin"], "active": false}""")).ShouldBeTrue(create.Body.Content);

        var avatar = Node<HttpRequestDefinition>(import, "users", "Upload avatar");
        avatar.Url.ShouldBe("{{baseUrl}}/users/{{userId}}/avatar");
        avatar.Body.MultipartFields.ShouldBe([
            new MultipartField("file", string.Empty, MultipartFieldKind.File),
            new MultipartField("note", "profile", IsEnabled: false),
        ]);

        var login = Node<HttpRequestDefinition>(import, "Log in");
        login.Body.Mode.ShouldBe(HttpBodyMode.FormUrlEncoded);
        login.Body.FormFields.ShouldBe([new KeyValueEntry("username", "ayse"), new KeyValueEntry("password", string.Empty)]);
        login.Auth.ShouldBe(AuthConfig.None);
        result.Issues.ShouldContain(i => i.Location == "Log in" && i.Message.Contains("Cookie: sid={{session}}", StringComparison.Ordinal));
    }

    [Fact]
    public void ServerVariablesWithoutDefaults_BecomeCollectionVariables()
    {
        const string yaml = """
            openapi: 3.0.0
            info: { title: Tenants, version: '1' }
            servers:
              - url: https://{tenant}.example.com
                variables:
                  tenant: { description: Your tenant }
            paths: {}
            """;

        var collection = OpenApiImporter.Import(yaml, []).Value.ShouldNotBeNull().Collection;

        collection.Variables.ShouldBe([new Variable("baseUrl", "https://{{tenant}}.example.com"), new Variable("tenant", string.Empty)]);
        collection.Description.ShouldBe("Version: 1");
    }

    [Fact]
    public void ReferenceProblems_AreReportedNotThrown()
    {
        const string yaml = """
            openapi: 3.0.0
            info: { title: Refs, version: 1.0 }
            servers: [{ url: 'https://refs.example.com' }]
            paths:
              /loop:
                post:
                  summary: Loop
                  requestBody:
                    content:
                      application/json:
                        schema: { $ref: '#/components/schemas/A' }
              /external:
                post:
                  summary: External
                  parameters:
                    - $ref: 'common.yaml#/parameters/Page'
                  requestBody:
                    content:
                      application/json:
                        schema: { $ref: '#/components/schemas/Missing' }
            components:
              schemas:
                A: { $ref: '#/components/schemas/B' }
                B: { $ref: '#/components/schemas/A' }
            """;

        var result = OpenApiImporter.Import(yaml, ["refs"]);
        var import = result.Value.ShouldNotBeNull();

        import.Collection.Key.ShouldBe("refs-2");
        import.Collection.Description.ShouldBe("Version: 1.0");
        Node<HttpRequestDefinition>(import, "Loop").Body.ShouldBe(HttpBody.Json(string.Empty));
        Node<HttpRequestDefinition>(import, "External").QueryParameters.ShouldBeEmpty();
        result.Issues.ShouldContain(i => i.Location == "Loop" && i.Message.Contains("loop", StringComparison.Ordinal));
        result.Issues.ShouldContain(i => i.Location == "External" && i.Message.Contains("common.yaml#/parameters/Page", StringComparison.Ordinal));
        result.Issues.ShouldContain(i => i.Location == "External" && i.Message.Contains("#/components/schemas/Missing", StringComparison.Ordinal));
    }

    [Fact]
    public void YamlAliasBombs_AreRejected()
    {
        var yaml = "openapi: 3.0.0\na: &a [x, x, x, x, x, x, x, x, x, x]\n";
        for (var level = 'b'; level <= 'h'; level++)
        {
            var previous = (char)(level - 1);
            yaml += $"{level}: &{level} [*{previous}, *{previous}, *{previous}, *{previous}, *{previous}, *{previous}, *{previous}, *{previous}, *{previous}, *{previous}]\n";
        }

        OpenApiImporter.Import(yaml, []).Error.ShouldNotBeNull().ShouldContain("too");
    }

    [Theory]
    [InlineData("{ \"openapi\": ", "not valid JSON or YAML")]
    [InlineData("openapi: [3.0", "not valid JSON or YAML")]
    [InlineData("", "not an OpenAPI or Swagger document")]
    [InlineData("just text", "not an OpenAPI or Swagger document")]
    [InlineData("{\"info\": {\"title\": \"x\"}}", "not an OpenAPI or Swagger document")]
    [InlineData("swagger: '1.2'", "\"1.2\" is not supported")]
    [InlineData("openapi: 4.0.0", "\"4.0.0\" is not supported")]
    public void UnusableFiles_ReturnAnErrorInsteadOfThrowing(string text, string expected)
    {
        var result = OpenApiImporter.Import(text, []);

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldNotBeNull().ShouldContain(expected);
    }

    [Theory]
    [InlineData("petstore.openapi.yaml", 8)]
    [InlineData("library.openapi.json", 5)]
    [InlineData("users.swagger.json", 4)]
    public async Task ImportedCollection_RoundTripsThroughTheYamlWorkspace(string sample, int requests)
    {
        var root = Path.Combine(Path.GetTempPath(), "jetapi-openapi-" + Guid.NewGuid().ToString("N"));
        try
        {
            var collection = Import(sample).Value.ShouldNotBeNull().Collection;
            await WorkspaceStore.SaveAsync(root, new Workspace { Name = "Imported", Collections = [collection] }, TestContext.Current.CancellationToken);

            var loaded = await WorkspaceStore.LoadAsync(root, TestContext.Current.CancellationToken);

            loaded.Diagnostics.Select(d => $"{d.FilePath}: {d.Message}").ShouldBeEmpty();
            var roundTripped = loaded.Workspace.Collections.ShouldHaveSingleItem();
            roundTripped.Name.ShouldBe(collection.Name);
            roundTripped.Auth.ShouldBe(collection.Auth);
            roundTripped.Variables.ShouldBe(collection.Variables);
            CollectionTree.EnumerateRequests(roundTripped).Count().ShouldBe(requests);
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
