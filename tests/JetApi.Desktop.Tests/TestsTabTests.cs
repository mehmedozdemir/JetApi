using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetApi.Core.Model;
using JetApi.Desktop.Controls;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;
using JetApi.Testing.JsonPath;

namespace JetApi.Desktop.Tests;

/// <summary>Checks and extractions in request tabs (M3, phase B).</summary>
public sealed class TestsTabTests(EchoServer server) : IClassFixture<EchoServer>
{
    private async Task<(TempWorkspace Workspace, WorkspaceViewModel Vm, HttpRequestTabViewModel Tab)> OpenAsync()
    {
        var workspace = await TempWorkspace.CreateAsync();
        var vm = workspace.CreateViewModel(new FakeDialogs());
        vm.OpenNode(vm.Collections[0].Children[0]);
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;
        tab.Method = "GET";
        tab.Url = server.BaseUrl.TrimEnd('/') + "/echo";
        tab.QueryParameters.Rows.Clear();
        return (workspace, vm, tab);
    }

    [Fact]
    public async Task KeptPostmanScripts_AreShown_AndSurviveSaving()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var session = workspace.Session;
        var collection = session.Workspace.Collections[0];
        var original = collection.Children[0];
        var scripts = new ScriptSet("pm.variables.set('ts', Date.now());", "console.log(pm.response.json());");
        await session.SaveNodeAsync(collection.Key, [original.Key], original with { Scripts = scripts }, TestContext.Current.CancellationToken);
        using var vm = workspace.CreateViewModel(new FakeDialogs());
        vm.OpenNode(vm.Collections[0].Children[0]);
        var tab = (HttpRequestTabViewModel)vm.SelectedTab!;

        tab.Tests.HasScripts.ShouldBeTrue();
        tab.Tests.ShowScripts.ShouldBeFalse();
        tab.Tests.ToggleScriptsCommand.Execute(null);
        tab.Tests.ShowScripts.ShouldBeTrue();
        tab.Tests.ScriptsText.ShouldBe("// Pre-request script (Postman)\npm.variables.set('ts', Date.now());\n\n// Test script (Postman)\nconsole.log(pm.response.json());");

        tab.Url += "?changed=1";
        await tab.SaveAsync();

