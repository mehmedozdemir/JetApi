using JetApi.Core.Model;
using JetApi.Runner;

namespace JetApi.Cli;

/// <summary>Prints the run as it happens: one line per request, one per check, then a summary.</summary>
internal sealed class ConsoleOutput(TextWriter output, TextWriter error, bool color)
{
    private const string Green = "\u001b[32m";
    private const string Red = "\u001b[31m";
    private const string Yellow = "\u001b[33m";
    private const string Dim = "\u001b[2m";
    private const string Bold = "\u001b[1m";
    private const string Reset = "\u001b[0m";

    /// <summary>"JetApi 0.3.0 · Users · Development".</summary>
    public void Header(string version, string title, string? environment) =>
        output.WriteLine(Paint(Bold, $"JetApi {version} · {title}") + Paint(Dim, " · " + (environment ?? Messages.Get("Cli_NoEnvironment"))));

    /// <summary>"Iteration 2/3 · email=ali@example.com".</summary>
    public void Iteration(int index, int count, IReadOnlyDictionary<string, string>? row)
    {
        output.WriteLine();
        output.WriteLine(Paint(Bold, row is null
            ? Messages.Format("Cli_Iteration", index, count)
            : Messages.Format("Cli_IterationRow", index, count, string.Join(", ", row.Select(c => $"{c.Key}={c.Value}")))));
    }

    /// <summary>"  ✓ POST Create user · 201 Created · 84 ms" and the checks under it.</summary>
    public void Result(RequestRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var badge = result.Request.Request is HttpRequestDefinition http ? http.Method : "gRPC";
        var mark = result.Passed ? Paint(Green, "✓") : Paint(Red, "✗");
        var status = result.Send.Error is null ? $" · {result.Send.StatusText}" : string.Empty;
        output.WriteLine($"  {mark} {Paint(Dim, badge)} {result.Request.Request.Name}{status}{Paint(Dim, " · " + Duration(result.Duration))}");
        if (result.Send.Error is { } sendError)
        {
            foreach (var line in sendError.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                output.WriteLine("      " + Paint(Red, line));
            }

            return;
        }

        foreach (var check in result.Checks)
        {
            var text = CheckText.Describe(check.Check);
            output.WriteLine(check.Passed
                ? $"      {Paint(Green, "✓")} {Paint(Dim, text)}"
                : $"      {Paint(Red, "✗")} {text} · {Paint(Red, check.Message ?? string.Empty)}");
        }

        foreach (var extraction in result.Extractions.Where(e => e.Error is not null))
        {
            output.WriteLine($"      {Paint(Yellow, "!")} {extraction.Extraction.Variable} · {extraction.Error}");
        }
    }

    /// <summary>The totals and whether the run passed.</summary>
    public void Summary(RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        output.WriteLine();
        if (summary.Cancelled || summary.StoppedEarly)
        {
            output.WriteLine(Paint(Yellow, Messages.Get(summary.Cancelled ? "Cli_Cancelled" : "Cli_Stopped")));
        }

        var line = Messages.Format("Cli_Summary", summary.Results.Count, summary.PassedChecks, summary.FailedChecks, Duration(summary.Duration));
        if (summary.FailedRequests > 0)
        {
            line += " · " + Messages.Format("Cli_NoResponse", summary.FailedRequests);
        }

        output.WriteLine(Paint(summary.Succeeded ? Green : Red, Paint(Bold, Messages.Get(summary.Succeeded ? "Cli_Passed" : "Cli_Failed"))) + " · " + line);
    }

    /// <summary>A note after the run (a report was written).</summary>
    public void Note(string text) => output.WriteLine(Paint(Dim, text));

    /// <summary>A warning on standard error (a skipped file).</summary>
    public void Warn(string text) => error.WriteLine(Paint(Yellow, text));

    /// <summary>An error on standard error.</summary>
    /// <returns><see cref="ExitCodes.Usage"/>.</returns>
    public int Fail(string text)
    {
        error.WriteLine(Paint(Red, text));
        return ExitCodes.Usage;
    }

    private string Paint(string code, string text) => color && text.Length > 0 ? code + text + Reset : text;

    private static string Duration(TimeSpan duration) =>
        duration.TotalSeconds < 1 ? $"{(int)duration.TotalMilliseconds} ms" : string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{duration.TotalSeconds:0.0} s");
}
