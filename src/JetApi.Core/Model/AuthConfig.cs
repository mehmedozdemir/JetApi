namespace JetApi.Core.Model;

/// <summary>
/// Authentication settings of a request, folder or collection. Requests default to
/// <see cref="InheritAuth"/>, so credentials are usually configured once on the collection.
/// All string values may contain <c>{{variable}}</c> placeholders.
/// </summary>
public abstract record AuthConfig
{
    /// <summary>Use the auth of the closest parent folder or collection.</summary>
    public static AuthConfig Inherit { get; } = new InheritAuth();

    /// <summary>Send no authentication.</summary>
    public static AuthConfig None { get; } = new NoAuth();
}

/// <summary>Use the auth of the closest ancestor that defines one.</summary>
public sealed record InheritAuth : AuthConfig;

/// <summary>Explicitly send no authentication.</summary>
public sealed record NoAuth : AuthConfig;

/// <summary><c>Authorization: Bearer &lt;token&gt;</c>.</summary>
public sealed record BearerAuth(string Token) : AuthConfig;

/// <summary><c>Authorization: Basic base64(username:password)</c>.</summary>
public sealed record BasicAuth(string Username, string Password) : AuthConfig;

/// <summary>Where an API key is sent.</summary>
public enum ApiKeyLocation
{
    /// <summary>As a request header (gRPC: metadata).</summary>
    Header,

    /// <summary>As a URL query parameter (HTTP only).</summary>
    Query,
}

/// <summary>An API key sent as a header or query parameter.</summary>
public sealed record ApiKeyAuth(string Key, string Value, ApiKeyLocation Location = ApiKeyLocation.Header) : AuthConfig;
