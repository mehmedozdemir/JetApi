namespace JetApi.Http;

/// <summary>Application-wide HTTP engine options.</summary>
public sealed record HttpExecutorOptions
{
    /// <summary>Default timeout when a request does not set one.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Response bodies larger than this are truncated to protect memory.</summary>
    public long MaxResponseBodyBytes { get; init; } = 50L * 1024 * 1024;

    /// <summary>Maximum number of redirects followed when redirects are enabled.</summary>
    public int MaxRedirects { get; init; } = 10;

    /// <summary><c>User-Agent</c> sent when the request does not set one.</summary>
    public string UserAgent { get; init; } = "JetApi/" + (typeof(HttpExecutorOptions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
}
