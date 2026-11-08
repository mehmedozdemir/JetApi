using JetApi.Core.Model;
using JetApi.Core.Variables;
using Microsoft.Extensions.Time.Testing;

namespace JetApi.Core.Tests.Model;

public sealed class OAuth2Tests
{
    private static readonly OAuth2Auth Template = new(
        OAuth2GrantType.Password,
        TokenUrl: " {{idp}}/token ",
        ClientId: "{{clientId}}",
        ClientSecret: "{{secret}}",
        Scope: "{{scope}}",
        Audience: "{{idp}}/api",
        AuthorizationUrl: "{{idp}}/authorize",
        Username: "{{user}}",
        Password: "{{password}}",
        RedirectUri: "http://127.0.0.1:{{port}}/cb");

    private static VariableResolver Environment(string idp, string user = "ayşe") => new(
    [
        new VariableLayer(VariableSource.Environment,
        [
            new Variable("idp", idp),
            new Variable("clientId", "app"),
            new Variable("secret", "s"),
            new Variable("scope", "read"),
            new Variable("user", user),
            new Variable("password", "p"),
            new Variable("port", "7000"),
        ]),
    ]);

    private static OAuth2Auth Resolve(VariableResolver variables) => Template.Resolve(t => variables.Resolve(t).Value);

    [Fact]
    public void Defaults_AreClientCredentialsPkceAndBasicHeaderOnAnyLoopbackPort()
    {
        var auth = new OAuth2Auth();

        (auth.GrantType, auth.UsePkce, auth.ClientAuthentication, auth.RedirectUri)
            .ShouldBe((OAuth2GrantType.ClientCredentials, true, OAuth2ClientAuthentication.BasicHeader, "http://127.0.0.1:0/callback"));
    }

    [Fact]
    public void Resolve_ResolvesEveryString_AndTrimsUrls()
    {
        Resolve(Environment("https://id.test")).ShouldBe(Template with
        {
            TokenUrl = "https://id.test/token",
            ClientId = "app",
            ClientSecret = "s",
            Scope = "read",
            Audience = "https://id.test/api",
            AuthorizationUrl = "https://id.test/authorize",
            Username = "ayşe",
            Password = "p",
            RedirectUri = "http://127.0.0.1:7000/cb",
        });
    }

    [Fact]
    public void TokenCacheKey_SeparatesEnvironmentsAndUsers_ButNotSecrets()
    {
        var dev = Resolve(Environment("https://dev.test"));

        dev.GetTokenCacheKey().ShouldBe(Resolve(Environment("https://dev.test")).GetTokenCacheKey());
        dev.GetTokenCacheKey().ShouldMatch("^[0-9a-f]{64}$");
        dev.GetTokenCacheKey().ShouldNotBe(Resolve(Environment("https://prod.test")).GetTokenCacheKey());
        dev.GetTokenCacheKey().ShouldNotBe(Resolve(Environment("https://dev.test", user: "mehmet")).GetTokenCacheKey());
        dev.GetTokenCacheKey().ShouldNotBe((dev with { Scope = "write" }).GetTokenCacheKey());
        dev.GetTokenCacheKey().ShouldNotBe((dev with { GrantType = OAuth2GrantType.AuthorizationCode }).GetTokenCacheKey());
        dev.GetTokenCacheKey().ShouldBe((dev with { ClientSecret = "rotated", Password = "changed", UsePkce = false }).GetTokenCacheKey());
    }

    [Fact]
    public void TokenCacheKey_IgnoresUserName_ForGrantsWithoutOne()
    {
        var auth = new OAuth2Auth(OAuth2GrantType.ClientCredentials, "https://id.test/token", "app");

        auth.GetTokenCacheKey().ShouldBe((auth with { Username = "left over" }).GetTokenCacheKey());
    }

    [Fact]
    public void IsExpired_UsesTheClockAndSkew()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        var token = new OAuth2Token("t", ExpiresAt: time.GetUtcNow().AddMinutes(1));

        token.IsExpired(time).ShouldBeFalse();
        token.IsExpired(time, TimeSpan.FromMinutes(2)).ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(31));
        token.IsExpired(time).ShouldBeTrue();
        token.IsExpired(time, TimeSpan.Zero).ShouldBeFalse();
        new OAuth2Token("forever").IsExpired(time).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Bearer", "Bearer abc")]
    [InlineData("bearer", "Bearer abc")]
    [InlineData("", "Bearer abc")]
    [InlineData("MAC", "MAC abc")]
    public void AuthorizationHeaderValue_NormalizesBearer(string type, string expected) =>
        new OAuth2Token("abc", type).AuthorizationHeaderValue.ShouldBe(expected);
}
