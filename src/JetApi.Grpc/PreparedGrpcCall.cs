using JetApi.Core.Model;
using JetApi.Grpc.Schema;

namespace JetApi.Grpc;

/// <summary>A unary call with every variable resolved and the request message encoded.</summary>
/// <param name="ServerUrl">Server address; <c>http</c> means plaintext HTTP/2.</param>
/// <param name="Method">The method to call.</param>
/// <param name="RequestMessage">Encoded request message.</param>
/// <param name="Metadata">Request metadata in order; keys are lowercase.</param>
/// <param name="Settings">Call options.</param>
public sealed record PreparedGrpcCall(
    Uri ServerUrl,
    ProtoMethod Method,
    ReadOnlyMemory<byte> RequestMessage,
    IReadOnlyList<KeyValuePair<string, string>> Metadata,
    GrpcRequestSettings Settings);
