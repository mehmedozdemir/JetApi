namespace JetApi.Grpc;

/// <summary>How a call ended.</summary>
public enum GrpcCallOutcome
{
    /// <summary>The server returned a status (OK or an error status).</summary>
    Completed,

    /// <summary>The user cancelled the call.</summary>
    Cancelled,
}

/// <summary>The result of a unary call.</summary>
public sealed record GrpcCallResult
{
    /// <summary>How the call ended.</summary>
    public required GrpcCallOutcome Outcome { get; init; }

    /// <summary>gRPC status code number (0 = OK).</summary>
    public int StatusCode { get; init; }

    /// <summary>gRPC status code name, e.g. <c>OK</c>, <c>NotFound</c>, <c>Unavailable</c>.</summary>
    public string StatusName { get; init; } = string.Empty;

    /// <summary>Status detail sent by the server or describing a transport error.</summary>
    public string StatusDetail { get; init; } = string.Empty;

    /// <summary>The response message as JSON when the status is OK.</summary>
    public string? ResponseJson { get; init; }

    /// <summary>The raw response message.</summary>
    public ReadOnlyMemory<byte> ResponseMessage { get; init; }

    /// <summary>Response headers (initial metadata). Binary values are base64.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders { get; init; } = [];

    /// <summary>Trailers. Binary values are base64.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Trailers { get; init; } = [];

    /// <summary>Time from start until the status was received.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>The request metadata as sent.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> SentMetadata { get; init; } = [];

    /// <summary>True when the status is OK.</summary>
    public bool IsOk => Outcome == GrpcCallOutcome.Completed && StatusCode == 0;
}
