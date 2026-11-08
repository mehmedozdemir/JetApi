using System.Globalization;
using System.Text;

namespace JetApi.Http;

public static partial class CodeSnippetGenerator
{
    private static readonly Dictionary<string, string> CSharpMethods = new(StringComparer.Ordinal)
    {
        ["GET"] = "Get",
        ["POST"] = "Post",
        ["PUT"] = "Put",
        ["DELETE"] = "Delete",
        ["PATCH"] = "Patch",
        ["HEAD"] = "Head",
        ["OPTIONS"] = "Options",
        ["TRACE"] = "Trace",
        ["CONNECT"] = "Connect",
    };

    // HttpClient as top-level statements (a .cs file of its own, or "dotnet run app.cs").
    private static string CSharp(Snippet snippet)
    {
        var lines = new List<string>();
        var needsHeaders = snippet.ContentType is not null || snippet.Body is MultipartBody { Parts: var parts } && parts.Any(p => p.IsFile || p.ContentType is not null);
        if (needsHeaders)
        {
            lines.Add("using System.Net.Http.Headers;");
            lines.Add(string.Empty);
        }

        if (!snippet.Settings.FollowRedirects || !snippet.Settings.ValidateCertificates)
        {
            lines.Add("using var handler = new HttpClientHandler");
            lines.Add("{");
            if (!snippet.Settings.FollowRedirects)
            {
                lines.Add("    AllowAutoRedirect = false,");
            }

            if (!snippet.Settings.ValidateCertificates)
            {
                lines.Add("    // Accepts any certificate, like the request's settings. Use only with local development servers.");
                lines.Add("    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,");
            }

            lines.Add("};");
        }

        var client = "using var client = new HttpClient(" + (lines.Contains("};") ? "handler" : string.Empty) + ")";
        lines.Add(snippet.Settings.Timeout is { } timeout ? client + " { Timeout = " + CSharpTimeSpan(timeout) + " };" : client + ";");
        lines.Add(string.Empty);

        var method = CSharpMethods.TryGetValue(snippet.Method, out var known) ? "HttpMethod." + known : "new HttpMethod(" + CSharpString(snippet.Method) + ")";
        lines.Add("using var request = new HttpRequestMessage(" + method + ", " + CSharpString(snippet.Url) + ");");
        foreach (var (name, value) in snippet.Headers.Where(h => !snippet.HasBody || !ContentHeaders.Contains(h.Key)))
        {
            lines.Add("request.Headers.TryAddWithoutValidation(" + CSharpString(name) + ", " + CSharpString(value) + ");");
        }

        switch (snippet.Body)
        {
            case BytesBody:
                lines.Add("var content = new StringContent(" + CSharpString(snippet.Text!, multiLine: true) + ");");
                break;
            case FormBody form:
                lines.Add("var content = new FormUrlEncodedContent(");
                lines.Add("[");
                lines.AddRange(form.Fields.Select(f => "    new KeyValuePair<string, string>(" + CSharpString(f.Key) + ", " + CSharpString(f.Value) + "),"));
                lines.Add("]);");
                break;
            case FileBody file:
                lines.Add("var content = new StreamContent(File.OpenRead(" + CSharpString(file.FilePath) + "));");
                break;
            case MultipartBody multipart:
                lines.Add("var content = new MultipartFormDataContent();");
                var number = 0;
                foreach (var part in multipart.Parts)
                {
                    number++;
                    if (part.IsFile)
                    {
                        var file = Invariant($"file{number}");
                        lines.Add("var " + file + " = new StreamContent(File.OpenRead(" + CSharpString(part.Value) + "));");
                        lines.Add(file + ".Headers.ContentType = MediaTypeHeaderValue.Parse(" + CSharpString(part.ContentType ?? "application/octet-stream") + ");");
                        lines.Add("content.Add(" + file + ", " + CSharpString(part.Name) + ", " + CSharpString(FileName(part.Value)) + ");");
                    }
                    else if (part.ContentType is not null)
                    {
                        var text = Invariant($"part{number}");
                        lines.Add("var " + text + " = new StringContent(" + CSharpString(part.Value) + ");");
                        lines.Add(text + ".Headers.ContentType = MediaTypeHeaderValue.Parse(" + CSharpString(part.ContentType) + ");");
                        lines.Add("content.Add(" + text + ", " + CSharpString(part.Name) + ");");
                    }
                    else
                    {
                        lines.Add("content.Add(new StringContent(" + CSharpString(part.Value) + "), " + CSharpString(part.Name) + ");");
                    }
                }

                break;
        }

        if (snippet.ContentType is { } contentType)
        {
            lines.Add("content.Headers.ContentType = MediaTypeHeaderValue.Parse(" + CSharpString(contentType) + ");");
        }

        foreach (var (name, value) in snippet.Headers.Where(h => snippet.HasBody && ContentHeaders.Contains(h.Key)))
        {
            lines.Add("content.Headers.TryAddWithoutValidation(" + CSharpString(name) + ", " + CSharpString(value) + ");");
        }

        if (snippet.HasBody)
        {
            lines.Add("request.Content = content;");
        }

        lines.Add(string.Empty);
        lines.Add("using var response = await client.SendAsync(request);");
        lines.Add("Console.WriteLine((int)response.StatusCode);");
        lines.Add("Console.WriteLine(await response.Content.ReadAsStringAsync());");
        return JoinLines(lines);
    }

