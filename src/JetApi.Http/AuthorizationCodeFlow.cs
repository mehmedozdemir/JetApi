using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using JetApi.Core.Model;

namespace JetApi.Http;

/// <summary>
/// The browser part of the authorization code grant for a native app (RFC 8252): builds the authorization URL with
/// <c>state</c> and a PKCE S256 challenge (RFC 7636), waits for the redirect on a loopback address and returns the
/// code. Exchanging the code for a token is <see cref="OAuth2Client.ExchangeCodeAsync"/>.
/// </summary>
/// <remarks>
/// The loopback listener is a minimal HTTP/1.1 server on <see cref="TcpListener"/> rather than <c>HttpListener</c>,
/// which needs a URL reservation for non-<c>localhost</c> prefixes on Windows. It only binds loopback addresses.
/// </remarks>
public static class AuthorizationCodeFlow
{
    /// <summary>Default time the user has to finish signing in.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    private const int MaxRequestBytes = 16 * 1024;
    private static readonly TimeSpan ConnectionReadTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs the sign-in: starts listening, calls <paramref name="openBrowser"/> with the authorization URL and waits
    /// for the redirect. Input, server and timeout problems come back in the result; exceptions thrown by
    /// <paramref name="openBrowser"/> propagate.
    /// </summary>
    /// <param name="auth">The auth with variables resolved; uses the authorization URL, client id, scope, audience, redirect URI and PKCE setting.</param>
    /// <param name="openBrowser">Opens the URL in the user's browser.</param>
    /// <param name="timeout">How long to wait for the redirect; <see cref="DefaultTimeout"/> when <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    public static async Task<AuthorizationCodeResult> RunAsync(
        OAuth2Auth auth,
        Func<Uri, Task> openBrowser,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(openBrowser);

        if (!Uri.TryCreate(auth.AuthorizationUrl, UriKind.Absolute, out var authorizationUrl)
            || (authorizationUrl.Scheme != Uri.UriSchemeHttp && authorizationUrl.Scheme != Uri.UriSchemeHttps))
        {
            return AuthorizationCodeResult.Failure(Messages.Format("OAuth_InvalidAuthorizationUrl", auth.AuthorizationUrl));
        }

        if (auth.ClientId.Length == 0)
        {
            return AuthorizationCodeResult.Failure(Messages.Get("OAuth_ClientIdRequired"));
        }

        if (!Uri.TryCreate(auth.RedirectUri, UriKind.Absolute, out var redirect) || redirect.Scheme != Uri.UriSchemeHttp || !redirect.IsLoopback)
        {
            return AuthorizationCodeResult.Failure(Messages.Format("OAuth_InvalidRedirectUri", auth.RedirectUri));
        }

        var listeners = new List<TcpListener>();
        try
        {
            int port;
            try
            {
                port = Listen(redirect, listeners);
            }
            catch (SocketException exception)
            {
                return AuthorizationCodeResult.Failure(Messages.Format("OAuth_ListenFailed", auth.RedirectUri, exception.Message));
            }

            var redirectUri = new UriBuilder(redirect) { Port = port }.Uri.AbsoluteUri;
            var state = RandomToken();
            var codeVerifier = auth.UsePkce ? CreateCodeVerifier() : null;
            var authorizationUri = BuildAuthorizationUri(auth, redirectUri, state, codeVerifier is null ? null : CreateCodeChallenge(codeVerifier));

            var callback = new TaskCompletionSource<AuthorizationCodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var limit = timeout ?? DefaultTimeout;
            using var timer = new CancellationTokenSource(limit);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);
            var server = new CallbackServer(redirect.AbsolutePath, state, codeVerifier, redirectUri, callback);
            var loops = listeners.Select(listener => server.AcceptAsync(listener, linked.Token)).ToList();
            try
            {
                await openBrowser(authorizationUri).ConfigureAwait(false);
                return await callback.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return AuthorizationCodeResult.Failure(Messages.Get("OAuth_Cancelled"));
            }
            catch (OperationCanceledException) when (timer.IsCancellationRequested)
            {
                return AuthorizationCodeResult.Failure(Messages.Format("OAuth_SignInTimedOut", limit.TotalSeconds.ToString("0", CultureInfo.CurrentCulture)));
            }
            finally
            {
                await linked.CancelAsync().ConfigureAwait(false);
                StopAll(listeners);
                await Task.WhenAll(loops).ConfigureAwait(false);
            }
        }
        finally
        {
            StopAll(listeners);
        }
    }

    /// <summary>Builds the authorization request URL (RFC 6749 §4.1.1), keeping any query the URL already has.</summary>
    /// <param name="auth">The auth with variables resolved.</param>
    /// <param name="redirectUri">The redirect URI with the actual port.</param>
    /// <param name="state">The anti-forgery state.</param>
    /// <param name="codeChallenge">The PKCE S256 challenge, or <c>null</c> without PKCE.</param>
