using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using JetApi.Core.Model;

namespace JetApi.Http;

/// <summary>
/// Talks to OAuth 2.0 token endpoints (RFC 6749): client credentials, password, authorization code (with PKCE) and
/// refresh. Every method takes an <see cref="OAuth2Auth"/> whose variables are already resolved
/// (<see cref="OAuth2Auth.Resolve"/>). Server, network and input errors come back in <see cref="OAuth2TokenResult"/>;
/// nothing is thrown for them. Thread-safe; one instance can live for the whole application.
/// </summary>
public sealed class OAuth2Client : IDisposable
{
    /// <summary>Default time limit of one token request.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private const int MaxResponseChars = 1024 * 1024;
    private const int MaxReportedBodyChars = 2000;

    private readonly HttpMessageInvoker _invoker;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;

    /// <summary>Creates a client.</summary>
    /// <param name="handler">
    /// Transport to use, e.g. one that trusts a development certificate; it is not disposed. When <c>null</c>, the client
    /// owns a handler that does not follow redirects and sends no cookies.
    /// </param>
    /// <param name="timeProvider">Clock that turns <c>expires_in</c> into <see cref="OAuth2Token.ExpiresAt"/>.</param>
    /// <param name="timeout">Time limit of one token request; <see cref="DefaultTimeout"/> when <c>null</c>.</param>
    public OAuth2Client(HttpMessageHandler? handler = null, TimeProvider? timeProvider = null, TimeSpan? timeout = null)
    {
        _invoker = handler is null
            ? new HttpMessageInvoker(
                new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    AutomaticDecompression = DecompressionMethods.All,
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                },
                disposeHandler: true)
            : new HttpMessageInvoker(handler, disposeHandler: false);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>
    /// Gets a token with the grant of <paramref name="auth"/>. The authorization code grant signs the user in through
    /// the browser (<see cref="AuthorizationCodeFlow"/>), so it needs <paramref name="openBrowser"/>.
    /// </summary>
    /// <param name="auth">The auth with variables resolved.</param>
    /// <param name="openBrowser">Opens the authorization URL in the user's browser (authorization code grant only).</param>
    /// <param name="signInTimeout">How long to wait for the browser sign-in; <see cref="AuthorizationCodeFlow.DefaultTimeout"/> when <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the whole operation.</param>
    public async Task<OAuth2TokenResult> GetTokenAsync(
        OAuth2Auth auth,
        Func<Uri, Task>? openBrowser = null,
        TimeSpan? signInTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auth);
        if (auth.GrantType != OAuth2GrantType.AuthorizationCode)
        {
            return await RequestTokenAsync(auth, cancellationToken).ConfigureAwait(false);
        }

        if (openBrowser is null)
        {
            return OAuth2TokenResult.Failure(Messages.Get("OAuth_BrowserRequired"));
        }

        var authorization = await AuthorizationCodeFlow.RunAsync(auth, openBrowser, signInTimeout, cancellationToken).ConfigureAwait(false);
        if (!authorization.IsSuccess)
        {
            return new OAuth2TokenResult
            {
                Error = authorization.Error,
                ErrorDescription = authorization.ErrorDescription,
                ErrorMessage = authorization.ErrorMessage,
            };
        }

        return await ExchangeCodeAsync(auth, authorization.Code!, authorization.CodeVerifier, authorization.RedirectUri!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests a token with the client credentials or password grant.</summary>
    /// <param name="auth">The auth with variables resolved.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<OAuth2TokenResult> RequestTokenAsync(OAuth2Auth auth, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auth);
        switch (auth.GrantType)
        {
            case OAuth2GrantType.ClientCredentials:
                if (auth.ClientId.Length == 0)
                {
                    return Task.FromResult(OAuth2TokenResult.Failure(Messages.Get("OAuth_ClientIdRequired")));
                }

                return SendAsync(auth, [new("grant_type", "client_credentials"), .. ScopeAndAudience(auth)], null, cancellationToken);
            case OAuth2GrantType.Password:
                if (auth.Username.Length == 0)
                {
                    return Task.FromResult(OAuth2TokenResult.Failure(Messages.Get("OAuth_UsernameRequired")));
                }

                return SendAsync(
                    auth,
                    [new("grant_type", "password"), new("username", auth.Username), new("password", auth.Password), .. ScopeAndAudience(auth)],
                    null,
                    cancellationToken);
            default:
                return Task.FromResult(OAuth2TokenResult.Failure(Messages.Get("OAuth_BrowserRequired")));
        }
    }

