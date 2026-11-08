using JetApi.Grpc.Schema;

namespace JetApi.Grpc.Tests;

internal static class TestSchema
{
    public static string ProtoRoot { get; } = Path.Combine(AppContext.BaseDirectory, "Protos");

    public static ProtoSchema Load()
    {
        var result = ProtoSchemaLoader.Load([ProtoRoot], ["test/v1/everything.proto"]);
        result.Diagnostics.Where(d => d.IsError).ShouldBeEmpty();
        return result.Schema!;
    }

    public static ProtoMessageType Everything(this ProtoSchema schema) => schema.Messages["jetapi.test.v1.Everything"];
}
