using System.Net;
using System.Text;
using System.Text.Json;
using JetApi.Core.Model;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;
using JetApi.Grpc.Schema;

namespace JetApi.Desktop.Tests;

public sealed class OAuthTests(EchoServer server) : IClassFixture<EchoServer>
{
    private static readonly OAuth2Auth ClientCredentials = new(
        OAuth2GrantType.ClientCredentials,
        TokenUrl: "https://auth.test/token",
        ClientId: "{{clientId}}",
        ClientSecret: "{{clientSecret}}",
        Scope: "read");

    private static async Task<TempWorkspace> SetupAsync(string baseUrl, AuthConfig collectionAuth)
    {
        var workspace = await TempWorkspace.CreateAsync();
        await workspace.Session.SaveEnvironmentAsync(new ApiEnvironment
        {
            Key = "dev",
            Name = "Development",
            Variables = [new("baseUrl", baseUrl.TrimEnd('/')), new("clientId", "app"), new("clientSecret", "s3cret", IsSecret: true)],
        });
        var collection = workspace.Session.Workspace.Collections[0];
        await workspace.Session.SaveCollectionAsync(collection with { Auth = collectionAuth });
        return workspace;
    }

    [Fact]
    public async Task ClientCredentials_TokenIsFetchedOnSend_CachedAndSentAsBearer()
    {
        using var workspace = await SetupAsync(server.BaseUrl, ClientCredentials);
        var tokenEndpoint = new TokenEndpoint("""{ "access_token": "tok-1", "token_type": "bearer", "expires_in": 3600 }""");
        using var runner = new RequestRunner(new ProtoSchemaCache(), oauthHandler: tokenEndpoint);
        using var vm = workspace.CreateViewModel(new FakeDialogs(), runner);
        vm.OpenNode(vm.Collections[0].Children[0]);
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
        tab.Url = "{{baseUrl}}/echo";

        await tab.SendAsync();
        await tab.SendAsync();

        tab.Response.StatusText.ShouldBe("200 OK");
        using var json = JsonDocument.Parse(tab.Response.DisplayedBody);
        json.RootElement.GetProperty("authorization").GetString().ShouldBe("Bearer tok-1");
        var request = tokenEndpoint.Requests.ShouldHaveSingleItem();
        request.ShouldContain("grant_type=client_credentials");
        request.ShouldContain("scope=read");
        File.Exists(Path.Combine(workspace.Root, ".jetapi", "tokens.json")).ShouldBeTrue();

        // The request inherits the collection's OAuth 2.0, so its Auth tab shows the token.
        tab.Auth.IsInherit.ShouldBeTrue();
        tab.Auth.ShowToken.ShouldBeTrue();
        tab.Auth.HasToken.ShouldBeTrue();
        tab.Auth.TokenStatus.ShouldNotBe(Localization.Loc.Get("OAuth_NoTokenYet"));
    }

    [Fact]
    public async Task TokenEndpointError_IsShownInsteadOfSending()
    {
        using var workspace = await SetupAsync(server.BaseUrl, ClientCredentials);
        var tokenEndpoint = new TokenEndpoint("""{ "error": "invalid_client" }""", HttpStatusCode.Unauthorized);
        using var runner = new RequestRunner(new ProtoSchemaCache(), oauthHandler: tokenEndpoint);
        using var vm = workspace.CreateViewModel(new FakeDialogs(), runner);
        vm.OpenNode(vm.Collections[0].Children[0]);
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
        tab.Url = "{{baseUrl}}/echo";

        await tab.SendAsync();

        tab.Response.HasPreparationErrors.ShouldBeTrue();
        tab.Response.PreparationErrors.ShouldHaveSingleItem().ShouldContain("invalid_client");
    }

    [Fact]
    public async Task AnAuthorizationHeaderOfTheRequest_SkipsTheToken()
    {
        using var workspace = await SetupAsync(server.BaseUrl, ClientCredentials);
        var tokenEndpoint = new TokenEndpoint("""{ "access_token": "unused" }""");
        using var runner = new RequestRunner(new ProtoSchemaCache(), oauthHandler: tokenEndpoint);
        using var vm = workspace.CreateViewModel(new FakeDialogs(), runner);
        vm.OpenNode(vm.Collections[0].Children[0]);
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
        tab.Url = "{{baseUrl}}/echo";
        tab.Headers.Rows[^1].Key = "Authorization";
        tab.Headers.Rows[0].Value = "Custom 1";

        await tab.SendAsync();

        tokenEndpoint.Requests.ShouldBeEmpty();
        using var json = JsonDocument.Parse(tab.Response.DisplayedBody);
        json.RootElement.GetProperty("authorization").GetString().ShouldBe("Custom 1");
    }

