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

namespace JetApi.Desktop.Tests;

/// <summary>
/// Renders every screen of the real application with Skia, fails on binding errors and saves
/// screenshots (to JETAPI_SCREENSHOTS, or the test output folder) for visual review.
/// </summary>
public sealed class ScreenTests
{
    private const string Proto = """
        syntax = "proto3";
        package users.v1;
        import "google/protobuf/timestamp.proto";
        message GetUserRequest { string id = 1; bool include_roles = 2; repeated string fields = 3; }
        message User { string id = 1; string name = 2; google.protobuf.Timestamp created_at = 3; }
        service UserService { rpc GetUser(GetUserRequest) returns (User); rpc WatchUsers(GetUserRequest) returns (stream User); }
        """;

    private static string ScreenshotDirectory
    {
        get
        {
            var directory = Environment.GetEnvironmentVariable("JETAPI_SCREENSHOTS") is { Length: > 0 } configured
                ? configured
                : Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    [Fact]
    public Task AllScreensRender_WithoutBindingErrors() =>
        HeadlessApp.Session.Dispatch(RenderAllScreensAsync, TestContext.Current.CancellationToken);

    [Fact]
    public Task Shortcuts_OpenPaletteAndEnvironments() =>
        HeadlessApp.Session.Dispatch(PressShortcuts, TestContext.Current.CancellationToken);

    // Returns a value so the headless session awaits the whole asynchronous flow on its UI thread.
    private static async Task<bool> RenderAllScreensAsync()
    {
        HeadlessApp.Log.Clear();
        var settingsFile = Path.Combine(Path.GetTempPath(), "jetapi-screens-" + Guid.NewGuid().ToString("N"), "settings.json");
        using var workspace = await CreateSampleWorkspaceAsync();
        using var vm = new MainWindowViewModel(new AppSettingsStore(settingsFile), new FakeDialogs(), theme => ((App)Avalonia.Application.Current!).ApplyTheme(theme), action => Dispatcher.UIThread.Post(action));
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();

        Capture(window, "01-welcome");

        await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
        var ws = vm.Workspace!;
        Capture(window, "02-workspace-empty");

        ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "Create user"));
        var http = (HttpRequestTabViewModel)ws.SelectedTab!;
        Capture(window, "03-rest-body");
        HoverVariables(window);
        http.Section = RequestSection.Headers;
        Capture(window, "04-rest-headers");
        http.Section = RequestSection.Auth;
        Capture(window, "05-rest-auth");

        var server = new EchoServer();
        await server.InitializeAsync();
        http.Url = server.BaseUrl.TrimEnd('/') + "/echo";
        http.Section = RequestSection.Body;
        await http.SendAsync();
        Capture(window, "06a-rest-response");
        http.ResponseSection = ResponseSection.Timing;
        Capture(window, "06b-rest-timing");
        http.ResponseSection = ResponseSection.Sent;
        Capture(window, "06c-rest-sent");
        await server.DisposeAsync();

        http.Url = "http://127.0.0.1:1/users";
        await http.SendAsync();
        http.ResponseSection = ResponseSection.Body;
        Capture(window, "06d-rest-failed");

        ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "Get user"));
        Capture(window, "07-grpc");
        var grpc = (GrpcRequestTabViewModel)ws.SelectedTab!;
        grpc.Message = "{\"id\": 5}";
        Capture(window, "08-grpc-json-error");
        grpc.Message = "{\"id\": \"42\"}";
        await grpc.SendAsync();
        Capture(window, "09-grpc-unavailable");

        ws.ShowEnvironments();
        Capture(window, "10-environments");
        ws.Environments!.SelectCommand.Execute(ws.Environments.Environments.Single(e => e.Name == "Production"));
        Capture(window, "11-environments-prod");
        ws.Environments = null;

        ws.OpenNodeSettingsCommand.Execute(ws.Collections[0]);
        Capture(window, "12-collection-settings");
        ws.NodeSettings = null;

        ws.OpenPalette();
        ws.Palette!.Query = "user";
        Capture(window, "13-palette");
        ws.Palette = null;

        ws.SelectedTab = ws.Tabs[0];
        ((App)Avalonia.Application.Current!).ApplyTheme(AppTheme.Dark);
        Capture(window, "14-dark");
        ((App)Avalonia.Application.Current!).ApplyTheme(AppTheme.Light);

        Loc.Instance.SetLanguage("tr");
        Capture(window, "15-turkish");
        Loc.Instance.SetLanguage("en");

        window.Close();
        HeadlessApp.Log.Messages.ShouldBeEmpty();
        return true;
    }

    private static async Task<bool> PressShortcuts()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        using var vm = new MainWindowViewModel(new AppSettingsStore(Path.Combine(workspace.Root, ".jetapi", "settings.json")), new FakeDialogs(), _ => { }, action => Dispatcher.UIThread.Post(action));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.K, Avalonia.Input.RawInputModifiers.Control);
        vm.Workspace!.Palette.ShouldNotBeNull();
        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);
        vm.Workspace.Palette.ShouldBeNull();

        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.E, Avalonia.Input.RawInputModifiers.Control);
        vm.Workspace.Environments.ShouldNotBeNull();
        window.Close();
        return true;
    }

    // Moves the mouse over variables in the URL box and the body editor and checks the hover hints.
    private static void HoverVariables(Window window)
    {
        var urlBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "UrlBox");
        var presenter = urlBox.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single();
        window.MouseMove(presenter.TranslatePoint(new Avalonia.Point(12, presenter.Bounds.Height / 2), window)!.Value);
        var urlHint = ToolTip.GetTip(urlBox).ShouldBeAssignableTo<Control>()!.Tag.ShouldBeOfType<VariableHint>();
        urlHint.Placeholder.ShouldBe("{{baseUrl}}");
        urlHint.Value.ShouldBe("http://localhost:5000");

        var body = window.GetVisualDescendants().OfType<CodeEditor>().Single(e => e.HighlightVariables);
        var textView = body.GetVisualDescendants().OfType<AvaloniaEdit.Rendering.TextView>().Single();
        var document = textView.Document;
        var undefinedAt = document.GetLocation(document.Text.IndexOf("{{teamId}}", StringComparison.Ordinal) + 3);
        var point = textView.GetVisualPosition(new AvaloniaEdit.TextViewPosition(undefinedAt), AvaloniaEdit.Rendering.VisualYPosition.LineMiddle) - textView.ScrollOffset;
        window.MouseMove(textView.TranslatePoint(point, window)!.Value);
        var bodyHint = ToolTip.GetTip(body.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single()).ShouldBeAssignableTo<Control>()!.Tag.ShouldBeOfType<VariableHint>();
        bodyHint.IsDefined.ShouldBeFalse();
        Capture(window, "03b-variable-hover");

        window.MouseMove(new Avalonia.Point(1, 1));
        ToolTip.GetTip(urlBox).ShouldBeNull();
    }

    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        frame.ShouldNotBeNull().Save(Path.Combine(ScreenshotDirectory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static async Task<TempWorkspace> CreateSampleWorkspaceAsync()
    {
        var workspace = await TempWorkspace.CreateAsync();
        var session = workspace.Session;
        Directory.CreateDirectory(Path.Combine(workspace.Root, "protos", "users", "v1"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "protos", "users", "v1", "users.proto"), Proto);
        await session.SaveEnvironmentAsync(new ApiEnvironment
        {
            Key = "dev",
            Name = "Development",
            Variables = [new("baseUrl", "http://localhost:5000"), new("grpcUrl", "http://127.0.0.1:1"), new("token", "dev-7f3a91c2e5", IsSecret: true)],
        });
        await session.AddEnvironmentAsync("Production", [new("baseUrl", "https://api.example.com"), new("token", string.Empty, IsSecret: true)]);
        await session.DeleteCollectionAsync("examples");
        var key = await session.AddCollectionAsync("User Service");
        var collection = session.Workspace.FindCollection(key)!;
        await session.SaveCollectionAsync(collection with { Auth = new BearerAuth("{{token}}"), Proto = new ProtoSettings(["protos"], ["users/v1/users.proto"]) });
        await session.AddNodeAsync(key, [], new HttpRequestDefinition { Key = "x", Name = "List users", Url = "{{baseUrl}}/users", QueryParameters = [new("page", "1")] });
        var admin = await session.AddNodeAsync(key, [], new Folder { Key = "x", Name = "Admin" });
        await session.AddNodeAsync(key, [], new HttpRequestDefinition
        {
            Key = "x",
            Name = "Create user",
            Method = "POST",
            Url = "{{baseUrl}}/users",
            Headers = [new("X-Trace-Id", "{{$guid}}")],
            Body = HttpBody.Json("{\n  \"name\": \"Ayşe Yılmaz\",\n  \"email\": \"ayse@example.com\",\n  \"roles\": [\"admin\"],\n  \"requestId\": \"{{$guid}}\",\n  \"teamId\": \"{{teamId}}\"\n}"),
        });
        await session.AddNodeAsync(key, admin, new HttpRequestDefinition { Key = "x", Name = "Delete user", Method = "DELETE", Url = "{{baseUrl}}/users/{{userId}}" });
        await session.AddNodeAsync(key, [], new GrpcRequestDefinition
        {
            Key = "x",
            Name = "Get user",
            ServerUrl = "{{grpcUrl}}",
            Service = "users.v1.UserService",
            Method = "GetUser",
            Message = "{\n  \"id\": \"42\",\n  \"includeRoles\": true\n}",
            Metadata = [new("x-tenant", "acme")],
        });
        return workspace;
    }
}
