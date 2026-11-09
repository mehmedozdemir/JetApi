using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
using JetApi.Core.Model;

namespace JetApi.Runner;

/// <summary>What a report is about.</summary>
/// <param name="Title">"Users" (collection or folder).</param>
/// <param name="Environment">The environment's name, if any.</param>
/// <param name="StartedAt">When the run started.</param>
public sealed record ReportInfo(string Title, string? Environment, DateTimeOffset StartedAt);

/// <summary>Human-readable descriptions of checks, in the UI language.</summary>
public static class CheckText
{
    /// <summary>"Status equals 201", "Content-Type contains json", "$.id exists", "Matches the JSON Schema".</summary>
    public static string Describe(ResponseCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (check.Source == CheckSource.Schema)
        {
            return Messages.Get("Report_Schema");
        }

        var subject = check.Source switch
        {
            CheckSource.Header or CheckSource.Json => check.Target,
            CheckSource.Status => Messages.Get("Report_Status"),
            CheckSource.Body => Messages.Get("Report_Body"),
            _ => Messages.Get("Report_Time"),
        };
        var op = Messages.Get("Op_" + check.Operator);
        return check.Operator is CheckOperator.Exists or CheckOperator.NotExists
            ? $"{subject} {op}"
            : $"{subject} {op} {check.Expected}{(check.Source == CheckSource.Time ? " ms" : string.Empty)}";
    }
}