    /// <summary>Exchanges an authorization code for a token (RFC 6749 §4.1.3, RFC 7636 §4.5).</summary>
    /// <param name="auth">The auth with variables resolved.</param>
    /// <param name="code">The authorization code.</param>
    /// <param name="codeVerifier">The PKCE code verifier, or <c>null</c> without PKCE.</param>
    /// <param name="redirectUri">The exact redirect URI sent to the authorization endpoint.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
#pragma warning disable CA1054 // The redirect URI is compared as an exact string by servers; a Uri could normalize it.
    public Task<OAuth2TokenResult> ExchangeCodeAsync(OAuth2Auth auth, string code, string? codeVerifier, string redirectUri, CancellationToken cancellationToken = default)
#pragma warning restore CA1054
    {
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentNullException.ThrowIfNull(redirectUri);

        List<KeyValuePair<string, string>> fields = [new("grant_type", "authorization_code"), new("code", code), new("redirect_uri", redirectUri)];
        if (codeVerifier is not null)
        {
            fields.Add(new("code_verifier", codeVerifier));
        }

        return SendAsync(auth, fields, null, cancellationToken);
    }

    /// <summary>
    /// Gets a new token with a refresh token (RFC 6749 §6). When the server does not issue a new refresh token,
    /// the old one is kept in the result.
    /// </summary>
    /// <param name="auth">The auth with variables resolved.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public Task<OAuth2TokenResult> RefreshAsync(OAuth2Auth auth, string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);
        return SendAsync(auth, [new("grant_type", "refresh_token"), new("refresh_token", refreshToken)], refreshToken, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _invoker.Dispose();

    private static IEnumerable<KeyValuePair<string, string>> ScopeAndAudience(OAuth2Auth auth)
    {
        if (auth.Scope.Length > 0)
        {
            yield return new("scope", auth.Scope);
        }

        if (auth.Audience.Length > 0)
        {
            yield return new("audience", auth.Audience);
        }
    }

    private async Task<OAuth2TokenResult> SendAsync(
        OAuth2Auth auth,
        List<KeyValuePair<string, string>> fields,
        string? previousRefreshToken,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(auth.TokenUrl, UriKind.Absolute, out var tokenUrl) || (tokenUrl.Scheme != Uri.UriSchemeHttp && tokenUrl.Scheme != Uri.UriSchemeHttps))
        {
            return OAuth2TokenResult.Failure(Messages.Format("OAuth_InvalidTokenUrl", auth.TokenUrl));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // A public client (no secret) identifies itself in the body (RFC 6749 §4.1.3, §2.3.1).
        if (auth.ClientAuthentication == OAuth2ClientAuthentication.BasicHeader && auth.ClientSecret.Length > 0)
        {
            // RFC 6749 §2.3.1: both values are form-encoded before Base64.
            var credentials = WebUtility.UrlEncode(auth.ClientId) + ":" + WebUtility.UrlEncode(auth.ClientSecret);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        }
        else if (auth.ClientId.Length > 0)
        {
            fields.Add(new("client_id", auth.ClientId));
            if (auth.ClientSecret.Length > 0)
            {
                fields.Add(new("client_secret", auth.ClientSecret));
            }
        }

        request.Content = new FormUrlEncodedContent(fields);

        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var response = await _invoker.SendAsync(request, linked.Token).ConfigureAwait(false);
            var body = await ReadBodyAsync(response.Content, linked.Token).ConfigureAwait(false);
            return Interpret(response, body, auth, previousRefreshToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OAuth2TokenResult.Failure(Messages.Get("OAuth_Cancelled"));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return OAuth2TokenResult.Failure(Messages.Format("OAuth_TimedOut", _timeout.TotalMilliseconds.ToString("0", CultureInfo.CurrentCulture)));
        }
        catch (HttpRequestException exception)
        {
            return OAuth2TokenResult.Failure(exception.InnerException switch
            {
                SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData } => Messages.Get("OAuth_Dns"),
                SocketException { SocketErrorCode: SocketError.ConnectionRefused } => Messages.Get("OAuth_Refused"),
                AuthenticationException tls => Messages.Format("OAuth_Tls", (tls.InnerException ?? tls).Message),
                _ => Messages.Format("OAuth_Network", exception.Message),
            });
        }
        catch (IOException exception)
        {
            return OAuth2TokenResult.Failure(Messages.Format("OAuth_Network", exception.Message));
        }
    }