        session.Workspace.Collections[0].Children[0].Scripts.ShouldBe(scripts);
        var reloaded = await JetApi.Storage.WorkspaceStore.LoadAsync(workspace.Root, TestContext.Current.CancellationToken);
        reloaded.Workspace.Collections[0].Children[0].Scripts.ShouldBe(scripts);
    }

    [Fact]
    public async Task Sending_RunsTheChecks_AndShowsTheResults()
    {
        var (workspace, vm, tab) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            tab.Tests.Add(new ResponseCheck(CheckSource.Status, CheckOperator.Equals, Expected: "200"));
            tab.Tests.Add(new ResponseCheck(CheckSource.Json, CheckOperator.Equals, "$.method", "POST"));
            tab.Tests.Add(new ResponseCheck(CheckSource.Header, CheckOperator.Contains, "content-type", "json"));
            tab.Tests.Add(new ResponseCheck(CheckSource.Time, CheckOperator.LessThan, Expected: "500", IsEnabled: false));

            await tab.SendAsync();

            tab.Tests.Checks.Select(c => c.Outcome).ShouldBe([TestOutcome.Passed, TestOutcome.Failed, TestOutcome.Passed, TestOutcome.None]);
            tab.Tests.Checks[1].ResultText.ShouldBe("Expected POST, got GET.");
            tab.Tests.Badge.ShouldBe("2/3");
            tab.Tests.HasFailures.ShouldBeTrue();
            var run = tab.TestRun.ShouldNotBeNull();
            run.Summary.ShouldBe("2 passed · 1 failed");
            run.Checks[0].ShouldBe(new TestResultLine(false, "$.method equals POST", "Expected POST, got GET."));
            tab.TestRunBadge.ShouldBe("2/3");
            tab.TestRunFailed.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Extractions_StoreRuntimeAndEnvironmentValues_AndAreUsedNext()
    {
        var (workspace, vm, tab) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            tab.Tests.Add(new Extraction(ExtractionSource.Json, "$.method", "lastMethod"));
            tab.Tests.Add(new Extraction(ExtractionSource.Status, string.Empty, "lastStatus", ExtractionTarget.Environment));
            tab.Tests.Add(new Extraction(ExtractionSource.Json, "$.missing", "nothing"));

            await tab.SendAsync();

            workspace.Session.RuntimeVariables["lastMethod"].ShouldBe("GET");
            workspace.Session.ActiveEnvironment!.Variables.ShouldContain(new Variable("lastStatus", "200"));
            tab.Tests.Extractions.Select(e => e.Outcome).ShouldBe([TestOutcome.Passed, TestOutcome.Passed, TestOutcome.Failed]);
            tab.TestRun!.Extractions[2].Detail.ShouldBe("Nothing found at $.missing. The variable was not changed.");
            tab.TestRun.HasChecks.ShouldBeFalse();

            // The runtime value takes part in the next request and in the hover hints.
            tab.Url = server.BaseUrl.TrimEnd('/') + "/echo?m={{lastMethod}}";
            tab.Variables.Describe("lastMethod").Value.ShouldBe("GET");
            await tab.SendAsync();
            tab.Response.DisplayedBody.ShouldContain("?m=GET");

            workspace.Session.ClearRuntimeVariables();
            tab.Variables.Describe("lastMethod").IsDefined.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task ChecksAreSavedWithTheRequest_AndEditingMarksItUnsaved()
    {
        var (workspace, vm, tab) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            await tab.SaveAsync();
            tab.IsDirty.ShouldBeFalse();

            tab.Tests.AddCheckCommand.Execute(null);
            tab.IsDirty.ShouldBeTrue();
            tab.Tests.Checks[0].Expected = "201";
            tab.Tests.AddExtractionCommand.Execute(null);
            tab.Tests.Extractions[0].Target = "$.id";
            tab.Tests.Extractions[0].Variable = "userId";
            await tab.SaveAsync();

            var file = workspace.Read("collections/examples/get-example.http.yaml");
            file.ShouldContain("tests:\n- check: status\n  op: equals\n  value: 201");
            file.ShouldContain("extract:\n- from: json\n  path: $.id\n  variable: userId");
            tab.IsDirty.ShouldBeFalse();

            tab.Tests.Checks[0].RemoveCommand.Execute(null);
            tab.IsDirty.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task EditingARow_ClearsItsOldResult_AndOperatorsFollowTheSource()
    {
        var (workspace, vm, tab) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            tab.Tests.Add(new ResponseCheck(CheckSource.Status, CheckOperator.Equals, Expected: "200"));
            await tab.SendAsync();
            var row = tab.Tests.Checks[0];
            row.IsPassed.ShouldBeTrue();

            row.SelectedSource = row.Sources.Single(s => s.Value == CheckSource.Time);

            row.Outcome.ShouldBe(TestOutcome.None);
            row.Operators.Select(o => o.Value).ShouldBe([CheckOperator.LessThan, CheckOperator.GreaterThan]);
            row.SelectedOperator!.Value.ShouldBe(CheckOperator.LessThan);
            row.SelectedSource = row.Sources.Single(s => s.Value == CheckSource.Schema);
            row.HasOperator.ShouldBeFalse();
            row.IsSchema.ShouldBeTrue();
            row.HasInlineExpected.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task AValuePickedInTheResponse_BecomesACheckOrAnExtraction()
    {
        var (workspace, vm, tab) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            tab.AddCheckFromResponseCommand.Execute(new JsonPathHit("$.user.email", "a@b.c", "email"));
            tab.AddCheckFromResponseCommand.Execute(new JsonPathHit("$.user", null, "user"));
            tab.AddExtractionFromResponseCommand.Execute(new JsonPathHit("$['zip code']", "35000", "zip code"));

            tab.Section.ShouldBe(RequestSection.Tests);
            tab.Tests.ToChecks().ShouldBe([new ResponseCheck(CheckSource.Json, CheckOperator.Equals, "$.user.email", "a@b.c"), new ResponseCheck(CheckSource.Json, CheckOperator.Exists, "$.user")]);
            tab.Tests.ToExtractions().ShouldBe([new Extraction(ExtractionSource.Json, "$['zip code']", "zip_code")]);
        }
    }

    [Fact]
    public Task TestsTab_ResultsTab_AndRightClick_InTheWindow() =>
        HeadlessApp.Session.Dispatch(() => WalkAsync(server.BaseUrl), TestContext.Current.CancellationToken);

    private static async Task<bool> WalkAsync(string baseUrl)
    {
        HeadlessApp.Log.Clear();
        using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
        var settingsFile = Path.Combine(workspace.Root, ".jetapi", "settings.json");
        using var vm = new MainWindowViewModel(new AppSettingsStore(settingsFile), new FakeDialogs(), theme => ((App)Application.Current!).ApplyTheme(theme), action => Dispatcher.UIThread.Post(action),
            settings => ((App)Application.Current!).ApplyAppearance(settings));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
        var ws = vm.Workspace!;
        ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "Create user"));
        var tab = (HttpRequestTabViewModel)ws.SelectedTab!;
        tab.Url = baseUrl.TrimEnd('/') + "/echo";
        tab.Tests.Add(new ResponseCheck(CheckSource.Status, CheckOperator.Equals, Expected: "201"));
        tab.Tests.Add(new ResponseCheck(CheckSource.Header, CheckOperator.Contains, "Content-Type", "json"));
        tab.Tests.Add(new ResponseCheck(CheckSource.Json, CheckOperator.Exists, "$.method"));
        tab.Tests.Add(new ResponseCheck(CheckSource.Schema, CheckOperator.Equals, Expected: "{ \"type\": \"object\", \"required\": [\"method\"] }"));
        tab.Tests.Add(new Extraction(ExtractionSource.Json, "$.method", "lastMethod"));
        tab.Section = RequestSection.Tests;
        await tab.SendAsync();
        Settle();

        // 1. The Tests tab with the results of the send next to each row.
        tab.Tests.Badge.ShouldBe("3/4");
        ScreenTests.Capture(window, "ux-38-tests-tab");

        // 2. The Test results tab.
        tab.ResponseSection = ResponseSection.Tests;
        Settle();
        ScreenTests.Capture(window, "ux-39-test-results");

        // 3. Right-click a value in the JSON response: the menu offers a check and an extraction for its path.
        tab.ResponseSection = ResponseSection.Body;
        Settle();
        var response = window.GetVisualDescendants().OfType<CodeEditor>().Single(e => e.IsReadOnly && e.IsEffectivelyVisible);
        var text = response.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single().Document.Text;
        response.MoveCaret(text.IndexOf("\"POST\"", StringComparison.Ordinal) + 2);
        var menu = response.Menu.ShouldNotBeNull();
        menu.ShowAt(response);
        Settle();
        var addCheck = menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == Loc.Format("Tests_AddCheckAt", "$.method"));
        addCheck.IsVisible.ShouldBeTrue();
        ScreenTests.Capture(window, "ux-40-response-add-check");
        menu.Hide();
        addCheck.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Settle();
        tab.Section.ShouldBe(RequestSection.Tests);
        tab.Tests.Checks[^1].ToCheck().ShouldBe(new ResponseCheck(CheckSource.Json, CheckOperator.Equals, "$.method", "POST"));

        // 4. Dark, Turkish, smallest window.
        ((App)Application.Current!).ApplyTheme(AppTheme.Dark);
        Loc.Instance.SetLanguage("tr");
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Settle();
        ScreenTests.Capture(window, "ux-41-tests-tab-dark-tr-min");
        tab.ResponseSection = ResponseSection.Tests;
        Settle();
        ScreenTests.Capture(window, "ux-42-test-results-dark-tr-min");
        Loc.Instance.SetLanguage("en");
        ((App)Application.Current!).ApplyTheme(AppTheme.Light);

        window.Close();
        HeadlessApp.Log.Messages.ShouldBeEmpty();
        return true;
    }

    [Fact]
    public Task KeptScripts_InTheWindow() => HeadlessApp.Session.Dispatch(ScriptsWalkAsync, TestContext.Current.CancellationToken);

    // The note for Postman scripts kept from an import, opened, in light/English and dark/Turkish at the smallest size.
    private static async Task<bool> ScriptsWalkAsync()
    {
        HeadlessApp.Log.Clear();
        using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
        var loaded = (await JetApi.Storage.WorkspaceStore.LoadAsync(workspace.Root)).Workspace;
        var collection = loaded.Collections[0];
        collection = collection with
        {
            Children = [.. collection.Children.Select(c => c.Name == "Create user"
                ? c with { Scripts = new ScriptSet(Test: "pm.test('created', function () {\n    pm.response.to.have.status(201);\n    pm.expect(_.isEmpty(pm.response.json().errors)).to.be.true;\n});") }
                : c)],
        };
        await JetApi.Storage.WorkspaceStore.SaveCollectionAsync(workspace.Root, collection);
        var settingsFile = Path.Combine(workspace.Root, ".jetapi", "settings.json");
        using var vm = new MainWindowViewModel(new AppSettingsStore(settingsFile), new FakeDialogs(), theme => ((App)Application.Current!).ApplyTheme(theme), action => Dispatcher.UIThread.Post(action),
            settings => ((App)Application.Current!).ApplyAppearance(settings));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
        var ws = vm.Workspace!;
        ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "Create user"));
        var tab = (HttpRequestTabViewModel)ws.SelectedTab!;
        tab.Section = RequestSection.Tests;
        tab.Tests.ToggleScriptsCommand.Execute(null);
        Settle();
        ScreenTests.Capture(window, "ux-47-tests-kept-scripts");

        ((App)Application.Current!).ApplyTheme(AppTheme.Dark);
        Loc.Instance.SetLanguage("tr");
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Settle();
        ScreenTests.Capture(window, "ux-48-tests-kept-scripts-dark-tr-min");
        Loc.Instance.SetLanguage("en");
        ((App)Application.Current!).ApplyTheme(AppTheme.Light);

        window.Close();
        HeadlessApp.Log.Messages.ShouldBeEmpty();
        return true;
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