    [Fact]
    public async Task ExpiredToken_IsRefreshed_AndKeepsItsRefreshToken()
    {
        using var workspace = await SetupAsync(server.BaseUrl, ClientCredentials);
        var tokenEndpoint = new TokenEndpoint("""{ "access_token": "tok-2", "expires_in": 60 }""");
        using var runner = new RequestRunner(new ProtoSchemaCache(), oauthHandler: tokenEndpoint);
        var key = workspace.Session.Workspace.Collections[0].Key;
        var resolved = RequestRunner.ResolveOAuth(workspace.Session, key, ClientCredentials);
        workspace.Session.Tokens.Set(resolved, new OAuth2Token("old", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1), RefreshToken: "r1"));

        var error = await runner.EnsureOAuthTokenAsync(workspace.Session, resolved, TestContext.Current.CancellationToken);

        error.ShouldBeNull();
        tokenEndpoint.Requests.ShouldHaveSingleItem().ShouldContain("grant_type=refresh_token");
        var token = workspace.Session.Tokens.Get(resolved).ShouldNotBeNull();
        token.AccessToken.ShouldBe("tok-2");
        token.RefreshToken.ShouldBe("r1");
    }

    [Fact]
    public async Task AuthorizationCodeWithoutToken_AsksForTheBrowserSignIn()
    {
        var auth = ClientCredentials with { GrantType = OAuth2GrantType.AuthorizationCode, AuthorizationUrl = "https://auth.test/authorize" };
        using var workspace = await SetupAsync(server.BaseUrl, auth);
        var tokenEndpoint = new TokenEndpoint("""{ "access_token": "unused" }""");
        using var runner = new RequestRunner(new ProtoSchemaCache(), oauthHandler: tokenEndpoint);
        using var vm = workspace.CreateViewModel(new FakeDialogs(), runner);
        vm.OpenNode(vm.Collections[0].Children[0]);
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
        tab.Url = "{{baseUrl}}/echo";

        await tab.SendAsync();

        tokenEndpoint.Requests.ShouldBeEmpty();
        tab.Response.PreparationErrors.ShouldHaveSingleItem().ShouldContain("Get a new access token");
    }

    [Fact]
    public void Editor_RoundTripsEveryOAuthSetting()
    {
        var auth = new OAuth2Auth(
            OAuth2GrantType.Password,
            "https://auth.test/token",
            "app",
            "secret",
            "read write",
            "api",
            string.Empty,
            "ayse",
            "pw",
            "http://127.0.0.1:7777/cb",
            UsePkce: false,
            OAuth2ClientAuthentication.Body);

        var editor = new AuthEditorViewModel(auth);

        editor.IsOAuth2.ShouldBeTrue();
        editor.IsPasswordGrant.ShouldBeTrue();
        editor.IsAuthorizationCode.ShouldBeFalse();
        editor.ToAuth().ShouldBe(auth);
        AuthEditorViewModel.Describe(auth).ShouldBe("OAuth 2.0 · Password");
    }

    [Fact]
    public void Editor_GrantSwitch_DropsFieldsOfOtherGrants()
    {
        var editor = new AuthEditorViewModel(AuthConfig.Inherit);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.SelectedKind = editor.Kinds.Single(k => k.Value == AuthKind.OAuth2);
        editor.SelectedGrant = editor.Grants.Single(g => g.Value == OAuth2GrantType.Password);
        editor.OAuthUsername = "ayse";
        editor.SelectedGrant = editor.Grants.Single(g => g.Value == OAuth2GrantType.ClientCredentials);

        changes.ShouldBe(4);
        editor.ToAuth().ShouldBe(new OAuth2Auth());
        editor.ShowToken.ShouldBeFalse();
    }

    [Fact]
    public async Task Editor_GetToken_ShowsTheErrorAndForgetClearsTheToken()
    {
        var tokens = new FakeTokens();
        var editor = new AuthEditorViewModel(ClientCredentials, tokens: tokens);
        editor.ShowToken.ShouldBeTrue();
        editor.HasToken.ShouldBeFalse();

        tokens.NextError = "The authorization server returned 'invalid_client'.";
        await editor.GetTokenCommand.ExecuteAsync(null);
        editor.TokenError.ShouldBe(tokens.NextError);

        tokens.NextError = null;
        await editor.GetTokenCommand.ExecuteAsync(null);
        editor.TokenError.ShouldBeNull();
        editor.HasToken.ShouldBeTrue();

        editor.ForgetTokenCommand.Execute(null);
        editor.HasToken.ShouldBeFalse();
        editor.TokenStatus.ShouldBe(Localization.Loc.Get("OAuth_NoTokenYet"));
    }

    private sealed class FakeTokens : IOAuthTokenService
    {
        private OAuth2Token? _token;

        public string? NextError { get; set; }

        public OAuth2Token? Current(OAuth2Auth auth) => _token;

        public Task<string?> GetNewTokenAsync(OAuth2Auth auth, CancellationToken cancellationToken)
        {
            if (NextError is null)
            {
                _token = new OAuth2Token("abc", ExpiresAt: DateTimeOffset.UtcNow.AddHours(1));
            }

            return Task.FromResult(NextError);
        }

        public void Forget(OAuth2Auth auth) => _token = null;
    }

    private sealed class TokenEndpoint(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
