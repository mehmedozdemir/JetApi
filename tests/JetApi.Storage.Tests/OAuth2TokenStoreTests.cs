using JetApi.Core.Model;

namespace JetApi.Storage.Tests;

public sealed class OAuth2TokenStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly OAuth2Auth Dev = new(OAuth2GrantType.ClientCredentials, "https://dev.test/token", "app", "secret", "read");
    private static readonly OAuth2Auth Prod = Dev with { TokenUrl = "https://prod.test/token" };

    private readonly TempDirectory _directory = new();
    private readonly FixedTime _time = new(Now);

    public void Dispose() => _directory.Dispose();

    private OAuth2TokenStore Open() => new(_directory.Path, _time);

    [Fact]
    public void SetThenGet_PersistsAcrossInstances_PerResolvedAuth()
    {
        var token = new OAuth2Token("dev-token", "Bearer", Now.AddHours(1), "refresh", "read");
        Open().Set(Dev, token);

        var reopened = Open();

        reopened.Get(Dev).ShouldBe(token);
        reopened.Get(Prod).ShouldBeNull();
        reopened.Get(Dev with { ClientSecret = "rotated" }).ShouldBe(token);
    }

    [Fact]
    public void File_LivesInLocalState_IsIgnoredByGit_AndHoldsNoConfiguration()
    {
        Open().Set(Dev, new OAuth2Token("dev-token", ExpiresAt: Now.AddHours(1)));

        var json = _directory.Read(".jetapi/tokens.json");
        json.ShouldContain(Dev.GetTokenCacheKey());
        json.ShouldContain("dev-token");
        json.ShouldNotContain("dev.test");
        json.ShouldNotContain("secret");
        _directory.Read(".jetapi/.gitignore").ShouldContain("*");
    }

    [Fact]
    public void ExistingGitIgnore_IsNotTouched()
    {
        _directory.Write(".jetapi/.gitignore", "custom\n");

        Open().Set(Dev, new OAuth2Token("t"));

        _directory.Read(".jetapi/.gitignore").ShouldBe("custom\n");
    }

    [Fact]
    public void Write_DropsExpiredTokensWithoutRefreshToken()
    {
        var store = Open();
        store.Set(Dev, new OAuth2Token("old", ExpiresAt: Now.AddMinutes(1)));
        store.Set(Prod with { Scope = "keep" }, new OAuth2Token("refreshable", ExpiresAt: Now.AddMinutes(1), RefreshToken: "r"));
        _time.Now = Now.AddMinutes(2);

        store.Set(Prod, new OAuth2Token("new"));

        var reopened = Open();
        reopened.Get(Dev).ShouldBeNull();
        reopened.Get(Prod with { Scope = "keep" })!.AccessToken.ShouldBe("refreshable");
        reopened.Get(Prod)!.AccessToken.ShouldBe("new");
    }

    [Fact]
    public void RemoveAndClear_ForgetTokens()
    {
        var store = Open();
        store.Set(Dev, new OAuth2Token("a"));
        store.Set(Prod, new OAuth2Token("b"));

        store.Remove(Dev).ShouldBeTrue();
        store.Remove(Dev).ShouldBeFalse();
        Open().Get(Dev).ShouldBeNull();
        Open().Get(Prod).ShouldNotBeNull();

        store.Clear();
        File.Exists(_directory.Combine(".jetapi", "tokens.json")).ShouldBeFalse();
        Open().Get(Prod).ShouldBeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\": 99, \"tokens\": {}}")]
    [InlineData("{\"version\": 1, \"tokens\": {\"k\": {\"tokenType\": \"Bearer\"}}}")]
    public void DamagedOrUnknownFile_IsIgnored(string content)
    {
        _directory.Write(".jetapi/tokens.json", content);

        var store = Open();
        store.Get(Dev).ShouldBeNull();

        store.Set(Dev, new OAuth2Token("fresh"));
        Open().Get(Dev)!.AccessToken.ShouldBe("fresh");
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
