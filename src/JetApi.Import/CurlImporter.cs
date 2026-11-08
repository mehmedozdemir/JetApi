using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JetApi.Core.Model;

namespace JetApi.Import;

/// <summary>
/// Converts a cURL command line (bash, Windows cmd or PowerShell quoting, as copied from browser developer tools,
/// Postman or documentation) into an HTTP request. Pure: nothing is run and no file is read. What cannot be carried
/// over is listed in the result's issues.
/// </summary>
public static partial class CurlImporter
{
    private const string FormContentType = "application/x-www-form-urlencoded";

    /// <summary>True when <paramref name="text"/> starts with a curl command (<c>curl</c>, <c>curl.exe</c>, or a path to one).</summary>
    public static bool LooksLikeCurl(string? text) => text is not null && CurlStart().IsMatch(text);

    /// <summary>Converts <paramref name="commandLine"/> into a request named after its method and path.</summary>
    public static ImportResult<HttpRequestDefinition> Import(string commandLine)
    {
        if (!LooksLikeCurl(commandLine))
        {
            return ImportResult.Failure<HttpRequestDefinition>(Messages.Get("Curl_NotCurl"));
        }

        var command = CurlTokenizer.Tokenize(commandLine.Trim().TrimStart('&').TrimStart());
        if (command.IsUnterminated)
        {
            return ImportResult.Failure<HttpRequestDefinition>(Messages.Get("Curl_UnterminatedQuote"));
        }

        var options = new Options();
        options.Parse(command.Words.Skip(1).ToList());
        if (options.Url is null)
        {
            return ImportResult.Failure<HttpRequestDefinition>(Messages.Get("Curl_NoUrl"));
        }

        if (command.ShellVariables.Count > 0)
        {
            options.Issue(Messages.Format("Curl_ShellVariables", string.Join(", ", command.ShellVariables)));
        }

        return new ImportResult<HttpRequestDefinition> { Value = options.Build(), Issues = options.Issues };
    }

    /// <summary>
    /// Splits the query string into parameters when that is lossless: JetApi encodes parameter values itself, so a value
    /// must decode to exactly what the server would read. A "+" (space or plus, depending on the server), a pair without
    /// "=" or an undecodable escape keeps the whole query in the URL instead.
    /// </summary>
    internal static (string Url, IReadOnlyList<KeyValueEntry> Query) SplitQuery(string raw)
    {
        var fragmentAt = raw.IndexOf('#', StringComparison.Ordinal);
        var withoutFragment = fragmentAt >= 0 ? raw[..fragmentAt] : raw;
        var fragment = fragmentAt >= 0 ? raw[fragmentAt..] : string.Empty;
        var queryAt = withoutFragment.IndexOf('?', StringComparison.Ordinal);
        if (queryAt < 0)
        {
            return (raw, []);
        }

        var query = new List<KeyValueEntry>();
        foreach (var pair in withoutFragment[(queryAt + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0 || pair.Contains('+', StringComparison.Ordinal)
                || !TryDecode(pair[..equals], plusIsSpace: false, out var key) || !TryDecode(pair[(equals + 1)..], plusIsSpace: false, out var value))
            {
                return (raw, []);
            }

            query.Add(new KeyValueEntry(key, value));
        }

        return (withoutFragment[..queryAt] + fragment, query);
    }

    /// <summary>Decodes percent escapes; false when an escape does not form valid UTF-8 (it could not be sent back the same way).</summary>
    internal static bool TryDecode(string value, bool plusIsSpace, out string decoded)
    {
        var result = new StringBuilder(value.Length);
        var bytes = new List<byte>();
        decoded = value;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length && char.IsAsciiHexDigit(value[i + 1]) && char.IsAsciiHexDigit(value[i + 2]))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
                continue;
            }

            if (!Flush(bytes, result))
            {
                return false;
            }

