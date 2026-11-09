using Avalonia.Headless;
using Avalonia.Threading;
using JetApi.Core.Model;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

/// <summary>Choosing <c>.proto</c> files on disk instead of typing their paths.</summary>
public sealed class ProtoPickTests
{
    private const string Proto = """
        syntax = "proto3";
        package users.v1;
        message GetUserRequest { string id = 1; }
        message User { string id = 1; }
        service UserService { rpc GetUser(GetUserRequest) returns (User); }
        """;

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ws");

    private static string At(params string[] parts) => Path.Combine([Root, .. parts]);

    [Fact]
    public void AFileUnderAnImportPath_IsListedRelativeToIt()
    {
        var result = ProtoPaths.AddFiles(new ProtoSettings(["protos"], []), Root, [At("protos", "users", "v1", "users.proto")]);

        result.Settings.ImportPaths.ShouldBe(["protos"]);
        result.Settings.Files.ShouldBe(["users/v1/users.proto"]);
        result.AddedFiles.ShouldBe(["users/v1/users.proto"]);
        result.OutsideWorkspace.ShouldBeFalse();
    }

    [Fact]
    public void WithoutImportPaths_TheWorkspaceRootIsUsed()
    {
        var result = ProtoPaths.AddFiles(ProtoSettings.Empty, Root, [At("api", "orders.proto")]);

        result.Settings.ImportPaths.ShouldBeEmpty();
        result.Settings.Files.ShouldBe(["api/orders.proto"]);
    }

    [Fact]
    public void AFileOutsideEveryImportPath_AddsItsFolder_AndKeepsTheRoot()
    {
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "billing.proto");

        var result = ProtoPaths.AddFiles(ProtoSettings.Empty, Root, [outside, outside]);

        result.Settings.ImportPaths.ShouldBe([".", Path.GetDirectoryName(outside)!.Replace('\\', '/')]);
        result.Settings.Files.ShouldBe(["billing.proto"]);
        result.OutsideWorkspace.ShouldBeTrue();
    }

    [Fact]
    public void TheDeepestImportPath_Wins_AndListedFilesAreNotRepeated()
    {
        var settings = new ProtoSettings(["protos", "protos/vendor"], ["google/api/http.proto"]);

        var result = ProtoPaths.AddFiles(settings, Root, [At("protos", "vendor", "google", "api", "http.proto"), At("shared", "common.proto")]);

        result.Settings.ImportPaths.ShouldBe(["protos", "protos/vendor", "shared"]);
        result.Settings.Files.ShouldBe(["google/api/http.proto", "common.proto"]);
        result.AddedFiles.ShouldBe(["common.proto"]);
        result.OutsideWorkspace.ShouldBeFalse();
    }

    [Fact]
    public async Task CollectionScreen_AddsPickedFilesAndFolders()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var dialogs = new FakeDialogs();
        using var vm = workspace.CreateViewModel(dialogs);
        var screen = new NodeSettingsViewModel(workspace.Session, dialogs, workspace.Session.Workspace.Collections[0].Key, [], _ => { });

        dialogs.FilesToPick.Add(Path.Combine(workspace.Root, "protos", "users.proto"));
        await screen.AddProtoFilesCommand.ExecuteAsync(null);
        screen.ProtoFilesText.ShouldBe("protos/users.proto");
        screen.ImportPathsText.ShouldBeEmpty();
        screen.HasChanges.ShouldBeTrue();

        dialogs.FolderToPick = Path.Combine(workspace.Root, "vendor");
        await screen.AddImportPathCommand.ExecuteAsync(null);
        screen.ImportPathsText.ShouldBe("vendor");
        screen.ProtoNote.ShouldBeNull();
    }

    [Fact]
    public async Task GrpcTab_ChoosingAProtoFile_SavesTheCollection_AndLoadsIt()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var collection = workspace.Session.Workspace.Collections[0];
        var path = await workspace.Session.AddNodeAsync(collection.Key, [], new GrpcRequestDefinition { Key = "x", Name = "Get user", ServerUrl = "http://localhost:1" }, TestContext.Current.CancellationToken);
        var file = Path.Combine(workspace.Root, "contracts", "users.proto");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, Proto, TestContext.Current.CancellationToken);
        var dialogs = new FakeDialogs();
        using var vm = workspace.CreateViewModel(dialogs);
        var tab = (GrpcRequestTabViewModel)vm.OpenRequest(collection.Key, path)!;
        tab.HasNoProto.ShouldBeTrue();

        dialogs.FilesToPick.Add(file);
        await tab.AddProtoFilesCommand.ExecuteAsync(null);

        workspace.Session.Workspace.FindCollection(collection.Key)!.Proto.Files.ShouldBe(["contracts/users.proto"]);
        tab.IsProtoLoaded.ShouldBeTrue();
        tab.Services.ShouldBe(["users.v1.UserService"]);
        tab.ProtoNote.ShouldBeNull();
    }

    [Fact]
    public Task ProtoScreens_Render() => HeadlessApp.Session.Dispatch(RenderAsync, TestContext.Current.CancellationToken);

    private static async Task<bool> RenderAsync()
    {
        HeadlessApp.Log.Clear();
        using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
        var dialogs = new FakeDialogs();
        using var vm = new MainWindowViewModel(new AppSettingsStore(Path.Combine(workspace.Root, ".jetapi", "settings.json")), dialogs, _ => { }, action => Dispatcher.UIThread.Post(action));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
        var ws = vm.Workspace!;
        var collectionKey = ws.Session.Workspace.Collections[0].Key;

        // The collection screen: "Add files…" next to the list, the note for files outside the workspace.
        ws.OpenCollectionSettings(collectionKey);
        var screen = ws.NodeSettings!;
        screen.Section = NodeSettingsSection.Proto;
        dialogs.FilesToPick.Add(Path.Combine(Path.GetTempPath(), "shared-protos", "billing.proto"));
        await screen.AddProtoFilesCommand.ExecuteAsync(null);
        screen.ProtoNote.ShouldNotBeNull();
        ScreenTests.Capture(window, "ux-28-proto-settings");
        screen.CancelCommand.Execute(null);

        // A gRPC request in a collection without protos offers to choose one, also in a small window.
        await ws.Session.SaveCollectionAsync(ws.Session.Workspace.Collections[0] with { Proto = ProtoSettings.Empty });
        ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "Get user"));
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        ((GrpcRequestTabViewModel)ws.SelectedTab!).HasNoProto.ShouldBeTrue();
        ScreenTests.Capture(window, "ux-29-grpc-no-proto-min-size");

        window.Close();
        HeadlessApp.Log.Messages.ShouldBeEmpty();
        return true;
    }
}
