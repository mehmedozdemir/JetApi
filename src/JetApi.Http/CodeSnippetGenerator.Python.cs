using System.Text;

namespace JetApi.Http;

public static partial class CodeSnippetGenerator
{
    // The requests library.
    private static string Python(Snippet snippet)
    {
        var lines = new List<string> { "import requests", string.Empty };
        if (!snippet.Settings.ValidateCertificates)
        {
            lines.Add("import urllib3");
            lines.Add(string.Empty);
            lines.Add("# The request skips certificate validation; this hides the warning about it.");
            lines.Add("urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)");
            lines.Add(string.Empty);
        }

        lines.Add("url = " + PythonString(snippet.Url));
        var arguments = new List<string> { PythonString(snippet.Method), "url" };

        var headers = MergeDuplicates(snippet.Headers);
        if (snippet.ContentType is { } contentType)
        {
            headers.Add(new("Content-Type", contentType));
        }

        if (headers.Count > 0)
        {
            lines.Add("headers = {");
            lines.AddRange(headers.Select(h => "    " + PythonString(h.Key) + ": " + PythonString(h.Value) + ","));
            lines.Add("}");
            arguments.Add("headers=headers");
        }

        string? openFile = null;
        switch (snippet.Body)
        {
            case BytesBody:
                var text = snippet.Text!;
                lines.Add("payload = " + PythonString(text, multiLine: true));

                // A str body would be sent as ISO-8859-1.
                arguments.Add(IsAscii(text) ? "data=payload" : "data=payload.encode(\"utf-8\")");
                break;
            case FormBody form when HasDuplicateKeys(form.Fields, StringComparer.Ordinal):
                lines.Add("payload = [");
                lines.AddRange(form.Fields.Select(f => "    (" + PythonString(f.Key) + ", " + PythonString(f.Value) + "),"));
                lines.Add("]");
                arguments.Add("data=payload");
                break;
            case FormBody form:
                lines.Add("payload = {");
                lines.AddRange(form.Fields.Select(f => "    " + PythonString(f.Key) + ": " + PythonString(f.Value) + ","));
                lines.Add("}");
                arguments.Add("data=payload");
                break;
            case FileBody file:
                openFile = file.FilePath;
                arguments.Add("data=payload");
                break;
            case MultipartBody multipart:
                lines.Add("files = [");
                foreach (var part in multipart.Parts)
                {
                    var value = part.IsFile
                        ? "(" + PythonString(FileName(part.Value)) + ", open(" + PythonString(part.Value) + ", \"rb\"), " + PythonString(part.ContentType ?? "application/octet-stream") + ")"
                        : part.ContentType is null
                            ? "(None, " + PythonString(part.Value) + ")"
                            : "(None, " + PythonString(part.Value) + ", " + PythonString(part.ContentType) + ")";
                    lines.Add("    (" + PythonString(part.Name) + ", " + value + "),");
                }

                lines.Add("]");
                arguments.Add("files=files");
                break;
        }

        if (!snippet.Settings.FollowRedirects)
        {
            arguments.Add("allow_redirects=False");
        }

        if (!snippet.Settings.ValidateCertificates)
        {
            arguments.Add("verify=False");
        }

        if (snippet.Settings.Timeout is { } timeout)
        {
            arguments.Add("timeout=" + Seconds(timeout));
        }

        lines.Add(string.Empty);
        var call = "response = requests.request(" + string.Join(", ", arguments) + ")";
        if (openFile is not null)
        {
            lines.Add("with open(" + PythonString(openFile) + ", \"rb\") as payload:");
            lines.Add("    " + call);
        }
        else
        {
            lines.Add(call);
        }

        lines.Add(string.Empty);
        lines.Add("print(response.status_code)");
        lines.Add("print(response.text)");
        return JoinLines(lines);
    }

    /// <summary>A Python string: triple-quoted for multi-line text, otherwise double-quoted.</summary>
    private static string PythonString(string value, bool multiLine = false)
    {
        var triple = multiLine && value.Contains('\n', StringComparison.Ordinal);
        var result = new StringBuilder(triple ? "\"\"\"" : "\"");
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            result.Append(c switch
            {
                '\\' => "\\\\",

                // In a triple-quoted string a quote needs escaping only where it could close the string.
                '"' when triple && i + 1 < value.Length && value[i + 1] != '"' => "\"",
                '"' => "\\\"",
                '\n' when triple => "\n",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) => Invariant($"\\x{(int)c:x2}"),
                _ when IsLoneSurrogate(value, i) => Invariant($"\\u{(int)c:x4}"),
                _ => c.ToString(),
            });
        }

        return result.Append(triple ? "\"\"\"" : "\"").ToString();
    }
}
