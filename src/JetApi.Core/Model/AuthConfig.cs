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

/// <summary>How an OAuth 2.0 access token is obtained.</summary>
public enum OAuth2GrantType
{
    /// <summary>Client credentials (RFC 6749 §4.4): the application acts on its own behalf.</summary>
    ClientCredentials,

    /// <summary>Authorization code (RFC 6749 §4.1), optionally with PKCE (RFC 7636): the user signs in in a browser.</summary>
    AuthorizationCode,

    /// <summary>Resource owner password credentials (RFC 6749 §4.3).</summary>
    Password,
}

/// <summary>How the client id and secret are sent to the token endpoint.</summary>
public enum OAuth2ClientAuthentication
{
    /// <summary><c>Authorization: Basic</c> header (RFC 6749 §2.3.1, the recommended way).</summary>
    BasicHeader,

    /// <summary><c>client_id</c> and <c>client_secret</c> in the form body.</summary>
    Body,
}

/// <summary>
/// OAuth 2.0: a token is obtained from <see cref="TokenUrl"/> and sent as <c>Authorization: Bearer &lt;token&gt;</c>.
/// Every string may contain <c>{{variable}}</c> placeholders; use <see cref="Resolve"/> before talking to the server.
/// Tokens themselves are never part of the model (see ADR-0007).
/// </summary>
/// <param name="GrantType">How the token is obtained.</param>
/// <param name="TokenUrl">Token endpoint.</param>
/// <param name="ClientId">Client id.</param>
/// <param name="ClientSecret">Client secret; empty for public clients.</param>
/// <param name="Scope">Space-separated scopes; empty to use the server's default.</param>
/// <param name="Audience">Optional <c>audience</c> parameter (Auth0 and others); empty to omit.</param>
/// <param name="AuthorizationUrl">Authorization endpoint (authorization code grant only).</param>
/// <param name="Username">Resource owner user name (password grant only).</param>
/// <param name="Password">Resource owner password (password grant only).</param>
/// <param name="RedirectUri">
/// Loopback redirect URI for the authorization code grant. Port <c>0</c> means any free port
/// (RFC 8252 §7.3); see <see cref="DefaultRedirectUri"/>.
/// </param>
/// <param name="UsePkce">Whether the authorization code grant uses PKCE with S256.</param>
/// <param name="ClientAuthentication">How client credentials are sent to the token endpoint.</param>
public sealed record OAuth2Auth(
    OAuth2GrantType GrantType = OAuth2GrantType.ClientCredentials,
    string TokenUrl = "",
    string ClientId = "",
    string ClientSecret = "",
    string Scope = "",
    string Audience = "",
    string AuthorizationUrl = "",
    string Username = "",
    string Password = "",
    string RedirectUri = OAuth2Auth.DefaultRedirectUri,
    bool UsePkce = true,
    OAuth2ClientAuthentication ClientAuthentication = OAuth2ClientAuthentication.BasicHeader) : AuthConfig
{
    /// <summary>The default redirect URI: a free loopback port chosen when the sign-in starts.</summary>
    public const string DefaultRedirectUri = "http://127.0.0.1:0/callback";

    /// <summary>Returns a copy with every string passed through <paramref name="resolve"/> (typically variable resolution).</summary>
    /// <param name="resolve">Resolves one template.</param>
    public OAuth2Auth Resolve(Func<string, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        return this with
        {
            TokenUrl = resolve(TokenUrl).Trim(),
            ClientId = resolve(ClientId),
            ClientSecret = resolve(ClientSecret),
            Scope = resolve(Scope).Trim(),
            Audience = resolve(Audience).Trim(),
            AuthorizationUrl = resolve(AuthorizationUrl).Trim(),
            Username = resolve(Username),
            Password = resolve(Password),
            RedirectUri = resolve(RedirectUri).Trim(),
        };
    }

    /// <summary>
    /// A stable key for caching the token of this (resolved) configuration: a SHA-256 hash of the grant, token URL,
    /// client id, scope, audience and user name. Different environments therefore never share a token, and
    /// the key reveals none of the values.
    /// </summary>
    public string GetTokenCacheKey()
    {
        var identity = string.Join(
            '\n',
            GrantType.ToString(),
            TokenUrl,
            ClientId,
            Scope,
            Audience,
            GrantType == OAuth2GrantType.Password ? Username : string.Empty);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
    }
}
