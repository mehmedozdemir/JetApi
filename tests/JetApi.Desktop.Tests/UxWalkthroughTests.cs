using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

/// <summary>
/// Uses the app the way a person does — real mouse clicks and key presses on the rendered window — and
/// saves a screenshot after each step (prefix "ux-") for UX review.
/// </summary>
public sealed class UxWalkthroughTests
{
    [Fact]
    public Task MouseAndKeyboardWalkthrough() =>
        HeadlessApp.Session.Dispatch(WalkAsync, TestContext.Current.CancellationToken);

    private static async Task<bool> WalkAsync()
    {
        HeadlessApp.Log.Clear();
        var settingsFile = Path.Combine(Path.GetTempPath(), "jetapi-ux-" + Guid.NewGuid().ToString("N"), "settings.json");
        using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
        var dialogs = new FakeDialogs();
        using var vm = new MainWindowViewModel(new AppSettingsStore(settingsFile), dialogs, theme => ((App)Application.Current!).ApplyTheme(theme), action => Dispatcher.UIThread.Post(action));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
        var ws = vm.Workspace!;
        Settle();

        // 1. Click the collection row: its screen opens.
        Click(window, TreeRow(window, "User Service"));
        ws.NodeSettings.ShouldNotBeNull().IsCollection.ShouldBeTrue();
        ScreenTests.Capture(window, "ux-01-click-collection");

        // 2. Type a collection variable that the active environment also defines.
        var variables = ws.NodeSettings!.Variables!;
        variables.Rows[^1].Name = "baseUrl";
        variables.Rows[^2].Value = "http://collection.local";
        Settle();
        variables.Rows[^2].IsPlaceholder.ShouldBeFalse();
        variables.Rows[^2].Note.ShouldNotBeEmpty();
        variables.Rows[^1].IsPlaceholder.ShouldBeTrue();
        ScreenTests.Capture(window, "ux-02-collection-variable-overridden");

        // 3. Click a request in the tree: the screen closes (edits saved) and the request opens.
        Click(window, TreeRow(window, "Create user"));
        ws.NodeSettings.ShouldBeNull();
        ws.SelectedTab!.Name.ShouldBe("Create user");
        ScreenTests.Capture(window, "ux-03-click-request");

        // 3b. The URL is a one-line editor: typing highlights variables, a pasted line break is dropped,
        //     Enter sends and Tab moves on to the next control.
        var url = window.GetVisualDescendants().OfType<JetApi.Desktop.Controls.CodeEditor>().Single(e => e.Name == "UrlBox");
        var urlEditor = url.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
        url.FocusEditor();
        urlEditor.Document.Text = "{{baseUrl}}/users\r\n?page=2";
        await Task.Delay(20);
        Settle();
        urlEditor.Document.Text.ShouldNotContain("\n");
        ws.SelectedTab.ShouldBeOfType<HttpRequestTabViewModel>().Url.ShouldBe("{{baseUrl}}/users ?page=2");
        urlEditor.Document.Text = "http://127.0.0.1:1/users";
        Settle();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Task.Delay(50);
        Settle();
        ((HttpRequestTabViewModel)ws.SelectedTab!).Response.IsEmpty.ShouldBeFalse();
        url.FocusEditor();
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Settle();
        (TopLevel.GetTopLevel(window)!.FocusManager!.GetFocusedElement() as Control).ShouldBeOfType<Button>();
        urlEditor.Document.Text = "{{baseUrl}}/users/{{userId}}";
        Settle();
        ScreenTests.Capture(window, "ux-03b-url-editor");

        // 4. Environments on the rail, then the other environment in the list.
        Click(window, Rail(window, 1));
        ws.IsEnvironmentsMode.ShouldBeTrue();
        ScreenTests.Capture(window, "ux-04-rail-environments");
        Click(window, SidebarButton(window, "Production"));
        ws.Environments!.SelectedScope!.Name.ShouldBe("Production");
        ScreenTests.Capture(window, "ux-05-select-production");

        // 5. Right-click Production: its menu offers the environment actions.
        RightClick(window, SidebarButton(window, "Production"));
        var menu = SidebarButton(window, "Production").ContextMenu.ShouldNotBeNull();
        menu.IsOpen.ShouldBeTrue();
        menu.Items.OfType<MenuItem>().Select(i => i.Header).ShouldContain(Loc.Get("Menu_Rename"));
        ScreenTests.Capture(window, "ux-06-environment-context-menu");
        menu.Close();

        // 6. F2 on the focused environment renames it.
        dialogs.Prompts.Enqueue("Prod");
        SidebarButton(window, "Production").Focus();
        window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
        await Task.Delay(50);
        Settle();
        ws.Environments!.Environments.Select(e => e.Name).ShouldContain("Prod");

        // 6b. Import from Postman: the new collection opens in the tree; the report lists what needs attention.
        dialogs.FilesToPick.AddRange([ImportTests.Sample("shop.postman_collection.json"), ImportTests.Sample("dev.postman_environment.json")]);
        Click(window, Rail(window, 0));
        await ws.ImportFromPostmanCommand.ExecuteAsync(null);
        Settle();
        ws.Collections.ShouldContain(c => c.Name == "Shop API");
        ScreenTests.Capture(window, "ux-06b-imported-tree");
        var reportWindow = new JetApi.Desktop.Views.Dialogs.ImportReportDialog(dialogs.ImportReports.Last());
        reportWindow.Show();
        Settle();
        ScreenTests.Capture(reportWindow, "ux-06c-import-report");
        reportWindow.Close();
        Click(window, Rail(window, 1));

        // 7. History, then back to collections.
        Click(window, Rail(window, 2));
        ws.IsHistoryVisible.ShouldBeTrue();
        ws.Environments.ShouldBeNull();
        ScreenTests.Capture(window, "ux-07-rail-history");
        Click(window, Rail(window, 0));
        ws.IsCollectionsMode.ShouldBeTrue();

        // 8. Keyboard only: Tab moves focus through the window; record what gets focus.
        var focused = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Settle();
            var element = TopLevel.GetTopLevel(window)!.FocusManager!.GetFocusedElement() as Control;
            focused.Add(Describe(element));
        }

