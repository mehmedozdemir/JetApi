using System.Text.Json;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

public sealed class CookieJarTests(EchoServer server) : IClassFixture<EchoServer>
{
    private static string CookieSent(HttpRequestTabViewModel tab)
    {
        using var json = JsonDocument.Parse(tab.Response.DisplayedBody);
        return json.RootElement.GetProperty("cookie").GetString()!;
    }

    private async Task<HttpRequestTabViewModel> SendAsync(WorkspaceViewModel vm, string path)
    {
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
        tab.Url = server.BaseUrl.TrimEnd('/') + path;
        await tab.SendAsync();
        return tab;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CookiesSetByTheServer_AreSentBack_OnlyWhenTheJarIsOn(bool useJar)
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var runner = new RequestRunner(new ProtoSchemaCache()) { UseCookieJar = useJar };
        using var vm = workspace.CreateViewModel(new FakeDialogs(), runner);
        vm.OpenNode(vm.Collections[0].Children[0]);
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
        tab.Method = "GET";

        await SendAsync(vm, "/login");
        await SendAsync(vm, "/echo");

        CookieSent(tab).ShouldBe(useJar ? "session=abc123" : string.Empty);
        workspace.Session.Cookies.Count.ShouldBe(useJar ? 1 : 0);
        if (useJar)
        {
            tab.Response.SentText.ShouldContain("Cookie: session=abc123");
        }
    }

    [Fact]
    public async Task ACookieHeaderOnTheRequest_WinsOverTheJar_AndClearingEmptiesIt()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var runner = new RequestRunner(new ProtoSchemaCache()) { UseCookieJar = true };
        using var vm = workspace.CreateViewModel(new FakeDialogs(), runner);
        vm.OpenNode(vm.Collections[0].Children[0]);
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
        tab.Method = "GET";
        await SendAsync(vm, "/login");

        tab.Headers.Rows[^1].Key = "Cookie";
        tab.Headers.Rows[0].Value = "mine=1";
        await SendAsync(vm, "/echo");
        CookieSent(tab).ShouldBe("mine=1");

        tab.Headers.Rows[0].IsEnabled = false;
        workspace.Session.ClearCookies();
        await SendAsync(vm, "/echo");
        CookieSent(tab).ShouldBe(string.Empty);
    }
}