    private static string CSharpTimeSpan(TimeSpan timeout) =>
        timeout.TotalSeconds == Math.Floor(timeout.TotalSeconds)
            ? Invariant($"TimeSpan.FromSeconds({(long)timeout.TotalSeconds})")
            : "TimeSpan.FromMilliseconds(" + timeout.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) + ")";

    /// <summary>
    /// A C# string literal: a raw literal for multi-line text (so pretty-printed JSON stays readable), a verbatim
    /// literal for Windows paths, otherwise a regular literal with escapes.
    /// </summary>
    private static string CSharpString(string value, bool multiLine = false)
    {
        var hasOtherControl = value.Any(c => (char.IsControl(c) && c is not '\n' and not '\t') || c is '\u2028' or '\u2029') || HasLoneSurrogate(value);
        if (multiLine && value.Contains('\n', StringComparison.Ordinal) && !hasOtherControl)
        {
            var longestRun = 0;
            var run = 0;
            foreach (var c in value)
            {
                run = c == '"' ? run + 1 : 0;
                longestRun = Math.Max(longestRun, run);
            }

            var delimiter = new string('"', Math.Max(3, longestRun + 1));
            var raw = new StringBuilder(delimiter).Append('\n');
            foreach (var line in value.Split('\n'))
            {
                raw.Append(line.Length == 0 ? string.Empty : "    " + line).Append('\n');
            }

            return raw.Append("    ").Append(delimiter).ToString();
        }

        if (value.Contains('\\', StringComparison.Ordinal) && !hasOtherControl && !value.Contains('\n', StringComparison.Ordinal) && !value.Contains('\t', StringComparison.Ordinal))
        {
            return "@\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        var result = new StringBuilder("\"");
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            result.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ when char.IsControl(c) || c is '\u2028' or '\u2029' || IsLoneSurrogate(value, i) => Invariant($"\\u{(int)c:X4}"),
                _ => c.ToString(),
            });
        }

        return result.Append('"').ToString();
    }

    private static bool HasLoneSurrogate(string value) => Enumerable.Range(0, value.Length).Any(i => IsLoneSurrogate(value, i));

    // A surrogate pair (an emoji, for example) can stay in the source; half of one cannot.
    private static bool IsLoneSurrogate(string value, int index) =>
        char.IsHighSurrogate(value[index]) ? index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])
        : char.IsLowSurrogate(value[index]) && (index == 0 || !char.IsHighSurrogate(value[index - 1]));
}
