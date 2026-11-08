using System.Text;
using JetApi.Core.Model;
using JetApi.Core.Variables;

namespace JetApi.Http.Tests;

public sealed class HttpRequestPreparerTests
{
    private static readonly VariableResolver Variables = new(
    [
        new VariableLayer(VariableSource.Environment,
        [
            new Variable("baseUrl", "https://api.test"),
            new Variable("token", "secret"),
            new Variable("name", "Ayşe Yılmaz"),
        ]),
    ]);

    private static HttpPreparationResult Prepare(HttpRequestDefinition request, AuthConfig? auth = null) =>
        HttpRequestPreparer.Prepare(request, auth ?? AuthConfig.None, Variables, Path.GetTempPath());

    private static HttpRequestDefinition Request(string url = "{{baseUrl}}/users") => new() { Key = "r", Name = "r", Url = url };

    [Fact]
    public void Prepare_ResolvesUrlAndAppendsEncodedEnabledParameters()
    {
        var result = Prepare(Request() with
        {
            QueryParameters = [new("q", "{{name}}"), new("disabled", "x", IsEnabled: false), new("a&b", "1=2")],
        });

        result.Request!.Url.AbsoluteUri.ShouldBe("https://api.test/users?q=Ay%C5%9Fe%20Y%C4%B1lmaz&a%26b=1%3D2");
    }

    [Theory]
    [InlineData("https://api.test/users?x=1", "https://api.test/users?x=1&p=v")]
    [InlineData("https://api.test/users?", "https://api.test/users?p=v")]
    [InlineData("https://api.test/users#top", "https://api.test/users?p=v#top")]
    [InlineData("localhost:8080/users", "http://localhost:8080/users?p=v")]
    public void Prepare_MergesParametersIntoExistingQuery(string url, string expected) =>
        Prepare(Request(url) with { QueryParameters = [new("p", "v")] }).Request!.Url.AbsoluteUri.ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("ftp://files.test/x")]
    [InlineData("http://")]
    public void Prepare_RejectsInvalidUrls(string url)
    {
        var result = Prepare(Request(url));

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem();
    }

    [Fact]
    public void Prepare_RejectsInvalidMethod() =>
        Prepare(Request() with { Method = "GET USERS" }).Errors.ShouldHaveSingleItem().ShouldContain("not a valid HTTP method");

    [Fact]
    public void Prepare_ReportsUnresolvedVariables_ButStillPrepares()
    {
        var result = Prepare(Request("{{baseUrl}}/{{missing}}"));

        result.IsSuccess.ShouldBeTrue();
        result.VariableIssues.ShouldBe([new VariableIssue("missing", VariableIssueKind.Undefined)]);
    }

    [Fact]
    public void Prepare_AppliesBearerAuth() =>
        Prepare(Request(), new BearerAuth("{{token}}")).Request!.Headers.ShouldContain(new KeyValuePair<string, string>("Authorization", "Bearer secret"));

    [Fact]
    public void Prepare_AppliesBasicAuth_WithUtf8Credentials()
    {
        var header = Prepare(Request(), new BasicAuth("ayşe", "{{token}}")).Request!.Headers.Single(h => h.Key == "Authorization");

        header.Value.ShouldBe("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ayşe:secret")));
    }

    [Fact]
    public void Prepare_OAuth2_AppliesTheTokenOfTheResolvedAuth()
    {
        var auth = new OAuth2Auth(OAuth2GrantType.ClientCredentials, "{{baseUrl}}/token", "{{name}}", "{{token}}");
        OAuth2Auth? asked = null;

        var result = HttpRequestPreparer.Prepare(Request(), auth, Variables, Path.GetTempPath(), resolved =>
        {
            asked = resolved;
            return new OAuth2Token("access-1", "bearer");
        });

        result.Request!.Headers.ShouldContain(new KeyValuePair<string, string>("Authorization", "Bearer access-1"));
        asked.ShouldBe(auth with { TokenUrl = "https://api.test/token", ClientId = "Ayşe Yılmaz", ClientSecret = "secret" });
    }

    [Fact]
    public void Prepare_OAuth2_UsesTheTokenType()
    {
        var auth = new OAuth2Auth(OAuth2GrantType.ClientCredentials, "https://id.test/token", "app");

        HttpRequestPreparer.Prepare(Request(), auth, Variables, Path.GetTempPath(), _ => new OAuth2Token("x", "DPoP"))
            .Request!.Headers.ShouldContain(new KeyValuePair<string, string>("Authorization", "DPoP x"));
    }

    [Fact]
    public void Prepare_OAuth2WithoutToken_AsksTheUserToGetOne()
    {
        var auth = new OAuth2Auth(OAuth2GrantType.ClientCredentials, "https://id.test/token", "app");

        var withoutProvider = Prepare(Request(), auth);
        var withoutToken = HttpRequestPreparer.Prepare(Request(), auth, Variables, Path.GetTempPath(), _ => null);
        var emptyToken = HttpRequestPreparer.Prepare(Request(), auth, Variables, Path.GetTempPath(), _ => new OAuth2Token(""));

        foreach (var result in new[] { withoutProvider, withoutToken, emptyToken })
        {
            result.IsSuccess.ShouldBeFalse();
            result.Errors.ShouldBe(["There is no OAuth 2.0 access token yet. Get a new access token in the Auth tab, then send again."]);
        }
    }

    [Fact]
    public void Prepare_OAuth2_ExplicitAuthorizationHeaderWins_WithoutAskingForAToken()
    {
        var request = Request() with { Headers = [new("Authorization", "Custom x")] };

        var result = HttpRequestPreparer.Prepare(request, new OAuth2Auth(), Variables, Path.GetTempPath(), _ => throw new InvalidOperationException("not asked"));

        result.Request!.Headers.ShouldBe([new("Authorization", "Custom x")]);
    }

    [Fact]
    public void Prepare_ExplicitAuthorizationHeader_WinsOverAuth()
    {
        var request = Request() with { Headers = [new("authorization", "Custom x")] };

        Prepare(request, new BearerAuth("t")).Request!.Headers.ShouldBe([new("authorization", "Custom x")]);
    }

    [Fact]
    public void Prepare_ApiKeyInQuery()
    {
        var result = Prepare(Request(), new ApiKeyAuth("api_key", "{{token}}", ApiKeyLocation.Query));

        result.Request!.Url.Query.ShouldBe("?api_key=secret");
        result.Request.Headers.ShouldBeEmpty();
    }

    [Fact]
    public void Prepare_JsonBody_IsUtf8WithoutCharsetParameter()
    {
        var body = Prepare(Request() with { Body = HttpBody.Json("{\"name\":\"{{name}}\"}") }).Request!.Body.ShouldBeOfType<BytesBody>();

        body.ContentType.ShouldBe("application/json");
        Encoding.UTF8.GetString(body.Content.Span).ShouldBe("{\"name\":\"Ayşe Yılmaz\"}");
    }

    [Fact]
    public void Prepare_MissingBodyFile_IsAnError()
    {
        var request = Request() with { Body = new HttpBody { Mode = HttpBodyMode.File, FilePath = "does-not-exist.bin" } };

        Prepare(request).Errors.ShouldHaveSingleItem().ShouldContain("does not exist");
    }

    [Fact]
    public void Prepare_SkipsDisabledHeadersAndEmptyNames()
    {
        var request = Request() with { Headers = [new("A", "1"), new("B", "2", IsEnabled: false), new("", "x")] };

        Prepare(request).Request!.Headers.ShouldBe([new("A", "1")]);
    }
}
