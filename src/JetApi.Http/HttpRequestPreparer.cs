using System.Text;
using JetApi.Core.Model;
using JetApi.Core.Variables;

namespace JetApi.Http;

/// <summary>The outcome of preparing a request.</summary>
/// <param name="Request">The prepared request, or <c>null</c> when <see cref="Errors"/> is not empty.</param>
/// <param name="Errors">Problems that prevent sending, in the current UI culture (English or Turkish).</param>
/// <param name="VariableIssues">Placeholders that could not be resolved; they are sent verbatim.</param>
public sealed record HttpPreparationResult(
    PreparedHttpRequest? Request,
    IReadOnlyList<string> Errors,
    IReadOnlyList<VariableIssue> VariableIssues)
{
    /// <summary>True when the request can be sent.</summary>
    public bool IsSuccess => Request is not null;
}

/// <summary>
/// Turns a saved <see cref="HttpRequestDefinition"/> into a <see cref="PreparedHttpRequest"/>:
/// resolves variables in every field, applies auth, builds the URL and validates the result.
/// </summary>
public static class HttpRequestPreparer
{
    private const string JsonContentType = "application/json";
    private const string XmlContentType = "application/xml";
    private const string TextContentType = "text/plain; charset=utf-8";
    private const string BinaryContentType = "application/octet-stream";

    /// <summary>Prepares <paramref name="request"/> for sending.</summary>
    /// <param name="request">The saved request.</param>
    /// <param name="effectiveAuth">The auth that applies, typically from <see cref="AuthResolver"/>.</param>
    /// <param name="variables">Variables of the active environment, collection and workspace.</param>
    /// <param name="workspaceRoot">Base directory for relative file paths in bodies.</param>
    public static HttpPreparationResult Prepare(
        HttpRequestDefinition request,
        AuthConfig effectiveAuth,
        VariableResolver variables,
        string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(effectiveAuth);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        var context = new Context(variables, workspaceRoot);

        var method = context.Resolve(request.Method).Trim().ToUpperInvariant();
        if (method.Length == 0 || !method.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            context.Errors.Add(Messages.Format("Http_InvalidMethod", method));
        }

        var headers = context.ResolveEntries(request.Headers);
        var queryParameters = context.ResolveEntries(request.QueryParameters);
        ApplyAuth(effectiveAuth, context, headers, queryParameters);

        var url = BuildUrl(context.Resolve(request.Url), queryParameters, context);
        var body = PrepareBody(request.Body, context);

        if (context.Errors.Count > 0 || url is null)
        {
            return new HttpPreparationResult(null, context.Errors, context.Issues);
        }

        return new HttpPreparationResult(
            new PreparedHttpRequest(method, url, headers, body, request.Settings),
            [],
            context.Issues);
    }

