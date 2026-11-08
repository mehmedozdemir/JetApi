using JetApi.Desktop.Controls;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

public sealed class MakeoverTests
{
    [Theory]
    [InlineData("[::ffff:127.0.0.1]:52707", "127.0.0.1:52707")]
    [InlineData("10.0.0.5:443", "10.0.0.5:443")]
    [InlineData("[2001:db8::1]:8080", "[2001:db8::1]:8080")]
    [InlineData("not an address", "not an address")]
    [InlineData(null, "")]
    public void EndPoint_ShowsMappedIpv4AsPlainIpv4(string? input, string expected) =>
        Formatting.EndPoint(input).ShouldBe(expected);

    [Fact]
    public void ShortenHome_ReplacesTheHomePrefixOnly()
    {
        var home = Path.Combine(Path.GetTempPath(), "home-user");
        var sep = Path.DirectorySeparatorChar;

        WorkspaceViewModel.ShortenHome(Path.Combine(home, "repos", "api"), home).ShouldBe($"~{sep}repos{sep}api");
        WorkspaceViewModel.ShortenHome(home, home).ShouldBe("~");
        WorkspaceViewModel.ShortenHome(home + "-other" + sep + "x", home).ShouldBe(home + "-other" + sep + "x");
        WorkspaceViewModel.ShortenHome("/srv/api", string.Empty).ShouldBe("/srv/api");
    }

    [Theory]
    [InlineData("POST", "Method.POST")]
    [InlineData(" post ", "Method.POST")]
    [InlineData("gRPC", "Method.gRPC")]
    [InlineData("DEL", "Method.DELETE")]
    [InlineData("PURGE", "Method.Other")]
    [InlineData(null, "Method.Other")]
    public void MethodColor_UsesTheMethodToken(string? method, string expected) =>
        MethodBadge.ResourceKey(method).ShouldBe(expected);

    [Fact]
    public void DescriptionHint_ShowsOnlyOnTheEmptyRow()
    {
        var row = new KeyValueRowViewModel();
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.DescriptionHint.ShouldNotBeEmpty();
        row.Key = "X-Trace-Id";

        row.DescriptionHint.ShouldBeEmpty();
        changed.ShouldContain(nameof(KeyValueRowViewModel.DescriptionHint));
        changed.ShouldContain(nameof(KeyValueRowViewModel.IsPlaceholder));
    }
}
