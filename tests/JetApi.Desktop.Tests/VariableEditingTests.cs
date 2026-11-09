using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetApi.Core.Model;
using JetApi.Core.Variables;
using JetApi.Desktop.Controls;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;
using JetApi.Desktop.Views;

namespace JetApi.Desktop.Tests;

/// <summary>Editing variables from the request editors: the hover card and "Set as variable".</summary>
public sealed class VariableEditingTests(EchoServer server) : IClassFixture<EchoServer>
{
    private static async Task<(TempWorkspace Workspace, WorkspaceViewModel Vm, HttpRequestTabViewModel Tab)> OpenAsync()
    {
        var workspace = await TempWorkspace.CreateAsync();
        var vm = workspace.CreateViewModel(new FakeDialogs());
        vm.OpenNode(vm.Collections[0].Children[0]);
        return (workspace, vm, (HttpRequestTabViewModel)vm.SelectedTab!);
    }

    [Fact]
    public async Task HoveredVariable_IsEditedInItsOwnLayer()
    {
        var (workspace, vm, tab) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            var card = VariableEditorViewModel.ForHint(tab.Variables, tab.Variables.Describe("baseUrl"));
            card.Value.ShouldBe("https://httpbin.org");
            card.Description.ShouldBe("Environment: Development");
            card.CanEdit.ShouldBeTrue();
            card.ShowScopeChoice.ShouldBeFalse();
            card.SaveCommand.CanExecute(null).ShouldBeFalse();
            card.KeepsOpen.ShouldBeFalse();

            string? saved = null;
            card.Saved += (_, name) => saved = name;
            card.Value = "http://localhost:8080";
            card.KeepsOpen.ShouldBeTrue();
            await card.SaveCommand.ExecuteAsync(null);

            saved.ShouldBe("baseUrl");
            card.Error.ShouldBeNull();
            workspace.Session.ActiveEnvironment!.Variables.Single(v => v.Name == "baseUrl").Value.ShouldBe("http://localhost:8080");
            workspace.Read("environments/dev.yaml").ShouldContain("http://localhost:8080");
            tab.Variables.Describe("baseUrl").Value.ShouldBe("http://localhost:8080");
            tab.UrlHints.ShouldHaveSingleItem().Value.ShouldBe("http://localhost:8080");
        }
    }

    [Fact]
    public async Task UndefinedVariable_IsAddedWhereTheUserChooses()
    {
        var (workspace, vm, tab) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            var card = VariableEditorViewModel.ForHint(tab.Variables, tab.Variables.Describe("teamId"));
            card.IsUndefined.ShouldBeTrue();
            card.ShowScopeChoice.ShouldBeTrue();
            card.SaveText.ShouldBe(Loc.Get("Btn_Add"));
            card.Scopes.Select(s => s.Source).ShouldBe([VariableSource.Environment, VariableSource.Collection, VariableSource.Global]);
            card.SelectedScope!.Source.ShouldBe(VariableSource.Environment);

            card.SelectedScope = card.Scopes.Single(s => s.Source == VariableSource.Collection);
            card.Value = "42";
            await card.SaveCommand.ExecuteAsync(null);

            workspace.Session.Workspace.Collections[0].Variables.ShouldContain(new Variable("teamId", "42"));
            var hint = tab.Variables.Describe("teamId");
            hint.IsDefined.ShouldBeTrue();
            hint.Source.ShouldBe(VariableSource.Collection);
        }
    }

    [Fact]
    public async Task SelectedText_BecomesANewVariable_WithNameChecks()
    {
        var (workspace, vm, tab) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            var card = VariableEditorViewModel.ForSelection(tab.Variables, "acme-42");
            card.IsNew.ShouldBeTrue();
            card.Value.ShouldBe("acme-42");
            card.SaveCommand.CanExecute(null).ShouldBeFalse();

            card.Name = "tenant id";
            await card.SaveCommand.ExecuteAsync(null);
            card.Error.ShouldBe(Loc.Get("Var_NameInvalid"));

            card.Name = "baseUrl";
            card.Error.ShouldBeNull();
            card.OverwriteWarning.ShouldNotBeNull();
            card.SelectedScope = card.Scopes.Single(s => s.Source == VariableSource.Global);
            card.OverwriteWarning.ShouldBeNull();

            card.Name = "tenantId";
            string? saved = null;
            card.Saved += (_, name) => saved = name;
            await card.SaveCommand.ExecuteAsync(null);

            saved.ShouldBe("tenantId");
            workspace.Session.Workspace.GlobalVariables.ShouldContain(new Variable("tenantId", "acme-42"));
        }
    }

    [Fact]
    public async Task DynamicSecretAndNestedVariables_AreShownSafely()
    {
        var (workspace, vm, tab) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            var environment = workspace.Session.ActiveEnvironment!;
            await workspace.Session.SaveEnvironmentAsync(environment with
            {
                Variables = [.. environment.Variables, new Variable("token", "s3cret", IsSecret: true), new Variable("usersUrl", "{{baseUrl}}/users")],
            }, TestContext.Current.CancellationToken);

            var dynamic = VariableEditorViewModel.ForHint(tab.Variables, tab.Variables.Describe("$guid"));
            dynamic.CanEdit.ShouldBeFalse();
            dynamic.IsReadOnly.ShouldBeTrue();

            var secret = VariableEditorViewModel.ForHint(tab.Variables, tab.Variables.Describe("token"));
            secret.IsSecret.ShouldBeTrue();
            secret.Value.ShouldBe("s3cret");
            secret.Hint!.Value.ShouldNotContain("s3cret");
            secret.ResolvedValue.ShouldBeNull();

            var nested = VariableEditorViewModel.ForHint(tab.Variables, tab.Variables.Describe("usersUrl"));
            nested.Value.ShouldBe("{{baseUrl}}/users");
            nested.ResolvedValue.ShouldBe("https://httpbin.org/users");
        }
    }

    [Fact]
    public async Task WithoutAnActiveEnvironment_TheEnvironmentIsNotOffered()
    {
        var (workspace, vm, tab) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            vm.SelectedEnvironment = vm.EnvironmentOptions[0];
            var card = VariableEditorViewModel.ForSelection(tab.Variables, "x");
            card.Scopes.Select(s => s.Source).ShouldBe([VariableSource.Collection, VariableSource.Global]);
            card.SelectedScope!.Source.ShouldBe(VariableSource.Collection);
        }
    }

    [Fact]
    public async Task DisabledDuplicate_IsEnabledAndUpdated_InsteadOfAddingAnother()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        await workspace.Session.SaveGlobalsAsync([new Variable("region", "eu", IsEnabled: false)], TestContext.Current.CancellationToken);

        await VariableWriter.SetAsync(workspace.Session, null, VariableSource.Global, "region", "us");

        workspace.Session.Workspace.GlobalVariables.ShouldBe([new Variable("region", "us")]);
    }

    [Fact]
    public async Task AValueFromAResponse_IsSavedIntoANewOrExistingVariable()
    {
        var (workspace, vm, tab) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            var card = VariableEditorViewModel.ForAssignment(tab.Variables, " \"abc123\" ");
            card.IsAssignment.ShouldBeTrue();
            card.Title.ShouldBe(Loc.Get("Var_AssignTitle"));
            card.Value.ShouldBe("abc123");
            card.NameSuggestions.ShouldContain("baseUrl");

            // An existing name moves the choice to where it is stored and says it will be updated.
            card.SelectedScope = card.Scopes.Single(s => s.Source == VariableSource.Global);
            card.Name = "baseUrl";
            card.SelectedScope!.Source.ShouldBe(VariableSource.Environment);
            card.OverwriteWarning.ShouldBe(Loc.Get("Var_WillUpdate"));

            card.Name = "token";
            await card.SaveCommand.ExecuteAsync(null);
            workspace.Session.ActiveEnvironment!.Variables.ShouldContain(new Variable("token", "abc123"));
        }
    }

    [Fact]
    public async Task Suggestions_ListVisibleVariables_ThenDynamicOnes()
    {
        var (workspace, vm, tab) = await OpenAsync();
        using (workspace)
        using (vm)
        {
            await workspace.Session.SaveGlobalsAsync([new Variable("apiKey", "k", IsSecret: true), new Variable("off", "x", IsEnabled: false)], TestContext.Current.CancellationToken);

            var suggestions = tab.Variables.Suggestions();

            var stored = suggestions.Where(s => !s.IsDynamic).ToList();
            stored.Select(s => s.Name).ShouldBe(["apiKey", "baseUrl"]);
            stored[0].Value.ShouldNotContain("k");
            stored[0].Source.ShouldBe(Loc.Get("Var_ScopeGlobal"));
            stored[1].Source.ShouldBe("Environment: Development");
            suggestions.Where(s => s.IsDynamic).Select(s => s.Name).ShouldContain("$guid");
            tab.Variables.Suggestions(includeDynamic: false).ShouldAllBe(s => !s.IsDynamic);
        }
    }

    [Fact]
    public Task ResponseValue_ToVariable_InTheWindow() =>
        HeadlessApp.Session.Dispatch(() => ResponseWalkAsync(server.BaseUrl), TestContext.Current.CancellationToken);

    private static async Task<bool> ResponseWalkAsync(string baseUrl)
    {
        HeadlessApp.Log.Clear();
        using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
        using var vm = new MainWindowViewModel(new AppSettingsStore(Path.Combine(workspace.Root, ".jetapi", "settings.json")), new FakeDialogs(), _ => { }, action => Dispatcher.UIThread.Post(action));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
        var ws = vm.Workspace!;
        ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "Create user"));
        var tab = (HttpRequestTabViewModel)ws.SelectedTab!;
        tab.Url = baseUrl.TrimEnd('/') + "/echo";
        await tab.SendAsync();
        Settle();

        var response = window.GetVisualDescendants().OfType<CodeEditor>().Single(e => e.IsReadOnly && e.IsEffectivelyVisible);
        var document = response.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single().Document;
        var start = document.Text.IndexOf("\"POST\"", StringComparison.Ordinal);
        response.Select(start, "\"POST\"".Length);
        response.AssignSelectionToVariable();
        Settle();
        var card = response.Popup.ShouldNotBeNull().Current.ShouldNotBeNull();
        card.Value.ShouldBe("POST");
        card.Name = "lastMethod";
        Settle();
        ScreenTests.Capture(window, "ux-30-response-to-variable");
        await card.SaveCommand.ExecuteAsync(null);
        Settle();
        response.Popup.IsOpen.ShouldBeFalse();
        ws.Session.ActiveEnvironment!.Variables.ShouldContain(new Variable("lastMethod", "POST"));
        document.Text.ShouldContain("\"POST\"");

        window.Close();
        HeadlessApp.Log.Messages.ShouldBeEmpty();
        return true;
    }

    [Fact]
    public Task HoverCard_AndSetAsVariable_InTheWindow() =>
        HeadlessApp.Session.Dispatch(WalkAsync, TestContext.Current.CancellationToken);

    private static async Task<bool> WalkAsync()
    {
        HeadlessApp.Log.Clear();
        var (openDelay, closeDelay) = (VariablePopup.OpenDelay, VariablePopup.CloseDelay);
        VariablePopup.OpenDelay = VariablePopup.CloseDelay = TimeSpan.Zero;
        try
        {
            using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
            using var vm = new MainWindowViewModel(new AppSettingsStore(Path.Combine(workspace.Root, ".jetapi", "settings.json")), new FakeDialogs(), _ => { }, action => Dispatcher.UIThread.Post(action));
            var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
            window.Show();
            await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
            var ws = vm.Workspace!;
            ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "Create user"));
            Settle();
            var tab = (HttpRequestTabViewModel)ws.SelectedTab!;

            // 1. Rest the pointer on {{baseUrl}}: the card shows the value in an editable box.
            var url = window.GetVisualDescendants().OfType<CodeEditor>().Single(e => e.Name == "UrlBox");
            var urlEditor = url.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
            HoverText(window, urlEditor, "{{baseUrl}}");
            var card = url.Popup.ShouldNotBeNull().Current.ShouldNotBeNull();
            card.Value.ShouldBe("http://localhost:5000");
            CaptureWithCard(window, url, "ux-24-variable-card");

            // 2. Edit the value: the card stays open when the pointer leaves; Save writes the environment.
            card.Value = "http://localhost:5050";
            window.MouseMove(new Point(1, 1));
            Settle();
            url.Popup!.IsOpen.ShouldBeTrue();
            CaptureWithCard(window, url, "ux-25-variable-card-edited");
            await card.SaveCommand.ExecuteAsync(null);
            Settle();
            url.Popup.IsOpen.ShouldBeFalse();
            ws.Session.ActiveEnvironment!.Variables.Single(v => v.Name == "baseUrl").Value.ShouldBe("http://localhost:5050");

            // 3. An undefined variable in the body: the card offers where to add it (dark, Turkish).
            ((App)Application.Current!).ApplyTheme(AppTheme.Dark);
            Loc.Instance.SetLanguage("tr");
            Settle();
            var body = window.GetVisualDescendants().OfType<CodeEditor>().Single(e => e.HighlightVariables && !e.IsSingleLine);
            var bodyEditor = body.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
            HoverText(window, bodyEditor, "{{teamId}}");
            var undefinedCard = body.Popup.ShouldNotBeNull().Current.ShouldNotBeNull();
            undefinedCard.ShowScopeChoice.ShouldBeTrue();
            CaptureWithCard(window, body, "ux-26-variable-card-undefined-dark-tr");
            window.MouseMove(new Point(1, 1));
            Settle();
            body.Popup!.IsOpen.ShouldBeFalse();
            Loc.Instance.SetLanguage("en");
            ((App)Application.Current!).ApplyTheme(AppTheme.Light);
            Settle();

            // 4. Select text in the body and choose "Set as variable…": the selection becomes the placeholder.
            var start = bodyEditor.Document.Text.IndexOf("ayse@example.com", StringComparison.Ordinal);
            body.Select(start, "ayse@example.com".Length);
            body.SetSelectionAsVariable();
            Settle();
            var create = body.Popup!.Current.ShouldNotBeNull();
            create.IsNew.ShouldBeTrue();
            create.Value.ShouldBe("ayse@example.com");
            create.Name = "email";
            Settle();
            CaptureWithCard(window, body, "ux-27-set-as-variable");
            await create.SaveCommand.ExecuteAsync(null);
            Settle();
            body.Popup.IsOpen.ShouldBeFalse();
            tab.BodyText.ShouldContain("\"email\": \"{{email}}\"");
            ws.Session.ActiveEnvironment!.Variables.ShouldContain(new Variable("email", "ayse@example.com"));
            tab.IsDirty.ShouldBeTrue();

            // 5. The same from a table cell (header value).
            tab.Section = RequestSection.Headers;
            Settle();
            var cell = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible && t.Text == "{{$guid}}");
            cell.Text = "trace-1";
            cell.SelectAll();
            var menu = cell.ContextFlyout.ShouldBeOfType<MenuFlyout>();
            menu.ShowAt(cell);
            Settle();
            var item = menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == Loc.Get("Var_SetAsVariable"));
            item.IsEnabled.ShouldBeTrue();
            menu.Hide();
            item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Settle();
            var fromCell = VariablePopup.Find(cell).ShouldNotBeNull().Current.ShouldNotBeNull();
            fromCell.Name = "traceId";
            await fromCell.SaveCommand.ExecuteAsync(null);
            Settle();
            cell.Text.ShouldBe("{{traceId}}");

            window.Close();
            HeadlessApp.Log.Messages.ShouldBeEmpty();
            return true;
        }
        finally
        {
            (VariablePopup.OpenDelay, VariablePopup.CloseDelay) = (openDelay, closeDelay);
        }
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void HoverText(Window window, AvaloniaEdit.TextEditor editor, string text)
    {
        var textView = editor.TextArea.TextView;
        var location = editor.Document.GetLocation(editor.Document.Text.IndexOf(text, StringComparison.Ordinal) + 3);
        var point = textView.GetVisualPosition(new AvaloniaEdit.TextViewPosition(location), AvaloniaEdit.Rendering.VisualYPosition.LineMiddle) - textView.ScrollOffset;
        window.MouseMove(textView.TranslatePoint(point, window)!.Value);
        Settle();
    }

    // The window, and the card's own popup window when popups are separate top levels.
    private static void CaptureWithCard(Window window, CodeEditor editor, string name)
    {
        ScreenTests.Capture(window, name);
        if (editor.Popup?.View is { } view && TopLevel.GetTopLevel(view) is { } top && top != window)
        {
            Settle();
            using var frame = top.CaptureRenderedFrame();
            frame.ShouldNotBeNull().Save(Path.Combine(ScreenTests.ScreenshotDirectory, name + "-card.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
}
