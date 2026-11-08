using JetApi.Core.Model;
using JetApi.Desktop.ViewModels;
using JetApi.Http;

namespace JetApi.Desktop.Tests;

public sealed class CurlTests
{
    private const string Command = """
        curl 'https://api.example.com/users?page=2' \
          -X POST \
          -H 'Content-Type: application/json' \
          -H 'X-Trace: 1' \
          --data-raw '{"name":"Ayşe"}'
        """;

    private static async Task<(TempWorkspace Workspace, WorkspaceViewModel Vm, FakeDialogs Dialogs)> SetupAsync()
    {
        var workspace = await TempWorkspace.CreateAsync();
        var dialogs = new FakeDialogs();
        return (workspace, workspace.CreateViewModel(dialogs), dialogs);
    }

    [Fact]
    public async Task PastingCurl_ReplacesTheRequest_KeepsItsName_AndMarksTheTabUnsaved()
    {
        var (workspace, vm, _) = await SetupAsync();
        using (workspace)
        using (vm)
        {
            vm.OpenNode(vm.Collections[0].Children[0]);
            var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
            var name = tab.Name;
            tab.ImportCurlCommand.CanExecute("https://example.com").ShouldBeFalse();
            tab.ImportCurlCommand.CanExecute(Command).ShouldBeTrue();

            await tab.ImportCurlCommand.ExecuteAsync(Command);

            var replaced = vm.SelectedTab.ShouldBeOfType<HttpRequestTabViewModel>();
            replaced.ShouldNotBeSameAs(tab);
            vm.Tabs.Count.ShouldBe(1);
            replaced.Name.ShouldBe(name);
            replaced.Method.ShouldBe("POST");
            replaced.Url.ShouldBe("https://api.example.com/users");
            replaced.QueryParameters.ToEntries().ShouldBe([new KeyValueEntry("page", "2")]);
            replaced.Headers.ToEntries().ShouldContain(new KeyValueEntry("X-Trace", "1"));
            replaced.SelectedBodyMode.Value.ShouldBe(HttpBodyMode.Json);
            replaced.BodyText.ShouldBe("""{"name":"Ayşe"}""");
            replaced.IsDirty.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task BrokenCurl_IsReportedInTheResponsePane()
    {
        var (workspace, vm, _) = await SetupAsync();
        using (workspace)
        using (vm)
        {
            vm.OpenNode(vm.Collections[0].Children[0]);
            var tab = (HttpRequestTabViewModel)vm.SelectedTab!;

            await tab.ImportCurlCommand.ExecuteAsync("curl 'https://example.com");

            vm.SelectedTab.ShouldBeSameAs(tab);
            tab.Response.PreparationErrors.ShouldHaveSingleItem().ShouldStartWith("The cURL command could not be imported");
        }
    }

    [Fact]
    public async Task NewRequestFromClipboard_CreatesASavedRequest()
    {
        var (workspace, vm, dialogs) = await SetupAsync();
        using (workspace)
        using (vm)
        {
            dialogs.ClipboardText = "curl --digest -u ayse:pw https://api.example.com/x";
            dialogs.Prompts.Enqueue("From curl");

            await vm.NewRequestFromCurlCommand.ExecuteAsync(null);

            var tab = vm.SelectedTab.ShouldBeOfType<HttpRequestTabViewModel>();
            tab.Name.ShouldBe("From curl");
            tab.Url.ShouldBe("https://api.example.com/x");
            tab.IsDirty.ShouldBeFalse();
            // Digest auth has no equivalent: the report says so.
            dialogs.ImportReports.ShouldHaveSingleItem().Files[0].Issues.ShouldContain(i => i.IsWarning);
        }
    }

    [Fact]
    public async Task NewRequestFromClipboard_WithoutCurl_Explains()
    {
        var (workspace, vm, dialogs) = await SetupAsync();
        using (workspace)
        using (vm)
        {
            dialogs.ClipboardText = "hello";

            await vm.NewRequestFromCurlCommand.ExecuteAsync(null);

            dialogs.Errors.ShouldHaveSingleItem().ShouldContain("cURL");
        }
    }

    [Fact]
    public async Task CodeSnippet_ResolvesVariables_SwitchesLanguage_AndCopies()
    {
        var (workspace, vm, dialogs) = await SetupAsync();
        using (workspace)
        using (vm)
        {
            vm.OpenNode(vm.Collections[0].Children[0]);
            var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
            tab.Url = "https://api.example.com/users";

            await tab.ShowCodeSnippetCommand.ExecuteAsync(null);

            var snippet = dialogs.Snippets.ShouldHaveSingleItem();
            snippet.SelectedLanguage = snippet.Languages.Single(l => l.Value == SnippetLanguage.Python);
            snippet.Code.ShouldContain("requests.request(");
            snippet.Code.ShouldContain("https://api.example.com/users");
            snippet.EditorLanguage.ShouldBe("python");
            await snippet.CopyCommand.ExecuteAsync(null);
            dialogs.Clipboard.ShouldBe(snippet.Code);
            snippet.Copied.ShouldBeTrue();
            snippet.SelectedLanguage = snippet.Languages.Single(l => l.Value == SnippetLanguage.CSharp);
            snippet.Copied.ShouldBeFalse();

            await tab.CopyAsCurlCommand.ExecuteAsync(null);
            dialogs.Clipboard.ShouldStartWith("curl");
        }
    }
}
