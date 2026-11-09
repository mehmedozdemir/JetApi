using System.Net;

namespace JetApi.Http;

/// <summary>Keeps cookies between requests in a <see cref="CookieContainer"/> (the app's jar, or one CLI run).</summary>
public static class HttpCookies
{
    /// <summary>Adds the jar's cookies for the URL; a <c>Cookie</c> header written on the request wins over the jar.</summary>
    public static PreparedHttpRequest Apply(PreparedHttpRequest request, CookieContainer jar)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(jar);
        if (request.Headers.Any(h => h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)))
        {
            return request;
        }

        var header = jar.GetCookieHeader(request.Url);
        return header.Length == 0 ? request : request with { Headers = [.. request.Headers, new("Cookie", header)] };
    }

    /// <summary>
    /// Keeps the <c>Set-Cookie</c> headers of the final response (redirect hops are followed by the HTTP stack and not
    /// seen here). A malformed cookie is ignored, as browsers do.
    /// </summary>
    public static void Keep(HttpExecutionResult result, Uri requestUrl, CookieContainer jar)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(jar);
        var url = result.FinalUrl ?? requestUrl;
        foreach (var header in result.Headers.Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                jar.SetCookies(url, header.Value);
            }
            catch (CookieException)
            {
                // Ignored, see the summary.
            }
        }
    }
}
