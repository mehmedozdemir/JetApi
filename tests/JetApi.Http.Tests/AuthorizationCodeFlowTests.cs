using System.Globalization;
using System.Net;
using System.Net.Sockets;
using JetApi.Core.Model;
using static JetApi.Http.Tests.OAuthTestEndpoints;

namespace JetApi.Http.Tests;

public sealed class AuthorizationCodeFlowTests(TestServer server) : IClassFixture<TestServer>, IDisposable
{
    private readonly OAuth2Client _client = new();

    // Plays the browser: follows the authorization server's redirect to the loopback listener.
    private readonly HttpClient _browser = new();

    private Task<HttpResponseMessage>? _page;

    public void Dispose()
    {
        _client.Dispose();
        _browser.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private OAuth2Auth Auth(string clientId = PublicClientId) => new(
        OAuth2GrantType.AuthorizationCode,
        TokenUrl: new Uri(server.BaseUrl, "/oauth/token").AbsoluteUri,
        ClientId: clientId,
        Scope: "openid profile",
        AuthorizationUrl: new Uri(server.BaseUrl, "/oauth/authorize").AbsoluteUri);

    private Task OpenBrowser(Uri uri)
    {
        _page = _browser.GetAsync(uri, Token);
        return Task.CompletedTask;
    }

    private async Task<string> PageAsync()
    {
        using var response = await _page!;
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().ShouldBe("text/html; charset=utf-8");
        return await response.Content.ReadAsStringAsync(Token);
    }

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    [Fact]
    public async Task PublicClientWithPkce_SignsInAndExchangesTheCode()
    {
        var result = await _client.GetTokenAsync(Auth(), OpenBrowser, cancellationToken: Token);

        result.IsSuccess.ShouldBeTrue(result.ErrorMessage);
        result.Token!.AccessToken.ShouldBe("code|spa|none|pkce=True");
        result.Token.RefreshToken.ShouldBe(RefreshToken);
        result.Token.Scope.ShouldBe("openid profile");
        var page = await PageAsync();
        page.ShouldContain("Signed in");
        page.ShouldContain("You can close this tab and return to JetApi.");
    }

    [Fact]
    public async Task ConfidentialClientWithoutPkce_UsesTheClientSecret()
    {
        var auth = Auth(ClientId) with { ClientSecret = ClientSecret, UsePkce = false };

        var result = await _client.GetTokenAsync(auth, OpenBrowser, cancellationToken: Token);

        result.Token!.AccessToken.ShouldBe("code|app|basic|pkce=False");
    }

    [Fact]
    public async Task AuthorizationUrl_CarriesStatePkceAndTheChosenPort()
    {
        Uri? authorize = null;
        var result = await AuthorizationCodeFlow.RunAsync(
            Auth() with { AuthorizationUrl = Auth().AuthorizationUrl + "?prompt=login", Audience = "https://api.test" },
            uri =>
            {
                authorize = uri;
                return OpenBrowser(new Uri(uri.AbsoluteUri.Replace("prompt=login&", string.Empty, StringComparison.Ordinal)));
            },
            cancellationToken: Token);

        var query = Query(authorize!);
        query["prompt"].ShouldBe("login");
        query["response_type"].ShouldBe("code");
        query["client_id"].ShouldBe(PublicClientId);
        query["scope"].ShouldBe("openid profile");
        query["audience"].ShouldBe("https://api.test");
        query["state"].Length.ShouldBe(43);
        query["code_challenge_method"].ShouldBe("S256");
        query["code_challenge"].ShouldBe(AuthorizationCodeFlow.CreateCodeChallenge(result.CodeVerifier!));
        result.CodeVerifier!.Length.ShouldBe(43);

        var redirect = new Uri(query["redirect_uri"]);
        redirect.Host.ShouldBe("127.0.0.1");
        redirect.Port.ShouldBeGreaterThan(0);
        redirect.AbsolutePath.ShouldBe("/callback");
        result.RedirectUri.ShouldBe(query["redirect_uri"]);
    }

    // Expected value computed independently (Python hashlib): base64url(sha256(verifier)) without padding.
    [Fact]
    public void CodeChallenge_IsUnpaddedBase64UrlOfSha256() =>
        AuthorizationCodeFlow.CreateCodeChallenge("dBjftJeZ4CK-B9pIgbBXy7Gv2HrZDTlyxP7m0R2sL2c").ShouldBe("dXKwt__A-wmB-hYBb6FKchRS0cssM7rYVC0uG_2TCMQ");

    [Fact]
    public async Task FixedPortAndLocalhost_AreHonored()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        var result = await AuthorizationCodeFlow.RunAsync(Auth() with { RedirectUri = $"http://localhost:{port}/jetapi/cb" }, OpenBrowser, cancellationToken: Token);

        result.IsSuccess.ShouldBeTrue(result.ErrorMessage);
        result.RedirectUri.ShouldBe($"http://localhost:{port}/jetapi/cb");
    }

    [Fact]
    public async Task DeniedSignIn_ReturnsTheServerError()
    {
        var result = await _client.GetTokenAsync(Auth(DenyingClientId), OpenBrowser, cancellationToken: Token);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe("access_denied");
        result.ErrorDescription.ShouldBe("The user said no");
        result.ErrorMessage.ShouldBe("The authorization server returned 'access_denied': The user said no The sign-in was declined or the user has no access.");
        var page = await PageAsync();
        page.ShouldContain("Sign-in failed");
        page.ShouldContain("access_denied");
    }

