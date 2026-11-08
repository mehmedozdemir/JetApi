using System.Net;

namespace JetApi.Http;

/// <summary>How an execution ended.</summary>
public enum HttpExecutionOutcome
{
    /// <summary>A response was received (any status code).</summary>
    Completed,

    /// <summary>No response: DNS failure, connection refused, TLS error, ...</summary>
    Failed,

    /// <summary>The request timeout elapsed.</summary>
    TimedOut,

    /// <summary>The user cancelled the request.</summary>
    Cancelled,
}

/// <summary>
/// Where the time went. <see cref="Dns"/> and <see cref="Connect"/> are <c>null</c> when an
/// existing connection was reused. <see cref="TimeToFirstByte"/> is measured from the start and
/// therefore includes DNS, TCP connect, the TLS handshake and server processing.
/// </summary>
/// <param name="Dns">Host name resolution.</param>
/// <param name="Connect">TCP connection establishment.</param>
/// <param name="TimeToFirstByte">From start until response headers were received.</param>
/// <param name="Download">Reading the response body.</param>
/// <param name="Total">From start until the body was read.</param>
public sealed record HttpTimings(TimeSpan? Dns, TimeSpan? Connect, TimeSpan TimeToFirstByte, TimeSpan Download, TimeSpan Total)
{
    /// <summary>True when the request reused a pooled connection.</summary>
    public bool IsConnectionReused => Connect is null;
}

/// <summary>What was actually sent, including headers added by the transport (e.g. <c>Accept-Encoding</c>).</summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Url">Request URL.</param>
/// <param name="Headers">Request and content headers as sent; <c>Host</c> is derived from the URL and not listed.</param>
/// <param name="BodySize">Body length in bytes, when known.</param>
public sealed record SentHttpRequest(string Method, Uri Url, IReadOnlyList<KeyValuePair<string, string>> Headers, long? BodySize);

/// <summary>The result of sending a request.</summary>
public sealed record HttpExecutionResult
{
    /// <summary>How the execution ended.</summary>
    public required HttpExecutionOutcome Outcome { get; init; }

    /// <summary>The request as sent.</summary>
    public required SentHttpRequest Sent { get; init; }

    /// <summary>Response status code; <c>null</c> unless <see cref="Outcome"/> is <see cref="HttpExecutionOutcome.Completed"/>.</summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>Reason phrase sent by the server, e.g. <c>Not Found</c>.</summary>
    public string? ReasonPhrase { get; init; }

    /// <summary>Negotiated protocol version, e.g. 1.1 or 2.0.</summary>
    public Version? HttpVersion { get; init; }

    /// <summary>URL of the final response after redirects.</summary>
    public Uri? FinalUrl { get; init; }

    /// <summary>Response, content and trailing headers in received order; duplicates preserved.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>The response body after content decoding (gzip, deflate, brotli).</summary>
    public ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>True when the body exceeded the configured maximum and was cut off.</summary>
    public bool IsBodyTruncated { get; init; }

    /// <summary>Response <c>Content-Type</c>, if any.</summary>
    public string? ContentType { get; init; }

    /// <summary>Timing breakdown; <c>null</c> when nothing was sent.</summary>
    public HttpTimings? Timings { get; init; }

    /// <summary>The remote IP endpoint of a newly opened connection.</summary>
    public string? RemoteEndPoint { get; init; }

    /// <summary>Why the request failed, in user-facing language.</summary>
    public string? ErrorMessage { get; init; }
}
