using System.Text;

namespace JetApi.Http;

public static partial class CodeSnippetGenerator
{
    private enum ShellDialect
    {
        Bash,
        Cmd,
    }

    // curl with long option names, one option per line.
    private static string Curl(Snippet snippet, ShellDialect dialect)
    {
        string Quote(string value) => dialect == ShellDialect.Bash ? BashQuote(value) : CmdQuote(value);

        var first = new StringBuilder("curl");
        if (snippet.Method == "HEAD")
        {
            // "--request HEAD" makes curl wait for a body that never comes.
            first.Append(" --head");
        }
        else if (!(snippet.Method == "GET" && !snippet.HasBody) && !(snippet.Method == "POST" && snippet.HasBody))
        {
            first.Append(" --request ").Append(Quote(snippet.Method));
        }

        first.Append(' ').Append(Quote(snippet.Url));
        var options = new List<string>();
        if (snippet.Settings.FollowRedirects)
        {
            options.Add("--location");
        }

        if (!snippet.Settings.ValidateCertificates)
        {
            options.Add("--insecure");
        }

        if (snippet.Settings.Timeout is { } timeout)
        {
            options.Add("--max-time " + Seconds(timeout));
        }

        foreach (var (name, value) in snippet.Headers)
        {
            // "Name;" is curl's way to send a header with an empty value.
            options.Add("--header " + Quote(value.Length == 0 ? name + ";" : name + ": " + value));
        }

        if (snippet.ContentType is { } contentType)
        {
            options.Add("--header " + Quote("Content-Type: " + contentType));
        }

        switch (snippet.Body)
        {
            case BytesBody:
                options.Add("--data-raw " + Quote(snippet.Text!));
                break;
            case FormBody form:
                foreach (var (name, value) in form.Fields)
                {
                    // --data-urlencode encodes only the value, so a name with special characters is encoded here.
                    options.Add(name.All(IsUnreserved)
                        ? "--data-urlencode " + Quote(name + "=" + value)
                        : "--data-raw " + Quote(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value)));
                }

                break;
            case FileBody file:
                options.Add("--data-binary " + Quote("@" + file.FilePath));
                break;
            case MultipartBody multipart:
                foreach (var part in multipart.Parts)
                {
                    options.Add(part switch
                    {
                        { IsFile: true } => "--form " + Quote(part.Name + "=@" + CurlFormQuote(part.Value) + (part.ContentType is null ? string.Empty : ";type=" + part.ContentType)),
                        { ContentType: null } => "--form-string " + Quote(part.Name + "=" + part.Value),
                        _ => "--form " + Quote(part.Name + "=" + CurlFormQuote(part.Value) + ";type=" + part.ContentType),
                    });
                }

                break;
        }

        if (options.Count == 0)
        {
            return first.ToString();
        }

