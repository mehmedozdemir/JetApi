using JetApi.Core.Model;

namespace JetApi.Storage.Tests;

public sealed class RequestFilesTests
{
    [Fact]
    public void SerializeThenDeserialize_RoundTripsHttpAndGrpcRequests()
    {
        var http = new HttpRequestDefinition { Key = "a", Name = "A", Method = "PUT", Url = "{{u}}/x", Body = HttpBody.Json("{\n}") };
        var grpc = new GrpcRequestDefinition { Key = "b", Name = "B", ServerUrl = "http://h:1", Service = "s.S", Method = "M", Message = "{\"id\":1}" };

        var httpCopy = (HttpRequestDefinition)RequestFiles.Deserialize(RequestFiles.Serialize(http), "copy", isGrpc: false);
        var grpcCopy = (GrpcRequestDefinition)RequestFiles.Deserialize(RequestFiles.Serialize(grpc), "b", isGrpc: true);

        (httpCopy.Key, httpCopy.Method, httpCopy.Url, httpCopy.Body.Content).ShouldBe(("copy", "PUT", "{{u}}/x", "{\n}"));
        (grpcCopy.Service, grpcCopy.Method, grpcCopy.Message).ShouldBe(("s.S", "M", "{\"id\":1}"));
    }

    [Fact]
    public void Deserialize_RejectsInvalidYaml() =>
        Should.Throw<InvalidDataException>(() => RequestFiles.Deserialize("schemaVersion: 1\nname: [", "k", isGrpc: false));
}
