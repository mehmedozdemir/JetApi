using JetApi.Core.Model;
using JetApi.Grpc.Schema;

namespace JetApi.Grpc.Tests;

public sealed class ProtoSchemaLoaderTests
{
    [Fact]
    public void Load_ExposesServicesOfRequestedFilesOnly()
    {
        var schema = TestSchema.Load();

        var service = schema.Services.ShouldHaveSingleItem();
        service.FullName.ShouldBe("jetapi.test.v1.EverythingService");
        service.Methods.Select(m => m.Name).ShouldBe(["Echo", "Fail", "Slow", "EchoMetadata", "Watch"]);
        schema.FindMethod("jetapi.test.v1.EverythingService", "Watch")!.IsServerStreaming.ShouldBeTrue();
        schema.FindMethod("jetapi.test.v1.EverythingService", "Echo")!.IsUnary.ShouldBeTrue();
        schema.Messages.ShouldContainKey("jetapi.test.v1.Address");
        schema.Messages.ShouldContainKey("google.protobuf.Timestamp");
    }

    [Fact]
    public void Load_DescribesFields()
    {
        var everything = TestSchema.Load().Everything();

        var addressById = everything.FindField("addressById")!;
        addressById.ShouldBeSameAs(everything.FindField("address_by_id"));
        addressById.IsMap.ShouldBeTrue();
        everything.FindField("numbers")!.IsPacked.ShouldBeTrue();
        everything.FindField("unpacked")!.IsPacked.ShouldBeFalse();
        everything.FindField("email")!.OneofName.ShouldBe("contact");
        everything.FindField("maybe")!.HasPresence.ShouldBeTrue();
        everything.FindField("maybe")!.OneofName.ShouldBeNull();
        everything.FindField("i32")!.HasPresence.ShouldBeFalse();
        everything.FindField("tree")!.MessageType!.FindField("children")!.MessageType!.FullName.ShouldBe("jetapi.test.v1.Node");
    }

    [Fact]
    public void Load_FromCollectionSettings_ResolvesImportPathsAgainstWorkspaceRoot()
    {
        var workspaceRoot = Path.GetDirectoryName(TestSchema.ProtoRoot)!;

        var result = ProtoSchemaLoader.Load(new ProtoSettings(["Protos"], ["test/v1/everything.proto"]), workspaceRoot);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Load_ReportsSyntaxErrorsWithLocation()
    {
        var directory = Directory.CreateTempSubdirectory("jetapi-proto");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "bad.proto"), "syntax = \"proto3\";\n\nmessage Broken {\n  strng name = 1;\n}\n");

            var result = ProtoSchemaLoader.Load([directory.FullName], ["bad.proto"]);

            result.IsSuccess.ShouldBeFalse();
            var error = result.Diagnostics.ShouldHaveSingleItem();
            (error.File, error.Line, error.IsError).ShouldBe(("bad.proto", 4, true));
            error.Message.ShouldContain("strng");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Load_ReportsMissingFilesAndImportPaths()
    {
        ProtoSchemaLoader.Load([TestSchema.ProtoRoot], ["nope.proto"]).Diagnostics.ShouldHaveSingleItem().Message.ShouldContain("not found");
        ProtoSchemaLoader.Load(["/does/not/exist"], ["x.proto"]).Diagnostics[0].Message.ShouldContain("does not exist");
        ProtoSchemaLoader.Load([TestSchema.ProtoRoot], []).Diagnostics.ShouldHaveSingleItem().Message.ShouldContain("No proto files");
    }
}
