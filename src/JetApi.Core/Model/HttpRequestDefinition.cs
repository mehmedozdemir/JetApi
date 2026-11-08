namespace JetApi.Core.Model;

/// <summary>A saved REST/HTTP request.</summary>
public sealed record HttpRequestDefinition : RequestNode
{
    /// <summary>HTTP method, e.g. GET. Custom methods are allowed.</summary>
    public string Method { get; init; } = "GET";

    /// <summary>Request URL; may contain a query string and <c>{{variable}}</c> placeholders.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Query parameters appended to <see cref="Url"/>.</summary>
    public IReadOnlyList<KeyValueEntry> QueryParameters { get; init; } = [];

    /// <summary>Request headers.</summary>
    public IReadOnlyList<KeyValueEntry> Headers { get; init; } = [];

    /// <summary>Request body.</summary>
    public HttpBody Body { get; init; } = HttpBody.Empty;

    /// <summary>Transport options.</summary>
    public HttpRequestSettings Settings { get; init; } = HttpRequestSettings.Default;
}

/// <summary>Per-request transport options.</summary>
/// <param name="FollowRedirects">Follow 3xx responses automatically.</param>
/// <param name="ValidateCertificates">Reject invalid TLS certificates. Turn off only for local development servers.</param>
/// <param name="Timeout">Overall request timeout; <c>null</c> uses the application default.</param>
public sealed record HttpRequestSettings(bool FollowRedirects = true, bool ValidateCertificates = true, TimeSpan? Timeout = null)
{
    /// <summary>Default settings.</summary>
    public static HttpRequestSettings Default { get; } = new();
}
