using System.Net;
using JetApi.Core.Model;

namespace JetApi.Http;

/// <summary>The outcome of a token request. Server and user errors are reported here, never thrown.</summary>
public sealed record OAuth2TokenResult
{
    /// <summary>The token; <c>null</c> on failure.</summary>
    public OAuth2Token? Token { get; init; }

    /// <summary>The OAuth error code (RFC 6749 §5.2), e.g. <c>invalid_client</c>, when the server returned one.</summary>
    public string? Error { get; init; }

    /// <summary>The server's <c>error_description</c>, untranslated.</summary>
    public string? ErrorDescription { get; init; }

    /// <summary>What went wrong, in the current UI culture (English or Turkish); <c>null</c> on success.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>HTTP status of the token endpoint's answer, when there was one.</summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>The token endpoint's raw answer (truncated) when it could not be used; never set on success.</summary>
    public string? ResponseBody { get; init; }

    /// <summary>True when <see cref="Token"/> is set.</summary>
    public bool IsSuccess => Token is not null;

    internal static OAuth2TokenResult Failure(string message, HttpStatusCode? status = null, string? body = null) =>
        new() { ErrorMessage = message, StatusCode = status, ResponseBody = body };
}

/// <summary>The outcome of the browser part of the authorization code grant.</summary>
public sealed record AuthorizationCodeResult
{
    /// <summary>The authorization code; <c>null</c> on failure.</summary>
    public string? Code { get; init; }

    /// <summary>The PKCE code verifier to send with the code; <c>null</c> without PKCE.</summary>
    public string? CodeVerifier { get; init; }

    /// <summary>The redirect URI actually used (with the chosen port); the token request must send exactly this.</summary>
    public string? RedirectUri { get; init; }

    /// <summary>The OAuth error code from the authorization server, e.g. <c>access_denied</c>.</summary>
    public string? Error { get; init; }

    /// <summary>The server's <c>error_description</c>, untranslated.</summary>
    public string? ErrorDescription { get; init; }

    /// <summary>What went wrong, in the current UI culture; <c>null</c> on success.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>True when <see cref="Code"/> is set.</summary>
    public bool IsSuccess => Code is not null;

    internal static AuthorizationCodeResult Failure(string message) => new() { ErrorMessage = message };
}
