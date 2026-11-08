using System.Text;

namespace JetApi.Http;

public static partial class CodeSnippetGenerator
{
    // fetch with top-level await (an ES module in Node.js 20+, or a browser console).
    private static string JavaScript(Snippet snippet)
    {
        var lines = new List<string>();
        var readsFiles = snippet.Body is FileBody || snippet.Body is MultipartBody { Parts: var parts } && parts.Any(p => p.IsFile);
        if (readsFiles)
        {
            lines.Add("import { openAsBlob } from \"node:fs\";");
            lines.Add(string.Empty);
        }

        if (snippet.Body is MultipartBody multipart)
        {
            lines.Add("const form = new FormData();");
            foreach (var part in multipart.Parts)
            {
                lines.Add(part.IsFile
                    ? "form.append(" + JavaScriptString(part.Name) + ", await openAsBlob(" + JavaScriptString(part.Value) + ", { type: "
                        + JavaScriptString(part.ContentType ?? "application/octet-stream") + " }), " + JavaScriptString(FileName(part.Value)) + ");"
                    : "form.append(" + JavaScriptString(part.Name) + ", " + JavaScriptString(part.Value) + ");");
            }

            lines.Add(string.Empty);
        }

        if (!snippet.Settings.ValidateCertificates)
        {
            lines.Add("// The request skips certificate validation. fetch cannot do that per request;");
            lines.Add("// in Node.js, set NODE_TLS_REJECT_UNAUTHORIZED=0 for local development servers only.");
        }

        var options = lines.Count;
        lines.Add("const response = await fetch(" + JavaScriptString(snippet.Url) + ", {");
        if (snippet.Method != "GET")
        {
            lines.Add("  method: " + JavaScriptString(snippet.Method) + ",");
        }

        var headers = snippet.Headers.ToList();
        if (snippet.ContentType is { } contentType)
        {
            headers.Add(new("Content-Type", contentType));
        }

        if (headers.Count > 0 && HasDuplicateKeys(headers, StringComparer.OrdinalIgnoreCase))
        {
            // Pairs keep repeated headers that an object cannot hold.
            lines.Add("  headers: [");
            lines.AddRange(headers.Select(h => "    [" + JavaScriptString(h.Key) + ", " + JavaScriptString(h.Value) + "],"));
            lines.Add("  ],");
        }
        else if (headers.Count > 0)
        {
            lines.Add("  headers: {");
            lines.AddRange(headers.Select(h => "    " + JavaScriptString(h.Key) + ": " + JavaScriptString(h.Value) + ","));
            lines.Add("  },");
        }

        switch (snippet.Body)
        {
            case BytesBody:
                lines.Add("  body: " + JavaScriptString(snippet.Text!, multiLine: true) + ",");
                break;
            case FormBody form:
                lines.Add("  body: new URLSearchParams([");
                lines.AddRange(form.Fields.Select(f => "    [" + JavaScriptString(f.Key) + ", " + JavaScriptString(f.Value) + "],"));
                lines.Add("  ]),");
                break;
            case FileBody file:
                lines.Add("  body: await openAsBlob(" + JavaScriptString(file.FilePath) + "),");
                break;
            case MultipartBody:
                lines.Add("  body: form,");
                break;
        }

        if (!snippet.Settings.FollowRedirects)
        {
            lines.Add("  redirect: \"manual\",");
        }

        if (snippet.Settings.Timeout is { } timeout)
        {
            lines.Add(Invariant($"  signal: AbortSignal.timeout({(long)Math.Ceiling(timeout.TotalMilliseconds)}),"));
        }

        if (lines.Count == options + 1)
        {
            // A plain GET needs no options.
            lines[options] = "const response = await fetch(" + JavaScriptString(snippet.Url) + ");";
        }
        else
        {
            lines.Add("});");
        }

        lines.Add(string.Empty);
        lines.Add("console.log(response.status);");
        lines.Add("console.log(await response.text());");
        return JoinLines(lines);
    }

    /// <summary>A JavaScript string: a template literal for multi-line text, otherwise a double-quoted string.</summary>
    private static string JavaScriptString(string value, bool multiLine = false)
    {
        var template = multiLine && value.Contains('\n', StringComparison.Ordinal);
        var result = new StringBuilder(template ? "`" : "\"");
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            result.Append(c switch
            {
                '\\' => "\\\\",
                '"' when !template => "\\\"",
                '`' when template => "\\`",
                '$' when template && i + 1 < value.Length && value[i + 1] == '{' => "\\$",
                '\n' when template => "\n",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' when !template => "\\t",
                '\t' => "\t",
                _ when char.IsControl(c) || c is '\u2028' or '\u2029' || IsLoneSurrogate(value, i) => Invariant($"\\u{(int)c:X4}"),
                _ => c.ToString(),
            });
        }

        return result.Append(template ? '`' : '"').ToString();
    }
}
