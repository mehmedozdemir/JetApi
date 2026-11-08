using JetApi.Core.Model;

namespace JetApi.Storage.Tests;

public sealed class OAuth2StorageTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static HttpRequestDefinition Request(AuthConfig auth) => new() { Key = "r", Name = "R", Url = "{{baseUrl}}/x", Auth = auth };

    private static AuthConfig RoundTrip(AuthConfig auth) =>
        RequestFiles.Deserialize(RequestFiles.Serialize(Request(auth)), "r", isGrpc: false).Auth;

    [Fact]
    public void ClientCredentials_WithDefaults_WritesOnlyWhatIsSet()
    {
        var auth = new OAuth2Auth(OAuth2GrantType.ClientCredentials, "{{tokenUrl}}", "{{clientId}}", "{{clientSecret}}", Scope: "read write");

        var yaml = RequestFiles.Serialize(Request(auth));

        yaml.ShouldBe(
            """
            schemaVersion: 1
            name: R
            method: GET
            url: '{{baseUrl}}/x'
            auth:
              type: oauth2
              grant: client_credentials
              tokenUrl: '{{tokenUrl}}'
              clientId: '{{clientId}}'
              clientSecret: '{{clientSecret}}'
              scope: read write

            """);
        RoundTrip(auth).ShouldBe(auth);
    }

    [Fact]
    public void AuthorizationCode_WithEveryOption_RoundTrips()
    {
        var auth = new OAuth2Auth(
            OAuth2GrantType.AuthorizationCode,
            TokenUrl: "https://id.test/token",
            ClientId: "app",
            Scope: "openid",
            Audience: "https://api.test",
            AuthorizationUrl: "https://id.test/authorize",
            RedirectUri: "http://127.0.0.1:7777/cb",
            UsePkce: false,
            ClientAuthentication: OAuth2ClientAuthentication.Body);

        var yaml = RequestFiles.Serialize(Request(auth));

        yaml.ShouldContain(
            """
            auth:
              type: oauth2
              grant: authorization_code
              tokenUrl: https://id.test/token
              authUrl: https://id.test/authorize
              clientId: app
              scope: openid
              audience: https://api.test
              redirectUri: http://127.0.0.1:7777/cb
              pkce: false
              clientAuth: body
            """.ReplaceLineEndings("\n"));
        RoundTrip(auth).ShouldBe(auth);
    }

    [Fact]
    public void Password_RoundTrips()
    {
        var auth = new OAuth2Auth(OAuth2GrantType.Password, "https://id.test/token", "app", Username: "ayşe", Password: "{{password}}");

        RoundTrip(auth).ShouldBe(auth);
        RequestFiles.Serialize(Request(auth)).ShouldContain("  grant: password\n  tokenUrl: https://id.test/token\n  clientId: app\n  username: ayşe\n  password: '{{password}}'\n");
    }

    [Fact]
    public void Read_MinimalOAuth2_UsesDefaults()
    {
        var auth = RequestFiles.Deserialize(
            "schemaVersion: 1\nname: R\nauth:\n  type: oauth2\n  grant: client_credentials\n",
            "r",
            isGrpc: false).Auth;

        auth.ShouldBe(new OAuth2Auth());
        ((OAuth2Auth)auth).RedirectUri.ShouldBe(OAuth2Auth.DefaultRedirectUri);
    }

    [Theory]
    [InlineData("  type: oauth2\n", "'auth.grant' is required.")]
    [InlineData("  type: oauth2\n  grant: implicit\n", "auth.grant must be 'client_credentials', 'authorization_code' or 'password', not 'implicit'.")]
    [InlineData("  type: oauth2\n  grant: password\n  clientAuth: header\n", "auth.clientAuth must be 'basic' or 'body', not 'header'.")]
    public void Read_InvalidOAuth2_ReportsTheProblem(string auth, string message) =>
        Should.Throw<InvalidDataException>(() => RequestFiles.Deserialize($"schemaVersion: 1\nname: R\nauth:\n{auth}", "r", isGrpc: false))
            .Message.ShouldBe(message);

    [Fact]
    public async Task CollectionAndFolderAuth_RoundTripThroughTheWorkspace()
    {
        var collectionAuth = new OAuth2Auth(OAuth2GrantType.ClientCredentials, "https://id.test/token", "svc", "s3cr3t");
        var folderAuth = new OAuth2Auth(OAuth2GrantType.AuthorizationCode, "https://id.test/token", "spa", AuthorizationUrl: "https://id.test/authorize");
        var workspace = new Workspace
        {
            Name = "W",
            Collections =
            [
                new ApiCollection
                {
                    Key = "c",
                    Name = "C",
                    Auth = collectionAuth,
                    Children = [new Folder { Key = "f", Name = "F", Auth = folderAuth, Children = [Request(AuthConfig.Inherit)] }],
                },
            ],
        };

        await WorkspaceStore.SaveAsync(_directory.Path, workspace, Token);
        var loaded = await WorkspaceStore.LoadAsync(_directory.Path, Token);

        loaded.Diagnostics.ShouldBeEmpty();
        var collection = loaded.Workspace.Collections.ShouldHaveSingleItem();
        collection.Auth.ShouldBe(collectionAuth);
        ((Folder)collection.Children.ShouldHaveSingleItem()).Auth.ShouldBe(folderAuth);
    }
}
