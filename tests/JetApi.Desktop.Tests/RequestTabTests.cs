using System.Text.Json;
using JetApi.Core.Model;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

public sealed class RequestTabTests(EchoServer server) : IClassFixture<EchoServer>
{
    private static async Task<(TempWorkspace Workspace, WorkspaceViewModel Vm, FakeDialogs Dialogs)> SetupAsync(string baseUrl)
    {
        var workspace = await TempWorkspace.CreateAsync();
        await workspace.Session.SaveEnvironmentAsync(new ApiEnvironment { Key = "dev", Name = "Development", Variables = [new("baseUrl", baseUrl.TrimEnd('/')), new("token", "s3cret", IsSecret: true)] });
        var collection = workspace.Session.Workspace.Collections[0];
        await workspace.Session.SaveCollectionAsync(collection with { Auth = new BearerAuth("{{token}}") });
        var dialogs = new FakeDialogs();
        return (workspace, workspace.CreateViewModel(dialogs), dialogs);
    }

    [Fact]
    public async Task Send_ShowsResponse_InheritedAuth_AndRecordsHistory()
    {
        var (workspace, vm, _) = await SetupAsync(server.BaseUrl);
        using (workspace)
        using (vm)
        {
            vm.OpenNode(vm.Collections[0].Children[0]);
            var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
            tab.Url = "{{baseUrl}}/echo";

            await tab.SendAsync();

            tab.Response.Tone.ShouldBe(StatusTone.Success);
            tab.Response.StatusText.ShouldBe("200 OK");
            using var json = JsonDocument.Parse(tab.Response.DisplayedBody);
            json.RootElement.GetProperty("authorization").GetString().ShouldBe("Bearer s3cret");
            json.RootElement.GetProperty("query").GetString().ShouldBe("?hello=world");
            tab.Response.DisplayedBody.ShouldContain("\n  ");
            tab.Response.Timings[^1].IsTotal.ShouldBeTrue();
            tab.Response.SentText.ShouldContain("Authorization: Bearer ••••••••");
            tab.Response.SentText.ShouldContain("# automatic");
            tab.Response.SentText.ShouldNotContain("s3cret");
            tab.Response.RevealSecrets = true;
            tab.Response.SentText.ShouldContain("s3cret");

            vm.HistoryItems.ShouldHaveSingleItem().Meta.ShouldStartWith("200 OK");
            File.Exists(Path.Combine(workspace.Root, ".jetapi", "history.jsonl")).ShouldBeTrue();
            File.ReadAllText(Path.Combine(workspace.Root, ".jetapi", "history.jsonl")).ShouldNotContain("s3cret");
        }
    }

    [Fact]
    public async Task UndefinedVariables_AreConfirmedBeforeSending()
    {
        var (workspace, vm, dialogs) = await SetupAsync(server.BaseUrl);
        using (workspace)
        using (vm)
        {
            vm.OpenNode(vm.Collections[0].Children[0]);
            var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
            tab.Url = "{{baseUrl}}/echo?x={{missing}}";
            dialogs.UndefinedChoice = UndefinedVariablesChoice.Cancel;

            await tab.SendAsync();

            dialogs.UndefinedPrompts.ShouldHaveSingleItem().ShouldHaveSingleItem().Name.ShouldBe("missing");
            tab.Response.IsEmpty.ShouldBeTrue();

            dialogs.UndefinedChoice = UndefinedVariablesChoice.OpenEnvironments;
            await tab.SendAsync();
            vm.Environments.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task InvalidRequest_ShowsPreparationErrors()
    {
        var (workspace, vm, _) = await SetupAsync(server.BaseUrl);
        using (workspace)
        using (vm)
        {
            vm.OpenNode(vm.Collections[0].Children[0]);
            var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
            tab.Url = "ftp://nope";

            await tab.SendAsync();

            tab.Response.HasPreparationErrors.ShouldBeTrue();
            tab.Response.StatusText.ShouldBe("Not sent");
        }
    }

    [Fact]
    public async Task AutoHeaders_ShowInheritedAuthAndBodyType_UnlessOverridden()
    {
        var (workspace, vm, _) = await SetupAsync(server.BaseUrl);
        using (workspace)
        using (vm)
        {
            vm.OpenNode(vm.Collections[0].Children[1]);
            var tab = (HttpRequestTabViewModel)vm.SelectedTab!;

            tab.AutoHeaders.Select(h => h.Name).ShouldBe(["Authorization", "Content-Type", "User-Agent", "Accept", "Accept-Encoding"]);
            tab.AutoHeaders[0].Value.ShouldContain("Examples");
            tab.Auth.InheritedDescription.ShouldBe("Uses the auth of “Examples”: Bearer token.");

            tab.Headers.Rows[^1].Key = "authorization";
            tab.Headers.Rows[0].Value = "Custom";
            tab.AutoHeaders.Select(h => h.Name).ShouldNotContain("Authorization");
        }
    }

    [Fact]
    public async Task FormatBody_IndentsJson_OrReportsInvalidJson()
    {
        var (workspace, vm, _) = await SetupAsync(server.BaseUrl);
        using (workspace)
        using (vm)
        {
            vm.OpenNode(vm.Collections[0].Children[1]);
            var tab = (HttpRequestTabViewModel)vm.SelectedTab!;

            tab.BodyText = "{\"a\":[1,2]}";
            tab.FormatBodyCommand.Execute(null);
            tab.BodyText.ShouldBe("{\n  \"a\": [\n    1,\n    2\n  ]\n}".Replace("\n", Environment.NewLine, StringComparison.Ordinal));

            tab.BodyText = "{ nope";
            tab.FormatBodyCommand.Execute(null);
            tab.BodyError.ShouldBe("The body is not valid JSON.");
        }
    }
}
