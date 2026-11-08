using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace JetApi.Http.Tests;

/// <summary>
/// A tiny OAuth 2.0 authorization server for the tests. Access tokens describe how they were obtained
/// (<c>grant|client|client-auth|extra</c>), so tests can assert what was sent without shared state.
/// </summary>
internal static class OAuthTestEndpoints
{
    public const string ClientId = "app";
    public const string ClientSecret = "s3cr3t";
    public const string OddClientId = "a b+c";
    public const string OddClientSecret = "p@ss:wörd&=";
    public const string PublicClientId = "spa";
    public const string DenyingClientId = "deny";
    public const string Username = "ayşe";
    public const string Password = "pa ss";
    public const string RefreshToken = "refresh-1";

    private static readonly Dictionary<string, string> Confidential = new(StringComparer.Ordinal)
    {
        [ClientId] = ClientSecret,
        [OddClientId] = OddClientSecret,
    };

    private static readonly ConcurrentDictionary<string, (string ClientId, string RedirectUri, string? Challenge)> Codes = new();

    public static void Map(WebApplication app)
    {
        app.MapGet("/oauth/authorize", (HttpContext context) =>
        {
            var query = context.Request.Query;
            var redirect = query["redirect_uri"].ToString();
            var state = query["state"].ToString();
            var clientId = query["client_id"].ToString();
            if (query["response_type"] != "code")
            {
                return Results.BadRequest("response_type");
            }

            if (clientId == DenyingClientId)
            {
                return Results.Redirect($"{redirect}?error=access_denied&error_description={Uri.EscapeDataString("The user said no")}&state={Uri.EscapeDataString(state)}");
            }

            string? challenge = null;
            if (query.ContainsKey("code_challenge"))
            {
                if (query["code_challenge_method"] != "S256")
                {
                    return Results.BadRequest("code_challenge_method");
                }

                challenge = query["code_challenge"].ToString();
            }

            var code = Guid.NewGuid().ToString("N");
            Codes[code] = (clientId, redirect, challenge);
            return Results.Redirect($"{redirect}?code={code}&state={Uri.EscapeDataString(state)}&scope={Uri.EscapeDataString(query["scope"].ToString())}");
        });

        app.MapPost("/oauth/token", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            var (clientId, clientAuth, clientOk) = AuthenticateClient(context, form);
            if (!clientOk)
            {
                return Error(HttpStatusCode.Unauthorized, "invalid_client", "Client authentication failed.");
            }

            var extra = string.Join(',', new[] { form["scope"].ToString(), form["audience"].ToString() }.Where(v => v.Length > 0));
            switch (form["grant_type"].ToString())
            {
                case "client_credentials" when clientAuth != "none":
                    return Token($"cc|{clientId}|{clientAuth}|{extra}", scope: form["scope"].ToString(), expiresIn: 3600);
                case "password" when form["username"] == Username && form["password"] == Password:
                    return Token($"password|{clientId}|{clientAuth}|{extra}", refresh: RefreshToken, expiresIn: "120");
                case "password":
                    return Error(HttpStatusCode.BadRequest, "invalid_grant", "Wrong user name or password.");
                case "authorization_code":
                    if (!Codes.TryRemove(form["code"].ToString(), out var issued)
                        || issued.ClientId != clientId
                        || issued.RedirectUri != form["redirect_uri"]
                        || (issued.Challenge is not null && issued.Challenge != Challenge(form["code_verifier"].ToString())))
                    {
                        return Error(HttpStatusCode.BadRequest, "invalid_grant", null);
                    }

                    return Token($"code|{clientId}|{clientAuth}|pkce={issued.Challenge is not null}", refresh: RefreshToken, expiresIn: 60);
                case "refresh_token" when form["refresh_token"] == RefreshToken:
                    return Token($"refreshed|{clientId}|{clientAuth}|", expiresIn: 60);
                case "refresh_token":
                    return Error(HttpStatusCode.BadRequest, "invalid_grant", "The refresh token has expired.");
                default:
                    return Error(HttpStatusCode.BadRequest, "unsupported_grant_type", null);
            }
        });

        app.MapPost("/oauth/html", () => Results.Content("<html><body>Gateway error</body></html>", "text/html", statusCode: 502));
        app.MapPost("/oauth/no-token", () => Results.Json(new { token_type = "Bearer" }));
        app.MapPost("/oauth/form", () => Results.Content("access_token=form-token&token_type=bearer&scope=user%20repo", "application/x-www-form-urlencoded"));
        app.MapPost("/oauth/slow", async (CancellationToken token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return Results.Ok();
        });
    }

    private static (string ClientId, string Method, bool Ok) AuthenticateClient(HttpContext context, IFormCollection form)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Basic ", StringComparison.Ordinal))
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
            var separator = decoded.IndexOf(':', StringComparison.Ordinal);
            var id = WebUtility.UrlDecode(decoded[..separator]);
            var secret = WebUtility.UrlDecode(decoded[(separator + 1)..]);
            return (id, "basic", form.ContainsKey("client_secret") is false && Confidential.TryGetValue(id, out var expected) && expected == secret);
        }

        var clientId = form["client_id"].ToString();
        if (form.ContainsKey("client_secret"))
        {
            return (clientId, "body", Confidential.TryGetValue(clientId, out var expected) && expected == form["client_secret"]);
        }

        // Public clients only identify themselves.
        return (clientId, "none", clientId is PublicClientId or DenyingClientId || clientId.Length == 0);
    }

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static IResult Token(string accessToken, string? scope = null, string? refresh = null, object? expiresIn = null)
    {
        var body = new Dictionary<string, object> { ["access_token"] = accessToken, ["token_type"] = "bearer" };
        if (scope is { Length: > 0 })
        {
            body["scope"] = scope;
        }

        if (refresh is not null)
        {
            body["refresh_token"] = refresh;
        }

        if (expiresIn is not null)
        {
            body["expires_in"] = expiresIn;
        }

        return Results.Json(body);
    }

    private static IResult Error(HttpStatusCode status, string error, string? description) =>
        Results.Json(
            description is null ? new Dictionary<string, string> { ["error"] = error } : new Dictionary<string, string> { ["error"] = error, ["error_description"] = description },
            statusCode: (int)status);
}