            result.Append(plusIsSpace && value[i] == '+' ? ' ' : value[i]);
        }

        if (!Flush(bytes, result))
        {
            return false;
        }

        decoded = result.ToString();
        return true;
    }

    private static bool Flush(List<byte> bytes, StringBuilder result)
    {
        if (bytes.Count == 0)
        {
            return true;
        }

        try
        {
            result.Append(StrictUtf8.GetString([.. bytes]));
            bytes.Clear();
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // "& curl.exe" is PowerShell's call operator.
    [GeneratedRegex(@"^\s*(?:&\s*)?(?:\S*[\\/])?curl(?:\.exe)?(?:\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurlStart();

    private enum DataKind
    {
        /// <summary>-d, --data, --data-ascii, --data-binary: "@file" reads a file.</summary>
        Data,

        /// <summary>--data-raw: taken literally.</summary>
        Raw,

        /// <summary>--data-urlencode: the content is encoded by curl.</summary>
        UrlEncode,

        /// <summary>--json: JSON with JSON headers.</summary>
        Json,
    }

    private sealed record DataPart(DataKind Kind, string Value, bool IsBinary);

    /// <summary>The options of one curl command and the request they describe.</summary>
    private sealed class Options
    {
        // Options whose value is ignored because it only affects curl's output or connection tuning.
        private static readonly HashSet<string> IgnoredWithValue = new(StringComparer.Ordinal)
        {
            "output", "dump-header", "write-out", "cookie-jar", "connect-timeout", "retry", "retry-delay", "retry-max-time",
            "max-redirs", "stderr", "trace", "trace-ascii", "trace-config", "limit-rate", "keepalive-time", "expect100-timeout",
            "output-dir", "create-file-mode", "etag-save", "etag-compare", "parallel-max", "speed-limit", "speed-time", "rate",
            "happy-eyeballs-timeout-ms", "max-filesize",
        };

        // Options without a value that only affect curl's output, protocol negotiation or connection handling.
        private static readonly HashSet<string> IgnoredFlags = new(StringComparer.Ordinal)
        {
            "compressed", "compressed-ssh", "silent", "show-error", "verbose", "include", "no-progress-meter", "progress-bar",
            "fail", "fail-with-body", "fail-early", "globoff", "no-buffer", "http1.0", "http1.1", "http2", "http2-prior-knowledge",
            "http3", "http3-only", "http0.9", "ipv4", "ipv6", "tr-encoding", "path-as-is", "remote-name", "remote-header-name",
            "remote-name-all", "create-dirs", "disable", "styled-output", "no-keepalive", "tcp-nodelay", "tcp-fastopen", "raw",
            "ignore-content-length", "retry-all-errors", "retry-connrefused", "parallel", "parallel-immediate", "no-alpn", "no-npn",
            "no-sessionid", "tlsv1", "tlsv1.0", "tlsv1.1", "tlsv1.2", "tlsv1.3", "sslv2", "sslv3", "ssl", "ssl-reqd",
            "ssl-no-revoke", "ssl-revoke-best-effort", "ssl-allow-beast", "xattr", "remote-time", "junk-session-cookies",
            "suppress-connect-headers", "basic", "no-clobber", "remove-on-error", "skip-existing", "show-headers", "clobber",
        };

        // Options with a value that change what is sent but have no JetApi equivalent yet.
        private static readonly HashSet<string> UnsupportedWithValue = new(StringComparer.Ordinal)
        {
            "proxy", "proxy-user", "cert", "cert-type", "key", "key-type", "pass", "cacert", "capath", "crlfile", "resolve",
            "connect-to", "interface", "unix-socket", "abstract-unix-socket", "config", "aws-sigv4", "request-target",
            "dns-servers", "doh-url", "noproxy", "proxy-header", "pinnedpubkey", "local-port", "continue-at", "variable",
            "time-cond", "ciphers", "tls13-ciphers", "curves", "netrc-file", "proto", "proto-redir", "telnet-option", "quote",
            "ftp-port", "mail-from", "mail-rcpt", "mail-auth", "hostpubmd5", "hostpubsha256", "pubkey", "login-options",
            "sasl-authzid", "service-name", "krb", "tls-max", "socks4", "socks4a", "socks5", "socks5-hostname", "preproxy",
            "proxy-cacert", "proxy-cert", "proxy-key", "delegation", "user-agent-file", "ip-tos", "vlan-priority", "ech",
        };

        // Options without a value that change what is sent but have no JetApi equivalent yet.
        private static readonly HashSet<string> UnsupportedFlags = new(StringComparer.Ordinal)
        {
            "netrc", "netrc-optional", "proxytunnel", "proxy-insecure", "cert-status", "false-start", "append", "list-only",
            "use-ascii", "crlf", "proxy-anyauth", "proxy-basic", "proxy-digest", "proxy-ntlm", "proxy-negotiate",
        };

        private static readonly HashSet<string> AuthSchemes = new(StringComparer.Ordinal) { "digest", "ntlm", "ntlm-wb", "negotiate", "anyauth" };

        // Short options; those that take a value are listed in ShortWithValue.
        private static readonly Dictionary<char, string> ShortOptions = new()
        {
            ['X'] = "request",
            ['H'] = "header",
            ['d'] = "data",
            ['G'] = "get",
            ['F'] = "form",
            ['u'] = "user",
            ['A'] = "user-agent",
            ['e'] = "referer",
            ['b'] = "cookie",
            ['L'] = "location",
            ['k'] = "insecure",
            ['m'] = "max-time",
            ['I'] = "head",
            ['T'] = "upload-file",
            ['r'] = "range",
            ['o'] = "output",
            ['D'] = "dump-header",
            ['w'] = "write-out",
            ['c'] = "cookie-jar",
            ['s'] = "silent",
            ['S'] = "show-error",
            ['v'] = "verbose",
            ['i'] = "include",
            ['#'] = "progress-bar",
            ['f'] = "fail",
            ['g'] = "globoff",
            ['N'] = "no-buffer",
            ['0'] = "http1.0",
            ['4'] = "ipv4",
            ['6'] = "ipv6",
            ['O'] = "remote-name",
            ['J'] = "remote-header-name",
            ['q'] = "disable",
            ['Z'] = "parallel",
            ['R'] = "remote-time",
            ['j'] = "junk-session-cookies",
            ['x'] = "proxy",
            ['U'] = "proxy-user",
            ['E'] = "cert",
            ['K'] = "config",
            ['C'] = "continue-at",
            ['z'] = "time-cond",
            ['t'] = "telnet-option",
            ['Q'] = "quote",
            ['P'] = "ftp-port",
            ['y'] = "speed-time",
            ['Y'] = "speed-limit",
            ['n'] = "netrc",
            ['p'] = "proxytunnel",
            ['a'] = "append",
            ['l'] = "list-only",
            ['B'] = "use-ascii",
            ['1'] = "tlsv1",
            ['2'] = "sslv2",
            ['3'] = "sslv3",
            ['M'] = "manual",
            ['h'] = "help",
            ['V'] = "version",
        };

        private static readonly HashSet<string> SupportedWithValue = new(StringComparer.Ordinal)
        {
            "request", "header", "data", "data-ascii", "data-binary", "data-raw", "data-urlencode", "json", "form", "form-string",
            "user", "oauth2-bearer", "user-agent", "referer", "cookie", "url", "max-time", "upload-file", "range", "url-query",
        };

        private readonly List<ImportIssue> _issues = [];
        private readonly List<KeyValueEntry> _headers = [];
        private readonly List<DataPart> _data = [];
        private readonly List<string> _urlQuery = [];
        private readonly List<MultipartField> _form = [];
        private readonly List<string> _cookies = [];
        private string? _method;
        private string? _user;
        private string? _bearer;
        private string? _authScheme;
        private string? _uploadFile;
        private bool _get;
        private bool _head;
        private bool _follow;
        private bool _insecure;
        private TimeSpan? _timeout;

        public string? Url { get; private set; }

        public IReadOnlyList<ImportIssue> Issues => _issues;

        public void Issue(string message, ImportIssueSeverity severity = ImportIssueSeverity.Warning) =>
            _issues.Add(new ImportIssue(string.Empty, message, severity));

        public void Parse(List<string> words)
        {
            var endOfOptions = false;
            for (var i = 0; i < words.Count; i++)
            {
                var word = words[i];
                if (endOfOptions || word.Length < 2 || word[0] != '-')
                {
                    Positional(word);
                }
                else if (word == "--")
                {
                    endOfOptions = true;
                }
                else if (word.StartsWith("--", StringComparison.Ordinal))
                {
                    var name = word[2..];
                    if (TakesValue(name))
                    {
                        if (i + 1 < words.Count)
                        {
                            Apply(name, word, words[++i]);
                        }
                        else
                        {
                            Issue(Messages.Format("Curl_MissingValue", word));
                        }
                    }
                    else
                    {
                        Apply(name, word, null);
                    }
                }
                else
                {
                    // Combined short options: "-sSL", "-XPOST", "-HAccept: */*".
                    for (var j = 1; j < word.Length; j++)
                    {
                        var flag = "-" + word[j];
                        if (!ShortOptions.TryGetValue(word[j], out var name))
                        {
                            Issue(Messages.Format("Curl_UnknownOption", flag));
                            continue;
                        }

                        if (!TakesValue(name))
                        {
                            Apply(name, flag, null);
                            continue;
                        }

                        if (j + 1 < word.Length)
                        {
                            Apply(name, flag, word[(j + 1)..]);
                        }
                        else if (i + 1 < words.Count)
                        {
                            Apply(name, flag, words[++i]);
                        }
                        else
                        {
                            Issue(Messages.Format("Curl_MissingValue", flag));
                        }

                        break;
                    }
                }
            }
        }

        private static bool TakesValue(string name) =>
            SupportedWithValue.Contains(name) || IgnoredWithValue.Contains(name) || UnsupportedWithValue.Contains(name);

        private void Positional(string word)
        {
            if (Url is null)
            {
                Url = word;
            }
            else
            {
                Issue(Messages.Format("Curl_ExtraUrl", word));
            }
        }

        private void Apply(string name, string written, string? value)
        {
            switch (name)
            {
                case "request":
                    _method = value!.Trim().ToUpperInvariant();
                    break;
                case "header":
                    AddHeader(value!);
                    break;
                case "data" or "data-ascii":
                    _data.Add(new DataPart(DataKind.Data, value!, IsBinary: false));
                    break;
                case "data-binary":
                    _data.Add(new DataPart(DataKind.Data, value!, IsBinary: true));
                    break;
                case "data-raw":
                    _data.Add(new DataPart(DataKind.Raw, value!, IsBinary: false));
                    break;
                case "data-urlencode":
                    _data.Add(new DataPart(DataKind.UrlEncode, value!, IsBinary: false));
                    break;
                case "json":
                    _data.Add(new DataPart(DataKind.Json, value!, IsBinary: false));
                    break;
                case "url-query":
                    if (UrlEncoded(value!) is { } encoded)
                    {
                        _urlQuery.Add(encoded);
                    }

                    break;
                case "form":
                    AddFormField(value!, literal: false);
                    break;
                case "form-string":
                    AddFormField(value!, literal: true);
                    break;
                case "user":
                    _user = value;
                    break;
                case "oauth2-bearer":
                    _bearer = value;
                    break;
                case "user-agent":
                    if (value!.Length > 0)
                    {
                        _headers.Add(new KeyValueEntry("User-Agent", value));
                    }

                    break;
                case "referer":
                    var referer = value!.EndsWith(";auto", StringComparison.Ordinal) ? value[..^5] : value;
                    if (referer.Length > 0)
                    {
                        _headers.Add(new KeyValueEntry("Referer", referer));
                    }

                    break;
                case "cookie":
                    if (value!.Contains('=', StringComparison.Ordinal))
                    {
                        _cookies.Add(value.Trim().TrimEnd(';'));
                    }
                    else
                    {
                        Issue(Messages.Format("Curl_FileNotRead", value));
                    }

                    break;
                case "url":
                    Positional(value!);
                    break;
                case "max-time":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds)
                    {
                        _timeout = TimeSpan.FromSeconds(seconds);
                    }
                    else
                    {
                        Issue(Messages.Format("Curl_InvalidTimeout", value));
                    }

                    break;
                case "upload-file":
                    _uploadFile = value;
                    break;
                case "range":
                    _headers.Add(new KeyValueEntry("Range", "bytes=" + value));
                    break;
                case "get":
                    _get = true;
                    break;
                case "head":
                    _head = true;
                    break;
                case "location" or "location-trusted":
                    _follow = true;
                    break;
                case "no-location":
                    _follow = false;
                    break;
                case "insecure":
                    _insecure = true;
                    break;
                case "no-insecure":
                    _insecure = false;
                    break;
                case "help" or "version" or "manual":
                    break;
                case var _ when AuthSchemes.Contains(name):
                    _authScheme = written;
                    break;
                case var _ when IgnoredWithValue.Contains(name) || IgnoredFlags.Contains(name):
                    break;
                case var _ when name.StartsWith("no-", StringComparison.Ordinal) && IgnoredFlags.Contains(name[3..]):
                    break;
                case var _ when UnsupportedWithValue.Contains(name) || UnsupportedFlags.Contains(name):
                    Issue(Messages.Format("Curl_IgnoredOption", written));
                    break;
                default:
                    Issue(Messages.Format("Curl_UnknownOption", written));
                    break;
            }
        }

        private void AddHeader(string header)
        {
            if (header.StartsWith('@'))
            {
                Issue(Messages.Format("Curl_FileNotRead", header[1..]));
                return;
            }

            var colon = header.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                var value = header[(colon + 1)..].Trim();

                // "Name:" with nothing after it removes one of curl's own headers; there is nothing to send.
                if (value.Length > 0)
                {
                    _headers.Add(new KeyValueEntry(header[..colon].Trim(), value));
                }

                return;
            }

            // "Name;" sends the header with an empty value.
            if (header.EndsWith(';') && header.Length > 1)
            {
                _headers.Add(new KeyValueEntry(header[..^1].Trim(), string.Empty));
            }
        }

        // -F name=value, name=@file, name=<file, with ;type= and ;filename= attributes; --form-string takes the value literally.
        private void AddFormField(string spec, bool literal)
        {
            var equals = spec.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                Issue(Messages.Format("Curl_InvalidForm", spec));
                return;
            }

            var name = spec[..equals];
            var rest = spec[(equals + 1)..];
            if (literal)
            {
                _form.Add(new MultipartField(name, rest));
                return;
            }

            var kind = MultipartFieldKind.Text;
            var fromFile = false;
            if (rest.StartsWith('@') || rest.StartsWith('<'))
            {
                fromFile = rest[0] == '<';
                kind = MultipartFieldKind.File;
                rest = rest[1..];
            }

            var (value, attributes) = SplitFormValue(rest);
            string? contentType = null;
            foreach (var attribute in attributes)
            {
                var separator = attribute.IndexOf('=', StringComparison.Ordinal);
                if (separator > 0 && attribute[..separator].Trim().Equals("type", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = attribute[(separator + 1)..].Trim();
                }
            }

            if (kind == MultipartFieldKind.File)
            {
                Issue(Messages.Format("Import_FilePath", value), ImportIssueSeverity.Info);
                if (fromFile)
                {
                    Issue(Messages.Format("Curl_FormFieldFromFile", name), ImportIssueSeverity.Info);
                }
            }

            _form.Add(new MultipartField(name, value, kind, ContentType: contentType));
        }

        // The value may be in double quotes (with \" and \\ escapes); unquoted, it ends at the first ";".
        private static (string Value, List<string> Attributes) SplitFormValue(string text)
        {
            var value = new StringBuilder();
            var i = 0;
            if (text.StartsWith('"'))
            {
                for (i = 1; i < text.Length && text[i] != '"'; i++)
                {
                    if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
                    {
                        i++;
                    }

                    value.Append(text[i]);
                }

                i = Math.Min(i + 1, text.Length);
            }
            else
            {
                for (; i < text.Length && text[i] != ';'; i++)
                {
                    value.Append(text[i]);
                }
            }

            var attributes = i < text.Length
                ? text[i..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : [];
            return (value.ToString(), attributes);
        }

        // --data-urlencode / --url-query: "content", "=content", "name=content", "@file", "name@file".
        private string? UrlEncoded(string spec)
        {
            var equals = spec.IndexOf('=', StringComparison.Ordinal);
            var at = spec.IndexOf('@', StringComparison.Ordinal);
            if (at >= 0 && (equals < 0 || at < equals))
            {
                Issue(Messages.Format("Curl_FileNotRead", spec[(at + 1)..]));
                return null;
            }

            return equals switch
            {
                < 0 => Uri.EscapeDataString(spec),
                0 => Uri.EscapeDataString(spec[1..]),
                _ => spec[..equals] + "=" + Uri.EscapeDataString(spec[(equals + 1)..]),
            };
        }

        public HttpRequestDefinition Build()
        {
            var headers = new List<KeyValueEntry>(_headers);
            if (_cookies.Count > 0)
            {
                headers.Add(new KeyValueEntry("Cookie", string.Join("; ", _cookies)));
            }

            var isJson = _data.Any(d => d.Kind == DataKind.Json);
            if (isJson && !headers.Any(h => IsHeader(h, "Accept")))
            {
                headers.Add(new KeyValueEntry("Accept", "application/json"));
            }

            var url = Url!;
            var rawData = JoinData();
            if (_get && rawData is { Length: > 0 })
            {
                url = AppendQuery(url, rawData);
                rawData = null;
            }

            foreach (var query in _urlQuery)
            {
                url = AppendQuery(url, query);
            }

            var (baseUrl, queryParameters) = SplitQuery(url);
            var body = BuildBody(rawData, headers, isJson);
            var method = _method
                ?? (_head ? "HEAD"
                : _get ? "GET"
                : _uploadFile is not null ? "PUT"
                : body.Mode != HttpBodyMode.None ? "POST"
                : "GET");
            var auth = BuildAuth(headers);
            var name = method + " " + PathOf(baseUrl);

            return new HttpRequestDefinition
            {
                Key = NodeKey.FromName(name),
                Name = name,
                Method = method,
                Url = baseUrl,
                QueryParameters = queryParameters,
                Headers = headers,
                Body = body,
                Auth = auth,
                Settings = new HttpRequestSettings(FollowRedirects: _follow, ValidateCertificates: !_insecure, Timeout: _timeout),
            };
        }

        // Joins all data options as curl does ("&" between -d values); null when there is no inline data.
        private string? JoinData()
        {
            var pieces = new List<string>();
            var jsonPieces = new StringBuilder();
            foreach (var part in _data)
            {
                switch (part.Kind)
                {
                    case DataKind.UrlEncode:
                        if (UrlEncoded(part.Value) is { } encoded)
                        {
                            pieces.Add(encoded);
                        }

                        break;
                    case DataKind.Data or DataKind.Json when part.Value.StartsWith('@'):
                        // A single file is the body (see BuildBody); mixed with other data it cannot be imported.
                        if (_data.Count > 1 || _get)
                        {
                            Issue(Messages.Format("Curl_FileNotRead", part.Value[1..]));
                        }

                        break;
                    case DataKind.Json:
                        // --json values are concatenated without a separator.
                        jsonPieces.Append(part.Value);
                        break;
                    default:
                        pieces.Add(part.Value);
                        break;
                }
            }

            if (jsonPieces.Length > 0)
            {
                pieces.Add(jsonPieces.ToString());
            }

            return pieces.Count == 0 ? null : string.Join('&', pieces);
        }

        private static string AppendQuery(string url, string query)
        {
            var fragmentAt = url.IndexOf('#', StringComparison.Ordinal);
            var fragment = fragmentAt >= 0 ? url[fragmentAt..] : string.Empty;
            var withoutFragment = fragmentAt >= 0 ? url[..fragmentAt] : url;
            var separator = !withoutFragment.Contains('?', StringComparison.Ordinal) ? "?"
                : withoutFragment.EndsWith('?') || withoutFragment.EndsWith('&') ? string.Empty
                : "&";
            return withoutFragment + separator + query + fragment;
        }

        private HttpBody BuildBody(string? rawData, List<KeyValueEntry> headers, bool isJson)
        {
            var contentTypeHeader = headers.FirstOrDefault(h => IsHeader(h, "Content-Type"));
            var contentType = contentTypeHeader?.Value ?? (isJson ? "application/json" : null);

            if (_form.Count > 0)
            {
                // The boundary is chosen when sending; a fixed multipart Content-Type would not match it.
                if (contentTypeHeader?.Value.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) == true)
                {
                    headers.Remove(contentTypeHeader);
                }

                if (rawData is not null)
                {
                    Issue(Messages.Get("Curl_DataAndForm"));
                }

                return new HttpBody { Mode = HttpBodyMode.Multipart, MultipartFields = _form };
            }

            var fileSource = rawData is null && !_get
                ? _data.FirstOrDefault(d => d.Kind is DataKind.Data or DataKind.Json && d.Value.StartsWith('@'))?.Value[1..] ?? _uploadFile
                : _uploadFile;
            if (rawData is null && fileSource is not null)
            {
                if (fileSource == "-" || fileSource == ".")
                {
                    Issue(Messages.Format("Curl_FileNotRead", fileSource));
                    return HttpBody.Empty;
                }

                Issue(Messages.Format("Import_FilePath", fileSource), ImportIssueSeverity.Info);
                DropImpliedContentType(headers, contentTypeHeader, "application/octet-stream");
                return new HttpBody { Mode = HttpBodyMode.File, FilePath = fileSource };
            }

            if (rawData is null)
            {
                return HttpBody.Empty;
            }

            if (contentType is null)
            {
                // curl sends -d data as a form. JSON without a Content-Type is almost always meant as JSON.
                if (LooksLikeJson(rawData))
                {
                    Issue(Messages.Get("Curl_JsonWithoutContentType"), ImportIssueSeverity.Info);
                    return HttpBody.Json(rawData);
                }

                if (TryParseForm(rawData) is { } fields)
                {
                    return HttpBody.Form(fields);
                }

                headers.Add(new KeyValueEntry("Content-Type", FormContentType));
                return HttpBody.Text(rawData);
            }

            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                DropImpliedContentType(headers, contentTypeHeader, "application/json");
                return HttpBody.Json(rawData);
            }

            if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                DropImpliedContentType(headers, contentTypeHeader, "application/xml");
                return HttpBody.Xml(rawData);
            }

            if (contentType.StartsWith(FormContentType, StringComparison.OrdinalIgnoreCase) && TryParseForm(rawData) is { } formFields)
            {
                DropImpliedContentType(headers, contentTypeHeader, FormContentType);
                return HttpBody.Form(formFields);
            }

            DropImpliedContentType(headers, contentTypeHeader, "text/plain; charset=utf-8");
            return HttpBody.Text(rawData);
        }

        // JetApi sets this Content-Type for the body mode by itself; any other value stays as a header and wins.
        private static void DropImpliedContentType(List<KeyValueEntry> headers, KeyValueEntry? header, string implied)
        {
            if (header is not null && header.Value.Trim().Equals(implied, StringComparison.OrdinalIgnoreCase))
            {
                headers.Remove(header);
            }
        }

        private static bool LooksLikeJson(string text)
        {
            var trimmed = text.TrimStart();
            if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        // Form fields hold decoded values (JetApi encodes them when sending); null when that would not be lossless.
        private static List<KeyValueEntry>? TryParseForm(string data)
        {
            var fields = new List<KeyValueEntry>();
            foreach (var pair in data.Split('&'))
            {
                var equals = pair.IndexOf('=', StringComparison.Ordinal);
                if (equals <= 0 || !TryDecode(pair[..equals], plusIsSpace: true, out var key) || !TryDecode(pair[(equals + 1)..], plusIsSpace: true, out var value))
                {
                    return null;
                }

                fields.Add(new KeyValueEntry(key, value));
            }

            return fields;
        }

        private AuthConfig BuildAuth(List<KeyValueEntry> headers)
        {
            if (_authScheme is not null)
            {
                Issue(Messages.Format("Curl_AuthNotSupported", _authScheme));
                return AuthConfig.None;
            }

            if (_bearer is not null)
            {
                return new BearerAuth(_bearer);
            }

            if (_user is not null)
            {
                var colon = _user.IndexOf(':', StringComparison.Ordinal);
                if (colon < 0)
                {
                    Issue(Messages.Format("Curl_PasswordMissing", _user));
                    return new BasicAuth(_user, string.Empty);
                }

                return new BasicAuth(_user[..colon], _user[(colon + 1)..]);
            }

            // An Authorization header becomes auth settings, so the token can move to a variable on the Auth tab.
            var authorization = headers.Where(h => IsHeader(h, "Authorization")).ToList();
            if (authorization.Count != 1)
            {
                return AuthConfig.Inherit;
            }

            var header = authorization[0];
            var space = header.Value.IndexOf(' ', StringComparison.Ordinal);
            var scheme = space > 0 ? header.Value[..space] : string.Empty;
            var credentials = space > 0 ? header.Value[(space + 1)..].Trim() : string.Empty;
            if (scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) && credentials.Length > 0)
            {
                headers.Remove(header);
                return new BearerAuth(credentials);
            }

            if (scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase) && DecodeBasic(credentials) is { } basic)
            {
                headers.Remove(header);
                return basic;
            }

            return AuthConfig.Inherit;
        }

        private static BasicAuth? DecodeBasic(string credentials)
        {
            var buffer = new byte[credentials.Length];
            if (!Convert.TryFromBase64String(credentials, buffer, out var length))
            {
                return null;
            }

            try
            {
                var text = StrictUtf8.GetString(buffer, 0, length);
                var colon = text.IndexOf(':', StringComparison.Ordinal);

                // Re-encoding must give the same header, otherwise the request would change.
                return colon < 0 || Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) != credentials
                    ? null
                    : new BasicAuth(text[..colon], text[(colon + 1)..]);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        private static bool IsHeader(KeyValueEntry header, string name) => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase);

        // "https://api.example.com/users/1" and "{{baseUrl}}/users/1" both give "/users/1".
        private static string PathOf(string url)
        {
            var rest = url;
            var fragmentAt = rest.IndexOf('#', StringComparison.Ordinal);
            rest = fragmentAt >= 0 ? rest[..fragmentAt] : rest;
            var queryAt = rest.IndexOf('?', StringComparison.Ordinal);
            rest = queryAt >= 0 ? rest[..queryAt] : rest;
            var schemeAt = rest.IndexOf("://", StringComparison.Ordinal);
            rest = schemeAt >= 0 ? rest[(schemeAt + 3)..] : rest;
            if (rest.StartsWith("{{", StringComparison.Ordinal) && rest.IndexOf("}}", StringComparison.Ordinal) is var close and > 0)
            {
                rest = rest[(close + 2)..];
            }

            var slash = rest.IndexOf('/', StringComparison.Ordinal);
            return slash >= 0 ? rest[slash..] : "/";
        }
    }
}
