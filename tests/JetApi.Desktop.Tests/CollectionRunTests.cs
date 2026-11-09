using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using JetApi.Core.Model;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

/// <summary>The collection runner in the app (M3, phase C), against a local echo server.</summary>
public sealed class CollectionRunTests(EchoServer server) : IClassFixture<EchoServer>
{
    // The example collection, pointed at the echo server, with a check on each request.
    private async Task<(TempWorkspace Workspace, WorkspaceViewModel Vm, FakeDialogs Dialogs)> OpenAsync()
    {
        var workspace = await TempWorkspace.CreateAsync();
        var session = workspace.Session;
        await session.SaveEnvironmentAsync(session.ActiveEnvironment! with { Variables = [new Variable("baseUrl", server.BaseUrl.TrimEnd('/'))] });
        var collection = session.Workspace.Collections[0];
        var get = (HttpRequestDefinition)CollectionTree.Find(collection, ["get-example"])!;
        await session.SaveNodeAsync(collection.Key, ["get-example"], get with
        {
            Checks = [new ResponseCheck(CheckSource.Json, CheckOperator.Equals, "$.path", "/get")],
            Extractions = [new Extraction(ExtractionSource.Json, "$.method", "firstMethod")],
        });
        var post = (HttpRequestDefinition)CollectionTree.Find(collection, ["post-json"])!;
        await session.SaveNodeAsync(collection.Key, ["post-json"], post with
        {
            Url = "{{baseUrl}}/post?who={{email}}&after={{firstMethod}}",
            Checks = [new ResponseCheck(CheckSource.Status, CheckOperator.Equals, Expected: "201")],
        });
        var dialogs = new FakeDialogs();
        return (workspace, workspace.CreateViewModel(dialogs), dialogs);
    }

    [Fact]
    public async Task Running_ShowsEachRequest_GroupedByIteration_AndStopsAtTheFirstFailure()
    {
        var (workspace, vm, _) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            vm.OpenRunner(vm.Collections[0].CollectionKey, []);
            var run = vm.CollectionRun.ShouldNotBeNull();
            run.Requests.Select(r => r.Name).ShouldBe(["Get example", "Post JSON"]);
            run.Iterations = 3;

            await run.RunCommand.ExecuteAsync(null);

            run.Results.ShouldHaveSingleItem().Items.Select(i => i.Passed).ShouldBe([true, false]);
            var failed = run.Results[0].Items[1];
            failed.StatusText.ShouldBe("200 OK");
            failed.ChecksText.ShouldBe("0/1");
            failed.Problems.ShouldBe(["Status equals 201 · Expected 201, got 200."]);
            run.EndNote.ShouldBe(Loc.Format("Run_Stopped", 4));
            run.Summary!.StoppedEarly.ShouldBeTrue();

            run.StopOnFailure = false;
            await run.RunCommand.ExecuteAsync(null);
            run.Results.Count.ShouldBe(3);
            run.Progress.ShouldBe(1);
            run.ProgressText.ShouldStartWith("Iteration 3 / 3 · 6 requests · 3 checks passed · 3 failed");
        }
    }

    [Fact]
    public async Task ADataFile_DrivesTheIterations_AndExtractedValuesFlowToLaterRequests()
    {
        var (workspace, vm, dialogs) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            var data = Path.Combine(workspace.Root, "users.csv");
            await File.WriteAllTextAsync(data, "email\na@b.c\nd@e.f\n", TestContext.Current.CancellationToken);
            dialogs.FileToPick = data;
            vm.OpenRunner(vm.Collections[0].CollectionKey, []);
            var run = vm.CollectionRun!;
            run.StopOnFailure = false;

            await run.ChooseDataFileCommand.ExecuteAsync(null);
            run.DataSummary.ShouldBe("2 rows · columns: email");
            await run.RunCommand.ExecuteAsync(null);

            run.Results.Select(r => r.Header).ShouldBe(["Iteration 1 · email=a@b.c", "Iteration 2 · email=d@e.f"]);
            var post = run.Results[1].Items[1].Result.Send.Response.Body!;
            post.ShouldContain("who=d@e.f");
            post.ShouldContain("after=GET");

            // Values extracted in a run are not kept in the session unless asked.
            workspace.Session.RuntimeVariables.ShouldNotContainKey("firstMethod");
        }
    }

    [Fact]
    public async Task OpeningARequest_ClosesTheRunner_AndUnchecking_LeavesRequestsOut()
    {
        var (workspace, vm, _) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            vm.OpenRunner(vm.Collections[0].CollectionKey, []);
            var run = vm.CollectionRun!;
            run.Requests[1].Include = false;
            await run.RunCommand.ExecuteAsync(null);
            run.Results[0].Items.ShouldHaveSingleItem().Name.ShouldBe("Get example");
            vm.HasOverlay.ShouldBeTrue();

            vm.OpenNode(vm.Collections[0].Children[0]);
            vm.CollectionRun.ShouldBeNull();
        }
    }

    [Fact]
    public Task Runner_InTheWindow() => HeadlessApp.Session.Dispatch(WalkAsync, TestContext.Current.CancellationToken);

    private async Task<bool> WalkAsync()
    {
        HeadlessApp.Log.Clear();
        var (workspace, _, _) = await OpenAsync();
        using (workspace)
        {
            var settingsFile = Path.Combine(workspace.Root, ".jetapi", "settings.json");
            using var vm = new MainWindowViewModel(new AppSettingsStore(settingsFile), new FakeDialogs(), theme => ((App)Application.Current!).ApplyTheme(theme), action => Dispatcher.UIThread.Post(action),
                settings => ((App)Application.Current!).ApplyAppearance(settings));
            var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
            window.Show();
            await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
            var ws = vm.Workspace!;
            ws.OpenRunner(ws.Collections[0].CollectionKey, []);
            Settle();
            ScreenTests.Capture(window, "ux-43-runner-before");
            var run = ws.CollectionRun!;
            run.Iterations = 2;
            run.StopOnFailure = false;
            await run.RunCommand.ExecuteAsync(null);
            Settle();
            ScreenTests.Capture(window, "ux-44-runner-results");

            ((App)Application.Current!).ApplyTheme(AppTheme.Dark);
            Loc.Instance.SetLanguage("tr");
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            Settle();
            ScreenTests.Capture(window, "ux-45-runner-dark-tr-min");
            Loc.Instance.SetLanguage("en");
            ((App)Application.Current!).ApplyTheme(AppTheme.Light);

            window.Close();
            HeadlessApp.Log.Messages.ShouldBeEmpty();
        }

        return true;
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
