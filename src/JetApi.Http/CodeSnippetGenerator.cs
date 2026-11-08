using System.Globalization;
using System.Text;
using JetApi.Core.Model;

namespace JetApi.Http;

/// <summary>A language or tool a request can be exported to.</summary>
public enum SnippetLanguage
{
    /// <summary>curl for bash, zsh and other POSIX shells.</summary>
    Curl,

    /// <summary>curl for the Windows command prompt (cmd.exe).</summary>
    CurlWindows,

    /// <summary>PowerShell's Invoke-RestMethod.</summary>
    PowerShell,

    /// <summary>C# with HttpClient, as top-level statements.</summary>
    CSharp,

    /// <summary>JavaScript with fetch (Node.js 20+ or a browser).</summary>
    JavaScript,

    /// <summary>Python with the requests library.</summary>
    Python,
}

/// <summary>
/// Turns a prepared request (variables resolved, auth applied) into runnable code that sends the same request.
/// Pure: no file or network access. Lines end with "\n"; there is no trailing newline.
/// </summary>
public static partial class CodeSnippetGenerator
{
    private const string FormContentType = "application/x-www-form-urlencoded";

    // Headers that HttpClient keeps on the content rather than on the request.
    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Allow", "Content-Disposition", "Content-Encoding", "Content-Language", "Content-Length", "Content-Location",
        "Content-MD5", "Content-Range", "Expires", "Last-Modified",
    };

    /// <summary>Generates code that sends <paramref name="request"/>.</summary>
    public static string Generate(PreparedHttpRequest request, SnippetLanguage language)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snippet = Snippet.From(request);
        return language switch
        {
            SnippetLanguage.Curl => Curl(snippet, ShellDialect.Bash),
            SnippetLanguage.CurlWindows => Curl(snippet, ShellDialect.Cmd),
            SnippetLanguage.PowerShell => PowerShell(snippet),
            SnippetLanguage.CSharp => CSharp(snippet),
            SnippetLanguage.JavaScript => JavaScript(snippet),
            SnippetLanguage.Python => Python(snippet),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Seconds(TimeSpan timeout) => timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool IsAscii(string text) => text.All(char.IsAscii);

    // Duplicate headers become one, comma-separated, for languages that keep headers in a dictionary.
    private static List<KeyValuePair<string, string>> MergeDuplicates(IEnumerable<KeyValuePair<string, string>> headers)
    {
        var merged = new List<KeyValuePair<string, string>>();
        foreach (var header in headers)
        {
            var index = merged.FindIndex(h => h.Key.Equals(header.Key, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                merged.Add(header);
            }
            else
            {
                var separator = header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ? "; " : ", ";
                merged[index] = new(merged[index].Key, merged[index].Value + separator + header.Value);
            }
        }

        return merged;
    }

    private static bool HasDuplicateKeys(IEnumerable<KeyValuePair<string, string>> pairs, StringComparer comparer)
    {
        var seen = new HashSet<string>(comparer);
        return pairs.Any(pair => !seen.Add(pair.Key));
    }

    /// <summary>The request in the shape every generator needs: the Content-Type separated from the other headers.</summary>
    /// <param name="Method">HTTP method.</param>
    /// <param name="Url">Absolute, escaped URL.</param>
    /// <param name="Headers">Headers in order, without Content-Type when there is a body.</param>
    /// <param name="ContentType">
    /// The Content-Type to send: an explicit header wins, else the body's own type. <c>null</c> for multipart bodies (the tool
    /// picks the boundary) and for forms without an explicit header (every tool sends the form type by itself).
    /// </param>
    /// <param name="Body">The body.</param>
    /// <param name="Settings">Transport options.</param>
    private sealed record Snippet(
        string Method,
        string Url,
        IReadOnlyList<KeyValuePair<string, string>> Headers,
        string? ContentType,
        PreparedBody Body,
        HttpRequestSettings Settings)
    {
        /// <summary>The body as text for <see cref="BytesBody"/>.</summary>
        public string? Text => Body is BytesBody bytes ? Encoding.UTF8.GetString(bytes.Content.Span) : null;

        public bool HasBody => Body is not NoBody;

        public static Snippet From(PreparedHttpRequest request)
        {
            if (request.Body is NoBody)
            {
                return new Snippet(request.Method, request.Url.AbsoluteUri, request.Headers, null, request.Body, request.Settings);
            }

            var explicitType = request.Headers.Where(IsContentType).Select(h => h.Value).Cast<string?>().FirstOrDefault();
            var headers = request.Headers.Where(h => !IsContentType(h)).ToList();
            var contentType = request.Body switch
            {
                BytesBody bytes => explicitType ?? bytes.ContentType,
                FileBody file => explicitType ?? file.ContentType,
                FormBody => explicitType,
                _ => null,
            };
            return new Snippet(request.Method, request.Url.AbsoluteUri, headers, contentType, request.Body, request.Settings);
        }

        private static bool IsContentType(KeyValuePair<string, string> header) => header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase);
    }

    private static string FileName(string path) => Path.GetFileName(path.Replace('\\', '/'));

    private static string JoinLines(IEnumerable<string> lines) => string.Join('\n', lines);
}