        File.WriteAllLines(Path.Combine(ScreenTestsDirectory(), "ux-08-tab-order.txt"), focused);
        focused.ShouldNotContain(f => f.StartsWith("GridSplitter", StringComparison.Ordinal));
        ScreenTests.Capture(window, "ux-08-keyboard-focus");

        // 9. Small window (minimum size) and dark theme on the reworked screens.
        window.Width = 960;
        window.Height = 600;
        Settle();
        ScreenTests.Capture(window, "ux-09-narrow-request");
        Click(window, TreeRow(window, "User Service"));
        ScreenTests.Capture(window, "ux-10-narrow-collection");
        ((App)Application.Current!).ApplyTheme(AppTheme.Dark);
        window.Width = 1280;
        window.Height = 800;
        Settle();
        ScreenTests.Capture(window, "ux-11-dark-collection");
        Click(window, Rail(window, 1));
        ScreenTests.Capture(window, "ux-12-dark-environments");
        ws.Environments!.IsDirty.ShouldBeFalse();
        Loc.Instance.SetLanguage("tr");
        Settle();
        ws.Environments!.IsDirty.ShouldBeFalse();
        ScreenTests.Capture(window, "ux-13-dark-environments-tr");
        Loc.Instance.SetLanguage("en");
        ((App)Application.Current!).ApplyTheme(AppTheme.Light);

        window.Close();
        HeadlessApp.Log.Messages.ShouldBeEmpty();
        return true;
    }

    private static string ScreenTestsDirectory()
    {
        var directory = Environment.GetEnvironmentVariable("JETAPI_SCREENSHOTS") is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    // Like a person, scrolls the row into view before clicking it.
    private static TextBlock TreeRow(Window window, string name)
    {
        var row = window.GetVisualDescendants().OfType<TreeViewItem>()
            .Select(item => item.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == name && t.IsEffectivelyVisible))
            .First(text => text is not null)!;
        row.BringIntoView();
        Settle();
        return row;
    }

    private static RadioButton Rail(Window window, int index) =>
        window.GetVisualDescendants().OfType<RadioButton>().Where(r => r.Classes.Contains("rail")).ElementAt(index);

    private static Button SidebarButton(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == name));

    private static Point Center(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Control control)
    {
        Settle();
        var point = Center(window, control);
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Settle();
    }

    private static void RightClick(Window window, Control control)
    {
        Settle();
        var point = Center(window, control);
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Settle();
    }

    private static string Describe(Control? control) => control switch
    {
        null => "(nothing)",
        _ => $"{control.GetType().Name} '{AutomationName(control)}'",
    };

    private static string AutomationName(Control control) =>
        Avalonia.Automation.AutomationProperties.GetName(control) is { Length: > 0 } name
            ? name
            : control switch
            {
                ContentControl { Content: string text } => text,
                TextBox box => box.PlaceholderText ?? box.Text ?? string.Empty,
                _ => control.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty,
            };
}