        var continuation = dialect == ShellDialect.Bash ? " \\\n  " : " ^\n  ";
        return first + continuation + string.Join(continuation, options);
    }

    private static bool IsUnreserved(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~';

    // A -F value in double quotes, so ";" and "," in it are not read as attributes.
    private static string CurlFormQuote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    // Single quotes keep everything literal; a single quote itself is closed, escaped and reopened.
    private static string BashQuote(string value) =>
        value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/' or ':' or '@' or '=' or ',' or '+')
            ? value
            : "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>
    /// Quotes for cmd.exe as Chrome's "Copy as cURL (cmd)" does. curl.exe splits its command line with the C runtime
    /// rules (<c>\"</c> is a quote, backslashes double only before a quote). Every quote and special character is then
    /// escaped with <c>^</c>, so cmd never enters its own quote mode, and a newline becomes <c>^</c> + two line breaks.
    /// </summary>
    private static string CmdQuote(string value)
    {
        var runtime = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            runtime.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
            backslashes = 0;
            runtime.Append(c);
        }

        runtime.Append('\\', backslashes * 2).Append('"');

        var text = runtime.ToString();
        var result = new StringBuilder(text.Length * 2);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                continue;
            }

            if (c == '\n')
            {
                result.Append("^\n\n");
            }
            else if (c == '%')
            {
                // "%^NAME%" is not a variable reference; the caret then disappears.
                result.Append('%');
                if (i + 1 < text.Length && (char.IsAsciiLetterOrDigit(text[i + 1]) || text[i + 1] == '_'))
                {
                    result.Append('^');
                }
            }
            else if (!char.IsAscii(c) || char.IsAsciiLetterOrDigit(c) || c is ' ' or '\t' or '_' or '-' or ':' or '=' or '+' or '~' or '\'' or '/' or '.' or ',' or '?' or ';' or '(' or ')' or '*' or '`')
            {
                result.Append(c);
            }
            else
            {
                result.Append('^').Append(c);
            }
        }

        return result.ToString();
    }

    private static readonly Dictionary<string, string> PowerShellMethods = new(StringComparer.Ordinal)
    {
        ["POST"] = "Post",
        ["PUT"] = "Put",
        ["DELETE"] = "Delete",
        ["PATCH"] = "Patch",
        ["HEAD"] = "Head",
        ["OPTIONS"] = "Options",
        ["TRACE"] = "Trace",
        ["MERGE"] = "Merge",
    };

    // Invoke-RestMethod with splatted parameters.
    private static string PowerShell(Snippet snippet)
    {
        var lines = new List<string>();
        var parameters = new List<string> { "Uri = " + PowerShellQuote(snippet.Url) };
        var needsPowerShell7 = !snippet.Settings.ValidateCertificates || snippet.Body is MultipartBody || !PowerShellMethods.ContainsKey(snippet.Method) && snippet.Method != "GET";

        if (snippet.Method != "GET")
        {
            parameters.Add(PowerShellMethods.TryGetValue(snippet.Method, out var method)
                ? "Method = '" + method + "'"
                : "CustomMethod = " + PowerShellQuote(snippet.Method));
        }

        var headers = MergeDuplicates(snippet.Headers);
        if (headers.Count > 0)
        {
            lines.Add("$headers = [ordered]@{");
            lines.AddRange(headers.Select(h => "    " + PowerShellQuote(h.Key) + " = " + PowerShellQuote(h.Value)));
            lines.Add("}");
            parameters.Add("Headers = $headers");
        }

        if (snippet.ContentType is { } contentType)
        {
            parameters.Add("ContentType = " + PowerShellQuote(contentType));
        }

        switch (snippet.Body)
        {
            case BytesBody:
                var text = snippet.Text!;
                lines.Add("$body = " + PowerShellQuote(text));

                // Windows PowerShell sends strings as ISO-8859-1; bytes keep the UTF-8 encoding.
                parameters.Add(IsAscii(text) ? "Body = $body" : "Body = [System.Text.Encoding]::UTF8.GetBytes($body)");
                break;
            case FormBody form when HasDuplicateKeys(form.Fields, StringComparer.OrdinalIgnoreCase):
                parameters.Add("Body = " + PowerShellQuote(string.Join('&', form.Fields.Select(f => Uri.EscapeDataString(f.Key) + "=" + Uri.EscapeDataString(f.Value)))));
                if (snippet.ContentType is null)
                {
                    parameters.Add("ContentType = " + PowerShellQuote(FormContentType));
                }

                break;
            case FormBody form:
                // A dictionary body is sent URL-encoded.
                lines.Add("$form = [ordered]@{");
                lines.AddRange(form.Fields.Select(f => "    " + PowerShellQuote(f.Key) + " = " + PowerShellQuote(f.Value)));
                lines.Add("}");
                parameters.Add("Body = $form");
                break;
            case FileBody file:
                parameters.Add("InFile = " + PowerShellQuote(file.FilePath));
                break;
            case MultipartBody multipart:
                lines.Add("$form = [ordered]@{");
                foreach (var group in multipart.Parts.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var values = group.Select(p => p.IsFile ? "(Get-Item -LiteralPath " + PowerShellQuote(p.Value) + ")" : PowerShellQuote(p.Value)).ToList();
                    lines.Add("    " + PowerShellQuote(group.Key) + " = " + (values.Count == 1 ? values[0] : "@(" + string.Join(", ", values) + ")"));
                }

                lines.Add("}");
                parameters.Add("Form = $form");
                break;
        }

        if (!snippet.Settings.FollowRedirects)
        {
            parameters.Add("MaximumRedirection = 0");
        }

        if (!snippet.Settings.ValidateCertificates)
        {
            parameters.Add("SkipCertificateCheck = $true");
        }

        if (snippet.Settings.Timeout is { } timeout)
        {
            parameters.Add(Invariant($"TimeoutSec = {Math.Max(1, (long)Math.Ceiling(timeout.TotalSeconds))}"));
        }

        var output = new List<string>();
        if (needsPowerShell7)
        {
            output.Add("# Requires PowerShell 7 or later.");
        }

        output.AddRange(lines);
        output.Add("$params = @{");
        output.AddRange(parameters.Select(p => "    " + p));
        output.Add("}");
        output.Add(string.Empty);
        output.Add("Invoke-RestMethod @params");
        return JoinLines(output);
    }

    // PowerShell also reads typographic single quotes as quotes; doubling any of them keeps it literal.
    private static string PowerShellQuote(string value)
    {
        var result = new StringBuilder("'");
        foreach (var c in value)
        {
            if (c is '\'' or '‘' or '’' or '‚' or '‛')
            {
                result.Append(c);
            }

            result.Append(c);
        }

        return result.Append('\'').ToString();
    }
}
