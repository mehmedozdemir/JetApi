namespace JetApi.Core.Model;

/// <summary>A saved unary gRPC call. Service and message types come from the collection's proto settings.</summary>
public sealed record GrpcRequestDefinition : RequestNode
{
    /// <summary>
    /// Server address including scheme: <c>https://host:port</c> for TLS or <c>http://host:port</c>
    /// for plaintext (h2c). May contain <c>{{variable}}</c> placeholders.
    /// </summary>
    public string ServerUrl { get; init; } = string.Empty;

    /// <summary>Fully qualified service name, e.g. <c>users.v1.UserService</c>.</summary>
    public string Service { get; init; } = string.Empty;

    /// <summary>Method name, e.g. <c>GetUser</c>.</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>Request message as proto3 JSON. May contain <c>{{variable}}</c> placeholders.</summary>
    public string Message { get; init; } = "{}";

    /// <summary>Request metadata (headers).</summary>
    public IReadOnlyList<KeyValueEntry> Metadata { get; init; } = [];

    /// <summary>Call options.</summary>
    public GrpcRequestSettings Settings { get; init; } = GrpcRequestSettings.Default;
}

/// <summary>Per-call gRPC options.</summary>
/// <param name="ValidateCertificates">Reject invalid TLS certificates.</param>
/// <param name="Deadline">Call deadline relative to the start of the call; <c>null</c> uses the application default.</param>
public sealed record GrpcRequestSettings(bool ValidateCertificates = true, TimeSpan? Deadline = null)
{
    /// <summary>Default settings.</summary>
    public static GrpcRequestSettings Default { get; } = new();
}

/// <summary>Where the protobuf schema of a collection comes from.</summary>
/// <param name="ImportPaths">Directories searched for <c>import</c> statements, relative to the workspace root.</param>
/// <param name="Files">Proto files to load, relative to one of the import paths.</param>
public sealed record ProtoSettings(IReadOnlyList<string> ImportPaths, IReadOnlyList<string> Files)
{
    /// <summary>No proto files configured.</summary>
    public static ProtoSettings Empty { get; } = new([], []);
}
