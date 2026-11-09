using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetApi.Desktop.Controls;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

/// <summary>Typing <c>{{</c> lists the variables of the request.</summary>
public sealed class VariableCompletionTests
{
    [Theory]
    [InlineData("{{", 2, 2, "")]
    [InlineData("x {{ba", 6, 4, "ba")]
    [InlineData("{{$gu", 5, 2, "$gu")]
    [InlineData("a{{b}}/{{c", 10, 9, "c")]
    public void AStartedPlaceholder_IsFound(string text, int caret, int start, string prefix) =>
        VariableCompletionText.FindPrefix(text, caret).ShouldBe(new CompletionPrefix(start, prefix));

    [Theory]
    [InlineData("{", 1)]
    [InlineData("{{a}}", 5)]
    [InlineData("{{a b", 5)]
    [InlineData("{{{a", 4)]
    [InlineData("abc", 3)]
    public void OtherText_IsNotAPlaceholderStart(string text, int caret) =>
        VariableCompletionText.FindPrefix(text, caret).ShouldBeNull();

    [Fact]
    public void Inserting_AddsTheClosingBraces_OnlyWhenMissing_AndReplacesTheRestOfTheName()
    {
        var open = VariableCompletionText.Apply("{{ba/users", new CompletionPrefix(2, "ba"), "baseUrl");
        open.ShouldBe(new CompletionEdit(2, 2, "baseUrl}}", 11));

        var closed = VariableCompletionText.Apply("{{baXX}}/users", new CompletionPrefix(2, "ba"), "baseUrl");
        closed.ShouldBe(new CompletionEdit(2, 4, "baseUrl", 11));
    }

    [Fact]
    public void Filter_PutsNamesThatStartWithTheTextFirst_AndDynamicOnesLast()
    {
        VariableSuggestion[] all = [new("userId", "1", "env", false), new("baseUrl", "x", "env", false), new("$guid", string.Empty, "dynamic", true), new("idToken", "t", "env", false)];

        VariableCompletionText.Filter(all, "id").Select(s => s.Name).ShouldBe(["idToken", "userId", "$guid"]);
        VariableCompletionText.Filter(all, string.Empty).Count.ShouldBe(4);
        VariableCompletionText.Filter(all, "zzz").ShouldBeEmpty();
    }

    [Fact]
    public Task TypingBraces_ListsVariables_InEditorsAndCells() =>
        HeadlessApp.Session.Dispatch(WalkAsync, TestContext.Current.CancellationToken);

    private static async Task<bool> WalkAsync()
    {
        HeadlessApp.Log.Clear();
        using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
        using var vm = new MainWindowViewModel(new AppSettingsStore(Path.Combine(workspace.Root, ".jetapi", "settings.json")), new FakeDialogs(), _ => { }, action => Dispatcher.UIThread.Post(action));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
        var ws = vm.Workspace!;
        ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "List users"));
        Settle();
        var tab = (HttpRequestTabViewModel)ws.SelectedTab!;

        // 1. URL: "{{" lists everything, typing filters, Enter inserts the name and the closing braces.
        var url = window.GetVisualDescendants().OfType<CodeEditor>().Single(e => e.Name == "UrlBox");
        var editor = url.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
        url.FocusEditor();
        editor.CaretOffset = editor.Document.TextLength;
        window.KeyTextInput("?id={{");
        Settle();
        var list = url.CompletionList.ShouldNotBeNull();
        list.IsOpen.ShouldBeTrue();
        list.Items.Select(i => i.Name).ShouldContain("baseUrl");
        list.Items.Select(i => i.Name).ShouldContain("$guid");
        ScreenTests.Capture(window, "ux-31-completion-url");
        window.KeyTextInput("tok");
        Settle();
        list.Items.Select(i => i.Name).First().ShouldBe("token");
        list.Selected!.Source.ShouldBe("Development");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle();
        list.IsOpen.ShouldBeFalse();
        tab.Url.ShouldBe("{{baseUrl}}/users?id={{token}}");
        tab.Response.IsEmpty.ShouldBeTrue();

        // 2. Escape closes without inserting; text that is not a placeholder start does not open the list.
        window.KeyTextInput("&x={{");
        Settle();
        list.IsOpen.ShouldBeTrue();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle();
        list.IsOpen.ShouldBeFalse();
        tab.Url.ShouldEndWith("&x={{");
        window.KeyTextInput("zzz");
        Settle();
        list.IsOpen.ShouldBeFalse();

        // 3. A header value cell (dark theme, Turkish): Down then Tab picks the second entry.
        ((App)Avalonia.Application.Current!).ApplyTheme(AppTheme.Dark);
        Loc.Instance.SetLanguage("tr");
        tab.Section = RequestSection.Headers;
        Settle();
        var cell = window.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible && t.Classes.Contains("mono")).ElementAt(1);
        cell.Focus();
        window.KeyTextInput("Bearer {{");
        Settle();
        var cellList = VariableCompletion.Find(cell).ShouldNotBeNull();
        cellList.IsOpen.ShouldBeTrue();
        ScreenTests.Capture(window, "ux-32-completion-cell-dark-tr");
        var second = cellList.Items[1].Name;
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Settle();
        cell.Text.ShouldBe("Bearer {{" + second + "}}");
        cellList.IsOpen.ShouldBeFalse();
        Loc.Instance.SetLanguage("en");
        ((App)Avalonia.Application.Current!).ApplyTheme(AppTheme.Light);

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