#pragma warning disable CA1054 // The redirect URI is compared as an exact string by servers; a Uri could normalize it.
    public static Uri BuildAuthorizationUri(OAuth2Auth auth, string redirectUri, string state, string? codeChallenge)
#pragma warning restore CA1054
    {
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentNullException.ThrowIfNull(state);

        List<KeyValuePair<string, string>> parameters =
        [
            new("response_type", "code"),
            new("client_id", auth.ClientId),
            new("redirect_uri", redirectUri),
        ];
        if (auth.Scope.Length > 0)
        {
            parameters.Add(new("scope", auth.Scope));
        }

        if (auth.Audience.Length > 0)
        {
            parameters.Add(new("audience", auth.Audience));
        }

        parameters.Add(new("state", state));
        if (codeChallenge is not null)
        {
            parameters.Add(new("code_challenge", codeChallenge));
            parameters.Add(new("code_challenge_method", "S256"));
        }

        var url = auth.AuthorizationUrl;
        var fragment = url.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0)
        {
            url = url[..fragment];
        }

        var separator = !url.Contains('?', StringComparison.Ordinal) ? "?" : url.EndsWith('?') || url.EndsWith('&') ? string.Empty : "&";
        return new Uri(url + separator + string.Join('&', parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
    }

    /// <summary>A new PKCE code verifier: 32 random bytes, Base64url-encoded (43 characters, RFC 7636 §4.1).</summary>
    public static string CreateCodeVerifier() => RandomToken();

    /// <summary>The S256 code challenge of <paramref name="codeVerifier"/> (RFC 7636 §4.2).</summary>
    /// <param name="codeVerifier">The code verifier.</param>
    public static string CreateCodeChallenge(string codeVerifier)
    {
        ArgumentNullException.ThrowIfNull(codeVerifier);
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
    }

    private static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Binds the redirect host's loopback address(es). "localhost" may resolve to IPv4 or IPv6 in the browser, so both
    // are tried; the first binding decides the port when the redirect URI says port 0.
    private static int Listen(Uri redirect, List<TcpListener> listeners)
    {
        IPAddress[] addresses = IPAddress.TryParse(redirect.IdnHost, out var address)
            ? [address]
            : [IPAddress.Loopback, IPAddress.IPv6Loopback];

        var port = redirect.Port;
        for (var i = 0; i < addresses.Length; i++)
        {
            var listener = new TcpListener(addresses[i], port);
            try
            {
                listener.Start();
            }
            catch (SocketException) when (i > 0)
            {
                // The extra address (IPv6) is best effort.
                continue;
            }

            listeners.Add(listener);
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        return port;
    }

    private static void StopAll(List<TcpListener> listeners)
    {
        foreach (var listener in listeners)
        {
            listener.Stop();
            listener.Dispose();
        }

        listeners.Clear();
    }

    private sealed class CallbackServer(
        string path,
        string state,
        string? codeVerifier,
        string redirectUri,
        TaskCompletionSource<AuthorizationCodeResult> callback)
    {
        public async Task AcceptAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            var connections = new List<Task>();
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);

                    // Browsers open speculative connections that never send a request; serve each one separately.
                    connections.Add(ServeAsync(client, cancellationToken));
                }
            }
            catch (OperationCanceledException)
            {
                // The flow has finished.
            }
            catch (SocketException)
            {
                // The listener was stopped.
            }
            catch (ObjectDisposedException)
            {
                // Same as above.
            }

            await Task.WhenAll(connections).ConfigureAwait(false);
        }

        private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                readTimeout.CancelAfter(ConnectionReadTimeout);
                try
                {
                    var stream = client.GetStream();
                    var target = await ReadRequestTargetAsync(stream, readTimeout.Token).ConfigureAwait(false);
                    if (target is null)
                    {
                        return;
                    }

                    var queryStart = target.IndexOf('?', StringComparison.Ordinal);
                    var requestPath = queryStart < 0 ? target : target[..queryStart];
                    if (!string.Equals(requestPath, path, StringComparison.Ordinal))
                    {
                        // e.g. /favicon.ico
                        await WriteAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not found", readTimeout.Token).ConfigureAwait(false);
                        return;
                    }

                    var result = Evaluate(ParseQuery(queryStart < 0 ? string.Empty : target[(queryStart + 1)..]));
                    await WriteAsync(stream, "200 OK", "text/html; charset=utf-8", Page(result), readTimeout.Token).ConfigureAwait(false);
                    callback.TrySetResult(result);
                }
                catch (OperationCanceledException)
                {
                    // Idle or slow connection, or the flow has finished.
                }
                catch (IOException)
                {
                    // The browser closed the connection.
                }
                catch (SocketException)
                {
                    // Same as above.
                }
            }
        }

        private AuthorizationCodeResult Evaluate(Dictionary<string, string> query)
        {
            // The state ties the answer to this sign-in; anything else may be forged by another local page (RFC 6749 §10.12).
            if (!query.TryGetValue("state", out var returnedState)
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(returnedState), Encoding.UTF8.GetBytes(state)))
            {
                return AuthorizationCodeResult.Failure(Messages.Get("OAuth_StateMismatch"));
            }

            if (query.TryGetValue("error", out var error) && error.Length > 0)
            {
                var description = query.GetValueOrDefault("error_description");
                return new AuthorizationCodeResult
                {
                    Error = error,
                    ErrorDescription = description,
                    ErrorMessage = OAuth2Messages.ServerError(error, description),
                };
            }

            if (!query.TryGetValue("code", out var code) || code.Length == 0)
            {
                return AuthorizationCodeResult.Failure(Messages.Get("OAuth_NoCode"));
            }

            return new AuthorizationCodeResult { Code = code, CodeVerifier = codeVerifier, RedirectUri = redirectUri };
        }

        private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[MaxRequestBytes];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return null;
                }

                length += read;
                if (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0)
                {
                    break;
                }
            }

            // Request line: GET /callback?code=...&state=... HTTP/1.1
            var head = Encoding.ASCII.GetString(buffer, 0, length);
            var lineEnd = head.IndexOf("\r\n", StringComparison.Ordinal);
            var parts = (lineEnd < 0 ? head : head[..lineEnd]).Split(' ');
            return parts.Length == 3 && parts[0] == "GET" && parts[1].StartsWith('/') ? parts[1] : null;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=', StringComparison.Ordinal);
                var name = WebUtility.UrlDecode(separator < 0 ? pair : pair[..separator]);

                // A repeated parameter is suspicious; keep the first one, as OAuth parameters must not repeat.
                values.TryAdd(name, separator < 0 ? string.Empty : WebUtility.UrlDecode(pair[(separator + 1)..]));
            }

            return values;
        }

        private static string Page(AuthorizationCodeResult result)
        {
            var title = Html(Messages.Get("OAuth_PageTitle"));
            var heading = Html(result.IsSuccess ? Messages.Get("OAuth_PageSucceeded") : Messages.Get("OAuth_PageFailed"));
            var detail = result.IsSuccess ? string.Empty : $"<p class=\"detail\">{Html(result.ErrorMessage)}</p>";
            var close = Html(Messages.Get("OAuth_PageClose"));
            var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return $$"""
                <!doctype html>
                <html lang="{{language}}">
                <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{title}}</title>
                <style>
                :root { color-scheme: light dark; --bg: #f7f7f8; --fg: #1d1d20; --muted: #5c5c66; --accent: {{(result.IsSuccess ? "#1f7a4d" : "#b3261e")}}; }
                @media (prefers-color-scheme: dark) { :root { --bg: #18181b; --fg: #ececf1; --muted: #a1a1aa; --accent: {{(result.IsSuccess ? "#4ade80" : "#f87171")}}; } }
                body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: var(--bg); color: var(--fg); font: 16px/1.5 system-ui, sans-serif; }
                main { max-width: 32rem; padding: 2rem 16px; text-align: center; }
                h1 { font-size: 1.25rem; margin: 0 0 .5rem; color: var(--accent); }
                p { margin: .25rem 0; color: var(--muted); }
                .detail { color: var(--fg); }
                </style>
                </head>
                <body><main><h1>{{heading}}</h1>{{detail}}<p>{{close}}</p></main></body>
                </html>
                """;
        }

        // WebUtility.HtmlEncode also turns Turkish letters into numeric entities; only markup characters need escaping.
        private static string Html(string? text) =>
            (text ?? string.Empty)
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&#39;", StringComparison.Ordinal);

        private static async Task WriteAsync(NetworkStream stream, string status, string contentType, string body, CancellationToken cancellationToken)
        {
            var content = Encoding.UTF8.GetBytes(body);
            var head = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {content.Length.ToString(CultureInfo.InvariantCulture)}\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
