namespace JetApi.Grpc.Codec;

/// <summary>A JSON message does not match its protobuf type, or binary data does not match the schema.</summary>
public sealed class ProtoJsonException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ProtoJsonException()
    {
        Path = "$";
    }

    /// <summary>Creates the exception.</summary>
    public ProtoJsonException(string message)
        : base(message)
    {
        Path = "$";
    }

    /// <summary>Creates the exception.</summary>
    public ProtoJsonException(string message, Exception innerException)
        : base(message, innerException)
    {
        Path = "$";
    }

    /// <summary>Creates the exception for a location in the JSON document.</summary>
    /// <param name="path">JSON path of the offending value, e.g. <c>$.user.roles[2]</c>.</param>
    /// <param name="message">Description.</param>
    public ProtoJsonException(string path, string message)
        : base($"{path}: {message}")
    {
        Path = path;
        Detail = message;
    }

    /// <summary>JSON path of the offending value.</summary>
    public string Path { get; }

    /// <summary>The message without the path prefix.</summary>
    public string? Detail { get; }
}