/// <summary>Writes run results as JUnit XML (for CI) or a single-file HTML page.</summary>
public static class RunReports
{
    /// <summary>
    /// JUnit XML: one test suite per iteration, one test case per check (a request without checks is one case that passes
    /// when a response came); a request without a response is an error.
    /// </summary>
    public static string JUnit(RunSummary summary, ReportInfo info)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(info);
        var builder = new StringBuilder();
        using (var xml = XmlWriter.Create(builder, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8, OmitXmlDeclaration = false }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("testsuites");
            xml.WriteAttributeString("name", info.Title);
            xml.WriteAttributeString("tests", Cases(summary.Results).ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("failures", summary.FailedChecks.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("errors", summary.FailedRequests.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("time", Seconds(summary.Duration));
            foreach (var iteration in summary.Results.GroupBy(r => r.Iteration))
            {
                var results = iteration.ToList();
                xml.WriteStartElement("testsuite");
                xml.WriteAttributeString("name", Messages.Format("Report_Iteration", info.Title, iteration.Key));
                xml.WriteAttributeString("tests", Cases(results).ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("failures", results.Sum(r => r.Checks.Count(c => !c.Passed)).ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("errors", results.Count(r => r.Send.Error is not null).ToString(CultureInfo.InvariantCulture));
                xml.WriteAttributeString("time", Seconds(TimeSpan.FromTicks(results.Sum(r => r.Duration.Ticks))));
                xml.WriteAttributeString("timestamp", info.StartedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
                foreach (var result in results)
                {
                    WriteCases(xml, result);
                }

                xml.WriteEndElement();
            }

            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        // StringBuilder output declares UTF-16; the file is written as UTF-8.
        return builder.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"", StringComparison.Ordinal);
    }

    /// <summary>A self-contained HTML page (no external files), following the system's light or dark theme.</summary>
    public static string Html(RunSummary summary, ReportInfo info)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(info);
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>").Append(E(info.Title)).Append(" · JetApi</title><style>")
            .Append(":root{color-scheme:light dark;--bg:#fff;--fg:#1a1d23;--muted:#5b6170;--line:#e6e8ec;--soft:#f6f7f9;--ok:#1b5e2b;--okbg:#e8f5ec;--bad:#8f1d1d;--badbg:#fdecec}")
            .Append("@media (prefers-color-scheme:dark){:root{--bg:#16181d;--fg:#e8eaed;--muted:#a3a8b3;--line:#2b2f37;--soft:#1e2127;--ok:#7bd88f;--okbg:#17301f;--bad:#ff8a8a;--badbg:#3a1a1a}}")
            .Append("body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,-apple-system,'Segoe UI',sans-serif}main{max-width:1000px;margin:0 auto;padding:24px 16px}")
            .Append("h1{font-size:22px;margin:0 0 4px}.muted{color:var(--muted)}.summary{display:flex;flex-wrap:wrap;gap:8px;margin:16px 0}")
            .Append(".pill{border-radius:6px;padding:3px 10px;background:var(--soft)}.ok{color:var(--ok);background:var(--okbg)}.bad{color:var(--bad);background:var(--badbg)}")
            .Append("section{border:1px solid var(--line);border-radius:8px;margin:12px 0;overflow:hidden}section h2{font-size:13px;margin:0;padding:8px 12px;background:var(--soft);color:var(--muted)}")
            .Append(".req{padding:8px 12px;border-top:1px solid var(--line)}.row{display:flex;gap:10px;align-items:baseline;flex-wrap:wrap}.name{flex:1 1 200px;font-weight:600}")
            .Append("code{font:12.5px ui-monospace,'Cascadia Mono',Consolas,monospace}ul{margin:6px 0 0;padding-left:22px}li{margin:2px 0}.fail{color:var(--bad)}.pass{color:var(--ok)}")
            .Append("</style></head><body><main>");
        html.Append("<h1>").Append(E(info.Title)).Append("</h1><div class=\"muted\">")
            .Append(E(info.StartedAt.ToString("f", CultureInfo.CurrentCulture)))
            .Append(info.Environment is null ? string.Empty : " · " + E(info.Environment))
            .Append(" · ").Append(E(Duration(summary.Duration))).Append("</div>");
        html.Append("<div class=\"summary\">")
            .Append("<span class=\"pill ").Append(summary.Succeeded ? "ok" : "bad").Append("\">").Append(E(Messages.Get(summary.Succeeded ? "Report_Passed" : "Report_Failed"))).Append("</span>")
            .Append("<span class=\"pill\">").Append(E(Messages.Format("Report_Requests", summary.Results.Count))).Append("</span>")
            .Append("<span class=\"pill ok\">").Append(E(Messages.Format("Report_ChecksPassed", summary.PassedChecks))).Append("</span>")
            .Append("<span class=\"pill").Append(summary.FailedChecks > 0 ? " bad" : string.Empty).Append("\">").Append(E(Messages.Format("Report_ChecksFailed", summary.FailedChecks))).Append("</span>");
        if (summary.FailedRequests > 0)
        {
            html.Append("<span class=\"pill bad\">").Append(E(Messages.Format("Report_NoResponse", summary.FailedRequests))).Append("</span>");
        }

        html.Append("</div>");
        if (summary.StoppedEarly || summary.Cancelled)
        {
            html.Append("<p class=\"fail\">").Append(E(Messages.Get(summary.Cancelled ? "Report_Cancelled" : "Report_Stopped"))).Append("</p>");
        }

        foreach (var iteration in summary.Results.GroupBy(r => r.Iteration))
        {
            html.Append("<section><h2>").Append(E(Messages.Format("Report_Iteration", info.Title, iteration.Key))).Append("</h2>");
            foreach (var result in iteration)
            {
                html.Append("<div class=\"req\"><div class=\"row\"><span class=\"").Append(result.Passed ? "pass" : "fail").Append("\">").Append(result.Passed ? "✓" : "✗").Append("</span>")
                    .Append("<code>").Append(E(Badge(result.Request.Request))).Append("</code><span class=\"name\">").Append(E(result.Request.Request.Name)).Append("</span>")
                    .Append("<span class=\"muted\">").Append(E(result.Send.StatusText)).Append("</span><code class=\"muted\">").Append(E(Duration(result.Duration))).Append("</code></div>");
                if (result.Send.Error is { } error)
                {
                    html.Append("<ul><li class=\"fail\">").Append(E(error)).Append("</li></ul>");
                }
                else if (result.Checks.Count > 0)
                {
                    html.Append("<ul>");
                    foreach (var check in result.Checks)
                    {
                        html.Append("<li class=\"").Append(check.Passed ? "pass" : "fail").Append("\"><code>").Append(E(CheckText.Describe(check.Check))).Append("</code>");
                        if (!check.Passed)
                        {
                            html.Append(" · ").Append(E(check.Message ?? string.Empty));
                        }

                        html.Append("</li>");
                    }

                    html.Append("</ul>");
                }

                html.Append("</div>");
            }

            html.Append("</section>");
        }

        html.Append("<p class=\"muted\">JetApi</p></main></body></html>");
        return html.ToString();
    }

    private static void WriteCases(XmlWriter xml, RequestRunResult result)
    {
        var className = string.Join('.', [result.Request.CollectionKey, .. result.Request.Path.SkipLast(1)]);
        var time = Seconds(result.Duration);
        if (result.Send.Error is { } error)
        {
            Case(result.Request.Request.Name, () =>
            {
                xml.WriteStartElement("error");
                xml.WriteAttributeString("message", error);
                xml.WriteEndElement();
            });
            return;
        }

        if (result.Checks.Count == 0)
        {
            Case(result.Request.Request.Name + " · " + result.Send.StatusText, null);
            return;
        }

        foreach (var check in result.Checks)
        {
            Case($"{result.Request.Request.Name} · {CheckText.Describe(check.Check)}", check.Passed ? null : () =>
            {
                xml.WriteStartElement("failure");
                xml.WriteAttributeString("message", check.Message ?? string.Empty);
                xml.WriteAttributeString("type", check.Check.Source.ToString());
                if (check.Actual is { } actual)
                {
                    xml.WriteString(actual);
                }

                xml.WriteEndElement();
            });
        }

        void Case(string name, Action? body)
        {
            xml.WriteStartElement("testcase");
            xml.WriteAttributeString("classname", className);
            xml.WriteAttributeString("name", name);
            xml.WriteAttributeString("time", time);
            body?.Invoke();
            xml.WriteEndElement();
        }
    }

    private static int Cases(IEnumerable<RequestRunResult> results) => results.Sum(r => r.Send.Error is null && r.Checks.Count > 0 ? r.Checks.Count : 1);

    private static string Seconds(TimeSpan duration) => duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);

    private static string Duration(TimeSpan duration) =>
        duration.TotalSeconds < 1 ? $"{(int)duration.TotalMilliseconds} ms" : string.Create(CultureInfo.CurrentCulture, $"{duration.TotalSeconds:0.0} s");

    private static string Badge(RequestNode request) => request is HttpRequestDefinition http ? http.Method : "gRPC";

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
