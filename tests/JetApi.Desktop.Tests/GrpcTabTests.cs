using JetApi.Core.Model;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

public sealed class GrpcTabTests
{
    private const string Proto = """
        syntax = "proto3";
        package users.v1;
        import "google/protobuf/timestamp.proto";
        message GetUserRequest { string id = 1; bool include_roles = 2; repeated string fields = 3; map<string, int32> counts = 4; }
        message User { string id = 1; google.protobuf.Timestamp created_at = 2; }
        service UserService {
          rpc GetUser(GetUserRequest) returns (User);
          rpc WatchUsers(GetUserRequest) returns (stream User);
        }
        """;

    private static async Task<(TempWorkspace Workspace, WorkspaceViewModel Vm, GrpcRequestTabViewModel Tab)> SetupAsync()
    {
        var workspace = await TempWorkspace.CreateAsync();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "protos", "users"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "protos", "users", "users.proto"), Proto);
        var collection = workspace.Session.Workspace.Collections[0];
        await workspace.Session.SaveCollectionAsync(collection with { Proto = new ProtoSettings(["protos"], ["users/users.proto"]) });
        var path = await workspace.Session.AddNodeAsync(collection.Key, [], new GrpcRequestDefinition { Key = "x", Name = "Get user", ServerUrl = "http://localhost:1" });
        var vm = workspace.CreateViewModel(new FakeDialogs());
        var tab = (GrpcRequestTabViewModel)vm.OpenRequest(collection.Key, path)!;
        return (workspace, vm, tab);
    }

    [Fact]
    public async Task LoadsServicesMethodsAndFields()
    {
        var (workspace, vm, tab) = await SetupAsync();
        using (workspace)
        using (vm)
        {
            tab.IsProtoLoaded.ShouldBeTrue();
            tab.Services.ShouldBe(["users.v1.UserService"]);
            tab.SelectedService.ShouldBe("users.v1.UserService");
            tab.Methods.Select(m => m.Label).ShouldBe(["GetUser · unary", "WatchUsers · streaming (v0.4)"]);
            tab.SelectedMethod!.Name.ShouldBe("GetUser");
            tab.InputTypeText.ShouldBe("Request message: users.v1.GetUserRequest");
            tab.Fields.Select(f => $"{f.Name}:{f.Type}").ShouldBe(["id:string", "include_roles:bool", "fields:repeated string", "counts:map<string, int32>"]);
            tab.IsPlaintext.ShouldBeTrue();
            tab.ProtoSummary.ShouldStartWith("1 services · 2 messages");
        }
    }

    [Fact]
    public async Task Template_IsValid_AndInvalidJsonIsReportedLive()
    {
        var (workspace, vm, tab) = await SetupAsync();
        using (workspace)
        using (vm)
        {
            tab.FillTemplateCommand.Execute(null);
            tab.Message.ShouldContain("\"includeRoles\": false");
            tab.MessageError.ShouldBeNull();

            tab.Message = "{\"id\": 5}";
            tab.MessageError.ShouldBe("$.id: Expected a string, got the number 5.");

            tab.Message = "{\"id\": \"{{$guid}}\"}";
            tab.MessageError.ShouldBeNull();
        }
    }

    [Fact]
    public async Task ProtoErrors_AreShown_AndCallsAreNotMade()
    {
        var (workspace, vm, tab) = await SetupAsync();
        using (workspace)
        using (vm)
        {
            await File.WriteAllTextAsync(Path.Combine(workspace.Root, "protos", "users", "users.proto"), "syntax = \"proto3\";\nmessage X { strng a = 1; }\n", TestContext.Current.CancellationToken);
            tab.ReloadProto();

            tab.HasProtoErrors.ShouldBeTrue();
            tab.ProtoErrors.ShouldHaveSingleItem().ShouldStartWith("users/users.proto:2:");

            await tab.SendAsync();
            tab.Response.HasPreparationErrors.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task UnreachableServer_ShowsUnavailableStatus()
    {
        var (workspace, vm, tab) = await SetupAsync();
        using (workspace)
        using (vm)
        {
            tab.Message = "{\"id\": \"1\"}";
            await tab.SendAsync();

            tab.Response.StatusText.ShouldBe("14 · Unavailable");
            tab.Response.Tone.ShouldBe(StatusTone.Danger);
            vm.HistoryItems.ShouldHaveSingleItem().Badge.ShouldBe("gRPC");
        }
    }
}