    private static async Task<string> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = new StringBuilder();
            var chunk = new char[8192];
            int read;
            while (text.Length < MaxResponseChars && (read = await reader.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                text.Append(chunk, 0, read);
            }

            return text.ToString();
        }
    }

    private OAuth2TokenResult Interpret(HttpResponseMessage response, string body, OAuth2Auth auth, string? previousRefreshToken)
    {
        var fields = Parse(body, response.Content.Headers.ContentType?.MediaType);
        var status = response.StatusCode;

        if (fields.TryGetValue("error", out var error) && error.Length > 0)
        {
            var description = fields.GetValueOrDefault("error_description");
            return new OAuth2TokenResult
            {
                Error = error,
                ErrorDescription = description,
                ErrorMessage = OAuth2Messages.ServerError(error, description),
                StatusCode = status,
                ResponseBody = Truncate(body),
            };
        }

        if (!response.IsSuccessStatusCode || !fields.TryGetValue("access_token", out var accessToken) || accessToken.Length == 0)
        {
            return OAuth2TokenResult.Failure(
                Messages.Format("OAuth_NoAccessToken", ((int)status).ToString(CultureInfo.CurrentCulture)),
                status,
                Truncate(body));
        }

        DateTimeOffset? expiresAt = fields.TryGetValue("expires_in", out var expiresIn)
            && double.TryParse(expiresIn, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0
                ? _timeProvider.GetUtcNow().AddSeconds(seconds)
                : null;

        var scope = fields.GetValueOrDefault("scope");
        return new OAuth2TokenResult
        {
            Token = new OAuth2Token(
                accessToken,
                fields.GetValueOrDefault("token_type") is { Length: > 0 } type ? type : "Bearer",
                expiresAt,
                fields.GetValueOrDefault("refresh_token") is { Length: > 0 } refresh ? refresh : previousRefreshToken,

                // RFC 6749 §5.1: an omitted scope means the requested one.
                scope is { Length: > 0 } ? scope : auth.Scope.Length > 0 ? auth.Scope : null),
            StatusCode = status,
        };
    }

    // Token responses are JSON (RFC 6749 §5.1). A few older servers (e.g. GitHub without Accept) answer form-encoded.
    private static Dictionary<string, string> Parse(string body, string? mediaType)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.Equals(mediaType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=', StringComparison.Ordinal);
                var name = WebUtility.UrlDecode(separator < 0 ? pair : pair[..separator]);
                fields[name] = separator < 0 ? string.Empty : WebUtility.UrlDecode(pair[(separator + 1)..]);
            }

            return fields;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return fields;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                fields[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
                    _ => string.Empty,
                };
            }
        }
        catch (JsonException)
        {
            // Not JSON (an HTML error page, for example); reported as "no access token" with the body.
        }

        return fields;
    }

    private static string? Truncate(string body) =>
        body.Length == 0 ? null : body.Length <= MaxReportedBodyChars ? body : body[..MaxReportedBodyChars] + "…";
}

/// <summary>Localized descriptions of OAuth error codes, shared by the token client and the browser flow.</summary>
internal static class OAuth2Messages
{
    public static string ServerError(string error, string? description)
    {
        var message = string.IsNullOrWhiteSpace(description)
            ? Messages.Format("OAuth_ServerError", error)
            : Messages.Format("OAuth_ServerErrorWithDescription", error, description);
        var hint = error switch
        {
            "invalid_client" or "unauthorized_client" => Messages.Get("OAuth_HintInvalidClient"),
            "invalid_grant" => Messages.Get("OAuth_HintInvalidGrant"),
            "invalid_scope" => Messages.Get("OAuth_HintInvalidScope"),
            "unsupported_grant_type" => Messages.Get("OAuth_HintUnsupportedGrant"),
            "access_denied" => Messages.Get("OAuth_HintAccessDenied"),
            _ => null,
        };
        return hint is null ? message : message + " " + hint;
    }
}
