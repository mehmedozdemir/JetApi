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

/// <summary>The gRPC message editor suggests the request message's fields; the field list opens from a button.</summary>
public sealed class FieldCompletionTests
{
    [Fact]
    public Task MessageEditor_SuggestsFields() =>
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
        ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "Get user"));
        var tab = (GrpcRequestTabViewModel)ws.SelectedTab!;
        tab.Message = "{\n  \"id\": \"42\"\n}";
        Settle();

        // 1. The editor takes the full width; the fields are behind the "Fields" button.
        tab.FieldsBadge.ShouldBe("3");
        var message = window.GetVisualDescendants().OfType<CodeEditor>().Single(e => e.IsEffectivelyVisible && e.MessageType is not null);
        var body = (Control)message.Parent!.Parent!;
        message.Bounds.Width.ShouldBeGreaterThan(body.Bounds.Width - 30);
        ScreenTests.Capture(window, "ux-56-grpc-message-full-width");
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FieldsButton");
        button.Flyout!.ShowAt(button);
        Settle();
        button.Flyout.IsOpen.ShouldBeTrue();
        ScreenTests.Capture(window, "ux-57-grpc-fields-list");
        button.Flyout.Hide();
        Settle();

        // 2. Typing a quote where a property name goes lists the fields that are not set yet.
        var editor = message.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single();
        message.FocusEditor();
        editor.CaretOffset = tab.Message.IndexOf("\"42\"", StringComparison.Ordinal) + 4;
        window.KeyTextInput(",");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyTextInput("\"");
        Settle();
        var list = message.CompletionList.ShouldNotBeNull();
        list.IsOpen.ShouldBeTrue();
        list.Fields.Select(f => f.Name).ShouldBe(["includeRoles", "fields"]);
        list.Fields[1].Type.ShouldBe("repeated string");
        ScreenTests.Capture(window, "ux-58-grpc-field-completion");

        // 3. Typing filters; Enter writes the field with its placeholder value selected, so typing replaces it.
        window.KeyTextInput("inc");
        Settle();
        list.Fields.Select(f => f.Name).ShouldBe(["includeRoles"]);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle();
        list.IsOpen.ShouldBeFalse();
        window.KeyTextInput("true");
        Settle();
        tab.Message.ShouldBe("{\n  \"id\": \"42\",\n  \"includeRoles\": true\n}");
        tab.MessageError.ShouldBeNull();

        // 4. Ctrl+Space offers the remaining field at a free position; Escape closes without changes.
        window.KeyTextInput(",");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle();
        list.IsOpen.ShouldBeFalse();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
        Settle();
        list.IsOpen.ShouldBeTrue();
        list.Fields.Select(f => f.Name).ShouldBe(["fields"]);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle();
        list.IsOpen.ShouldBeFalse();

        // 5. No suggestions inside a value.
        editor.CaretOffset = tab.Message.IndexOf("42", StringComparison.Ordinal) + 1;
        window.KeyTextInput("\"");
        Settle();
        list.IsOpen.ShouldBeFalse();

        // 6. Dark theme, Turkish: the list and the field list.
        ((App)Avalonia.Application.Current!).ApplyTheme(AppTheme.Dark);
        Loc.Instance.SetLanguage("tr");
        tab.Message = "{\n  \n}";
        Settle();
        message.FocusEditor();
        editor.CaretOffset = 4;
        window.KeyTextInput("\"");
        Settle();
        list.IsOpen.ShouldBeTrue();
        list.Fields.Count.ShouldBe(3);
        ScreenTests.Capture(window, "ux-59-grpc-field-completion-dark-tr");
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        button.Flyout.ShowAt(button);
        Settle();
        ScreenTests.Capture(window, "ux-60-grpc-fields-list-dark-tr");
        button.Flyout.Hide();
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
