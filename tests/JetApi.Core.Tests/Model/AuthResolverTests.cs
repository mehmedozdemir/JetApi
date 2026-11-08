using JetApi.Core.Model;

namespace JetApi.Core.Tests.Model;

public sealed class AuthResolverTests
{
    private static readonly ApiCollection Collection = new() { Key = "c", Name = "C", Auth = new BearerAuth("collection") };

    [Fact]
    public void Resolve_ReturnsRequestAuth_WhenExplicit()
    {
        var request = new HttpRequestDefinition { Key = "r", Name = "R", Auth = new BasicAuth("u", "p") };

        AuthResolver.Resolve(request, [], Collection).ShouldBe(new BasicAuth("u", "p"));
    }

    [Fact]
    public void Resolve_ReturnsClosestFolderAuth_WhenRequestInherits()
    {
        var request = new HttpRequestDefinition { Key = "r", Name = "R" };
        Folder[] ancestors =
        [
            new() { Key = "inner", Name = "Inner" },
            new() { Key = "outer", Name = "Outer", Auth = new BearerAuth("folder") },
        ];

        AuthResolver.Resolve(request, ancestors, Collection).ShouldBe(new BearerAuth("folder"));
    }

    [Fact]
    public void Resolve_FallsBackToCollection()
    {
        var request = new HttpRequestDefinition { Key = "r", Name = "R" };

        AuthResolver.Resolve(request, [], Collection).ShouldBe(new BearerAuth("collection"));
    }

    [Fact]
    public void Resolve_ExplicitNone_StopsInheritance()
    {
        var request = new HttpRequestDefinition { Key = "r", Name = "R", Auth = AuthConfig.None };

        AuthResolver.Resolve(request, [], Collection).ShouldBeOfType<NoAuth>();
    }
}