    [Fact]
    public async Task ForgedCallback_IsRejected_AfterIgnoringOtherPathsAndIdleConnections()
    {
        var result = await AuthorizationCodeFlow.RunAsync(
            Auth(),
            async uri =>
            {
                var redirect = new Uri(Query(uri)["redirect_uri"]);

                // A speculative connection that never sends anything must not block the listener.
                using var idle = new TcpClient();
                await idle.ConnectAsync(redirect.Host, redirect.Port, Token);

                using var favicon = await _browser.GetAsync(new Uri(redirect, "/favicon.ico"), Token);
                favicon.StatusCode.ShouldBe(HttpStatusCode.NotFound);

                _page = _browser.GetAsync(new Uri(redirect.AbsoluteUri + "?code=stolen&state=forged"), Token);
            },
            cancellationToken: Token);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldBe("The sign-in response does not belong to this sign-in (state mismatch), so it was rejected. Try again.");
        (await PageAsync()).ShouldContain("state mismatch");
    }

    [Fact]
    public async Task CallbackWithoutCode_IsReported()
    {
        var result = await AuthorizationCodeFlow.RunAsync(
            Auth(),
            uri =>
            {
                var query = Query(uri);
                return OpenBrowser(new Uri($"{query["redirect_uri"]}?state={Uri.EscapeDataString(query["state"])}"));
            },
            cancellationToken: Token);

        result.ErrorMessage.ShouldBe("The sign-in response contained neither a code nor an error.");
    }

    [Fact]
    public async Task NoAnswer_TimesOut_AndCancellationIsReported()
    {
        var timedOut = await AuthorizationCodeFlow.RunAsync(Auth(), _ => Task.CompletedTask, TimeSpan.FromMilliseconds(300), Token);
        timedOut.ErrorMessage.ShouldBe("No sign-in response within 0 seconds. Try again and finish signing in in the browser.");

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var cancelled = await AuthorizationCodeFlow.RunAsync(Auth(), _ => Task.CompletedTask, cancellationToken: cancellation.Token);
        cancelled.ErrorMessage.ShouldBe("Getting the token was cancelled.");
    }

    [Fact]
    public async Task ListenerIsClosed_AfterTheFlow()
    {
        Uri? redirect = null;
        await AuthorizationCodeFlow.RunAsync(Auth(), uri =>
        {
            redirect = new Uri(Query(uri)["redirect_uri"]);
            return OpenBrowser(uri);
        }, cancellationToken: Token);
        await PageAsync();

        using var client = new TcpClient();
        await Should.ThrowAsync<SocketException>(() => client.ConnectAsync(redirect!.Host, redirect.Port, Token).AsTask());
    }

    [Theory]
    [InlineData("https://id.test/authorize", "https://app.test/callback", "'https://app.test/callback' cannot receive the sign-in response.")]
    [InlineData("https://id.test/authorize", "https://127.0.0.1:0/callback", "cannot receive the sign-in response")]
    [InlineData("https://id.test/authorize", "callback", "cannot receive the sign-in response")]
    [InlineData("{{authUrl}}", OAuth2Auth.DefaultRedirectUri, "'{{authUrl}}' is not a valid http or https authorization URL.")]
    public async Task InvalidInput_IsReportedBeforeOpeningTheBrowser(string authorizationUrl, string redirectUri, string message)
    {
        var result = await AuthorizationCodeFlow.RunAsync(
            Auth() with { AuthorizationUrl = authorizationUrl, RedirectUri = redirectUri },
            _ => throw new InvalidOperationException("The browser must not open."),
            cancellationToken: Token);

        result.ErrorMessage!.ShouldContain(message);
    }

    [Fact]
    public async Task MissingClientId_IsReported() =>
        (await AuthorizationCodeFlow.RunAsync(Auth(clientId: ""), _ => Task.CompletedTask, cancellationToken: Token)).ErrorMessage
            .ShouldBe("Client ID is required.");

    [Fact]
    public async Task PortInUse_IsReported()
    {
        using var busy = new TcpListener(IPAddress.Loopback, 0);
        busy.Start();
        var port = ((IPEndPoint)busy.LocalEndpoint).Port;

        var result = await AuthorizationCodeFlow.RunAsync(Auth() with { RedirectUri = $"http://127.0.0.1:{port}/cb" }, _ => Task.CompletedTask, cancellationToken: Token);

        result.ErrorMessage!.ShouldStartWith($"Could not wait for the sign-in response on http://127.0.0.1:{port}/cb:");
    }

    [Fact]
    public async Task Page_FollowsTheUiLanguage()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");

            (await AuthorizationCodeFlow.RunAsync(Auth(), OpenBrowser, cancellationToken: Token)).IsSuccess.ShouldBeTrue();

            var page = await PageAsync();
            page.ShouldContain("<html lang=\"tr\">");
            page.ShouldContain("Oturum açıldı");
            page.ShouldContain("Bu sekmeyi kapatıp JetApi&#39;ye dönebilirsiniz.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
