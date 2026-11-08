using System.Net;
using JetApi.Core.Model;
using static JetApi.Http.Tests.OAuthTestEndpoints;

namespace JetApi.Http.Tests;

public sealed class OAuth2ClientTests(TestServer server) : IClassFixture<TestServer>, IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly OAuth2Client _client = new(timeProvider: new FixedTime(Now), timeout: TimeSpan.FromMilliseconds(500));

    public void Dispose() => _client.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private OAuth2Auth Auth(OAuth2GrantType grant = OAuth2GrantType.ClientCredentials, string path = "/oauth/token") =>
        new(grant, new Uri(server.BaseUrl, path).AbsoluteUri, ClientId, ClientSecret);

    [Fact]
    public async Task ClientCredentials_BasicHeader_ReturnsTheToken()
    {
        var result = await _client.RequestTokenAsync(Auth() with { Scope = "read write", Audience = "https://api.test" }, Token);

        result.IsSuccess.ShouldBeTrue(result.ErrorMessage);
        result.Token.ShouldBe(new OAuth2Token("cc|app|basic|read write,https://api.test", "bearer", Now.AddHours(1), null, "read write"));
        result.StatusCode.ShouldBe(HttpStatusCode.OK);
        result.ErrorMessage.ShouldBeNull();
        result.ResponseBody.ShouldBeNull();
    }

    [Fact]
    public async Task ClientCredentials_Body_SendsCredentialsInTheForm()
    {
        var result = await _client.RequestTokenAsync(Auth() with { ClientAuthentication = OAuth2ClientAuthentication.Body }, Token);

        result.Token!.AccessToken.ShouldBe("cc|app|body|");
        result.Token.ExpiresAt.ShouldBe(Now.AddHours(1));
    }

    [Theory]
    [InlineData(OAuth2ClientAuthentication.BasicHeader, "basic")]
    [InlineData(OAuth2ClientAuthentication.Body, "body")]
    public async Task ClientCredentials_SpecialCharacters_AreEncodedPerRfc6749(OAuth2ClientAuthentication mode, string expected)
    {
        var auth = Auth() with { ClientId = OddClientId, ClientSecret = OddClientSecret, ClientAuthentication = mode };

        var result = await _client.RequestTokenAsync(auth, Token);

        result.Token!.AccessToken.ShouldBe($"cc|{OddClientId}|{expected}|");
    }

    [Fact]
    public async Task WrongSecret_IsInvalidClient_WithAHint()
    {
        var result = await _client.RequestTokenAsync(Auth() with { ClientSecret = "wrong" }, Token);

        result.IsSuccess.ShouldBeFalse();
        result.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        result.Error.ShouldBe("invalid_client");
        result.ErrorDescription.ShouldBe("Client authentication failed.");
        result.ErrorMessage.ShouldBe(
            "The authorization server returned 'invalid_client': Client authentication failed. "
            + "Check the client ID and secret, and whether the server expects them in the Basic header or in the body.");
        result.ResponseBody!.ShouldContain("invalid_client");
    }

    [Fact]
    public async Task Password_ReturnsTokenWithRefreshToken_AndStringExpiresIn()
    {
        var auth = Auth(OAuth2GrantType.Password) with { Username = Username, Password = Password, Scope = "profile" };

        var result = await _client.RequestTokenAsync(auth, Token);

        result.Token.ShouldBe(new OAuth2Token("password|app|basic|profile", "bearer", Now.AddMinutes(2), RefreshToken, "profile"));
    }

    [Fact]
    public async Task Password_WrongCredentials_IsInvalidGrant()
    {
        var result = await _client.RequestTokenAsync(Auth(OAuth2GrantType.Password) with { Username = Username, Password = "nope" }, Token);

        result.Error.ShouldBe("invalid_grant");
        result.ErrorMessage.ShouldBe(
            "The authorization server returned 'invalid_grant': Wrong user name or password. "
            + "The code, refresh token or user credentials are wrong or have expired. Get a new token.");
    }

    [Fact]
    public async Task Password_PublicClient_SendsOnlyTheClientId()
    {
        var auth = Auth(OAuth2GrantType.Password) with { ClientId = PublicClientId, ClientSecret = "", Username = Username, Password = Password };

        (await _client.RequestTokenAsync(auth, Token)).Token!.AccessToken.ShouldBe("password|spa|none|");
    }

    [Fact]
    public async Task Refresh_ReturnsANewToken_AndKeepsTheRefreshTokenWhenNoNewOneIsIssued()
    {
        var result = await _client.RefreshAsync(Auth(), RefreshToken, Token);

        result.Token.ShouldBe(new OAuth2Token("refreshed|app|basic|", "bearer", Now.AddMinutes(1), RefreshToken));
    }

    [Fact]
    public async Task Refresh_ExpiredRefreshToken_IsInvalidGrant()
    {
        var result = await _client.RefreshAsync(Auth(), "old", Token);

        result.Error.ShouldBe("invalid_grant");
        result.ErrorMessage!.ShouldStartWith("The authorization server returned 'invalid_grant': The refresh token has expired.");
    }

    [Fact]
    public async Task UnsupportedGrant_WithoutDescription()
    {
        var auth = Auth() with { ClientId = PublicClientId, ClientSecret = "" };

        var result = await _client.RequestTokenAsync(auth, Token);

        result.Error.ShouldBe("unsupported_grant_type");
        result.ErrorMessage.ShouldBe("The authorization server returned 'unsupported_grant_type'. This server does not allow this grant type for the client.");
    }

    [Fact]
    public async Task HtmlErrorPage_IsReportedWithStatusAndBody()
    {
        var result = await _client.RequestTokenAsync(Auth(path: "/oauth/html"), Token);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("The token endpoint answered HTTP 502 without an access token.");
        result.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        result.ResponseBody.ShouldBe("<html><body>Gateway error</body></html>");
    }

    [Fact]
    public async Task SuccessWithoutAccessToken_IsAFailure() =>
        (await _client.RequestTokenAsync(Auth(path: "/oauth/no-token"), Token)).ErrorMessage
            .ShouldBe("The token endpoint answered HTTP 200 without an access token.");

    [Fact]
    public async Task FormEncodedResponse_IsUnderstood()
    {
        var result = await _client.RequestTokenAsync(Auth(path: "/oauth/form"), Token);

        result.Token.ShouldBe(new OAuth2Token("form-token", "bearer", null, null, "user repo"));
    }

    [Fact]
    public async Task SlowServer_TimesOut()
    {
        var result = await _client.RequestTokenAsync(Auth(path: "/oauth/slow"), Token);

        result.ErrorMessage.ShouldBe("The token endpoint did not answer within 500 ms.");
    }

    [Fact]
    public async Task Cancellation_IsReported()
    {
        using var client = new OAuth2Client();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await client.RequestTokenAsync(Auth(path: "/oauth/slow"), cancellation.Token);

        result.ErrorMessage.ShouldBe("Getting the token was cancelled.");
    }

    [Fact]
    public async Task UnreachableServer_IsReported()
    {
        // Windows retries a refused loopback connection for about two seconds.
        using var client = new OAuth2Client(timeout: TimeSpan.FromSeconds(20));

        var result = await client.RequestTokenAsync(Auth() with { TokenUrl = "http://127.0.0.1:1/token" }, Token);

        result.ErrorMessage.ShouldBe("The token endpoint refused the connection. Is the server running and listening on this port?");
    }

    [Theory]
    [InlineData(OAuth2GrantType.ClientCredentials, "", "u", "Client ID is required.")]
    [InlineData(OAuth2GrantType.Password, "app", "", "Username is required for the password grant.")]
    [InlineData(OAuth2GrantType.AuthorizationCode, "app", "", "The authorization code grant signs you in through the browser. Use “Get new access token” to start the sign-in.")]
    public async Task InvalidInput_IsReportedWithoutCallingTheServer(OAuth2GrantType grant, string clientId, string username, string message)
    {
        var result = await _client.RequestTokenAsync(Auth(grant) with { ClientId = clientId, Username = username }, Token);

        result.ErrorMessage.ShouldBe(message);
        result.StatusCode.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{{tokenUrl}}")]
    [InlineData("ftp://id.test/token")]
    public async Task InvalidTokenUrl_IsReported(string url) =>
        (await _client.RequestTokenAsync(Auth() with { TokenUrl = url }, Token)).ErrorMessage
            .ShouldBe($"'{url}' is not a valid http or https token URL.");

    [Fact]
    public async Task GetToken_DispatchesByGrant()
    {
        (await _client.GetTokenAsync(Auth(), cancellationToken: Token)).Token!.AccessToken.ShouldStartWith("cc|");
        (await _client.GetTokenAsync(Auth(OAuth2GrantType.AuthorizationCode), cancellationToken: Token)).ErrorMessage!
            .ShouldContain("through the browser");
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