    private static void ApplyAuth(
        AuthConfig auth,
        Context context,
        List<KeyValuePair<string, string>> headers,
        List<KeyValuePair<string, string>> queryParameters)
    {
        // An explicit Authorization header typed by the user wins over the auth tab.
        var hasAuthorizationHeader = headers.Any(h => h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        switch (auth)
        {
            case BearerAuth bearer when !hasAuthorizationHeader:
                headers.Add(new("Authorization", "Bearer " + context.Resolve(bearer.Token)));
                break;
            case BasicAuth basic when !hasAuthorizationHeader:
                var credentials = context.Resolve(basic.Username) + ":" + context.Resolve(basic.Password);
                headers.Add(new("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials))));
                break;
            case ApiKeyAuth apiKey:
                var entry = new KeyValuePair<string, string>(context.Resolve(apiKey.Key), context.Resolve(apiKey.Value));
                if (apiKey.Location == ApiKeyLocation.Query)
                {
                    queryParameters.Add(entry);
                }
                else
                {
                    headers.Add(entry);
                }

                break;
        }
    }

    private static Uri? BuildUrl(string rawUrl, List<KeyValuePair<string, string>> queryParameters, Context context)
    {
        var url = rawUrl.Trim();
        if (url.Length == 0)
        {
            context.Errors.Add(Messages.Get("Http_EmptyUrl"));
            return null;
        }

        // Like curl and Postman, a URL without a scheme means http.
        if (!url.Contains("://", StringComparison.Ordinal))
        {
            url = "http://" + url;
        }

        if (queryParameters.Count > 0)
        {
            var fragmentIndex = url.IndexOf('#', StringComparison.Ordinal);
            var fragment = fragmentIndex >= 0 ? url[fragmentIndex..] : string.Empty;
            var withoutFragment = fragmentIndex >= 0 ? url[..fragmentIndex] : url;
            var separator = !withoutFragment.Contains('?', StringComparison.Ordinal) ? "?"
                : withoutFragment.EndsWith('?') || withoutFragment.EndsWith('&') ? string.Empty
                : "&";
            var query = string.Join('&', queryParameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
            url = withoutFragment + separator + query + fragment;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            context.Errors.Add(Messages.Format("Http_InvalidUrl", rawUrl));
            return null;
        }

        return uri;
    }

    private static PreparedBody PrepareBody(HttpBody body, Context context) => body.Mode switch
    {
        HttpBodyMode.None => PreparedBody.None,
        HttpBodyMode.Json => new BytesBody(Encoding.UTF8.GetBytes(context.Resolve(body.Content)), JsonContentType),
        HttpBodyMode.Xml => new BytesBody(Encoding.UTF8.GetBytes(context.Resolve(body.Content)), XmlContentType),
        HttpBodyMode.Text => new BytesBody(Encoding.UTF8.GetBytes(context.Resolve(body.Content)), TextContentType),
        HttpBodyMode.FormUrlEncoded => new FormBody(context.ResolveEntries(body.FormFields)),
        HttpBodyMode.File => context.ResolveFile(body.FilePath, Messages.Get("Http_BodyFile")) is { } path
            ? new FileBody(path, BinaryContentType)
            : PreparedBody.None,
        HttpBodyMode.Multipart => new MultipartBody(
        [
            .. body.MultipartFields
                .Where(field => field.IsEnabled)
                .Select(field => field.Kind == MultipartFieldKind.File
                    ? new PreparedMultipartPart(context.Resolve(field.Name), context.ResolveFile(field.Value, Messages.Format("Http_PartFile", field.Name)) ?? string.Empty, true, field.ContentType)
                    : new PreparedMultipartPart(context.Resolve(field.Name), context.Resolve(field.Value), false, field.ContentType)),
        ]),
        _ => throw new NotSupportedException($"Unsupported body mode {body.Mode}."),
    };

    private sealed class Context(VariableResolver variables, string workspaceRoot)
    {
        public List<string> Errors { get; } = [];

        public List<VariableIssue> Issues { get; } = [];

        public string Resolve(string template)
        {
            var result = variables.Resolve(template);
            foreach (var issue in result.Issues.Where(issue => !Issues.Contains(issue)))
            {
                Issues.Add(issue);
            }

            return result.Value;
        }

        public List<KeyValuePair<string, string>> ResolveEntries(IEnumerable<KeyValueEntry> entries) =>
        [
            .. entries
                .Where(entry => entry.IsEnabled && entry.Key.Length > 0)
                .Select(entry => new KeyValuePair<string, string>(Resolve(entry.Key), Resolve(entry.Value))),
        ];

        public string? ResolveFile(string template, string what)
        {
            var path = Resolve(template).Trim();
            if (path.Length == 0)
            {
                Errors.Add(Messages.Format("Http_NoFileSelected", what));
                return null;
            }

            var fullPath = Path.GetFullPath(path, workspaceRoot);
            if (!File.Exists(fullPath))
            {
                Errors.Add(Messages.Format("Http_FileNotFound", what, path));
                return null;
            }

            return fullPath;
        }
    }
}
