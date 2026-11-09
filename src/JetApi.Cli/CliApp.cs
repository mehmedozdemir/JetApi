using System.Globalization;
using System.Text;
using JetApi.Core.Model;
using JetApi.Runner;
using JetApi.Storage;

namespace JetApi.Cli;

/// <summary>Exit codes of <c>jetapi</c>.</summary>
internal static class ExitCodes
{
    /// <summary>Every request got a response and every check passed.</summary>
    public const int Passed = 0;

    /// <summary>A check failed, a request got no response, or the run stopped early.</summary>
    public const int Failed = 1;

    /// <summary>Wrong arguments, or the workspace, environment, target, data file or a report could not be used.</summary>
    public const int Usage = 2;
}

/// <summary>The <c>jetapi</c> command line.</summary>
internal static class CliApp
{
    /// <summary>The version, "0.3.0".</summary>
    public static string Version => typeof(CliApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Runs the command line.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="color">Use ANSI colors.</param>
    /// <param name="cancellationToken">Stops the run (Ctrl+C).</param>
    /// <returns>The exit code (<see cref="ExitCodes"/>).</returns>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, bool color, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        var language = CliParser.Language(args);
        if (language is "en" or "tr")
        {
            var culture = CultureInfo.GetCultureInfo(language == "tr" ? "tr-TR" : "en-US");
            CultureInfo.CurrentUICulture = culture;
        }

        var parsed = CliParser.Parse(args);
        if (!parsed.IsSuccess)
        {
            await error.WriteLineAsync(parsed.Error).ConfigureAwait(false);
            await error.WriteLineAsync(Messages.Get("Cli_SeeHelp")).ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        switch (parsed.Command)
        {
            case CliCommand.Help:
                await output.WriteLineAsync(Messages.Format("Cli_Usage", Version)).ConfigureAwait(false);
                return ExitCodes.Passed;
            case CliCommand.Version:
                await output.WriteLineAsync(Version).ConfigureAwait(false);
                return ExitCodes.Passed;
            default:
                var console = new ConsoleOutput(output, error, color && !parsed.Options.NoColor);
                return await RunCollectionAsync(parsed.Options, console, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<int> RunCollectionAsync(CliOptions options, ConsoleOutput console, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(options.Workspace);
        if (!WorkspaceStore.IsWorkspace(root))
        {
            return console.Fail(Messages.Format("Cli_NotWorkspace", root));
        }

        var loaded = await WorkspaceStore.LoadAsync(root, cancellationToken).ConfigureAwait(false);
        foreach (var diagnostic in loaded.Diagnostics)
        {
            console.Warn(Messages.Format("Cli_SkippedFile", Path.GetRelativePath(root, diagnostic.FilePath), diagnostic.Message));
        }

        var workspace = loaded.Workspace;
        ApiEnvironment? environment = null;
        if (options.Environment is { } environmentName)
        {
            environment = workspace.Environments.FirstOrDefault(e => e.Key == environmentName)
                ?? workspace.Environments.FirstOrDefault(e => string.Equals(e.Name, environmentName, StringComparison.OrdinalIgnoreCase));
            if (environment is null)
            {
                return console.Fail(Messages.Format("Cli_UnknownEnvironment", environmentName, Choices(workspace.Environments.Select(e => e.Name))));
            }
        }

        var target = RunTarget.Find(workspace, options.Target);
        if (target.Error is { } targetError)
        {
            return console.Fail(targetError);
        }

        if (target.Requests.Count == 0)
        {
            return console.Fail(Messages.Get("Cli_NothingToRun"));
        }

        // Postman scripts kept from an import do not run (ADR-0010); say so rather than pass silently.
        if (target.Requests.Count(r => !r.Request.Scripts.IsEmpty) is > 0 and var withScripts)
        {
            console.Warn(Messages.Format("Cli_ScriptsNotRun", withScripts));
        }

        IReadOnlyList<IReadOnlyDictionary<string, string>>? rows = null;
        if (options.DataFile is { } dataPath)
        {
            var data = await DataFile.LoadAsync(Path.GetFullPath(dataPath), cancellationToken).ConfigureAwait(false);
            if (!data.IsSuccess)
            {
                return console.Fail(Messages.Format("Cli_DataError", dataPath, data.Error));
            }

            rows = data.Rows;
        }

        var runOptions = new RunOptions(options.Iterations, TimeSpan.FromMilliseconds(options.DelayMs), options.Bail, rows, options.Persist);
        var iterations = rows?.Count ?? options.Iterations;
        var info = new ReportInfo(target.Title, environment?.Name, DateTimeOffset.Now);
        console.Header(Version, target.Title, environment?.Name);

        using var sender = new CliSender(root, workspace, environment, options.Variables, TimeSpan.FromMilliseconds(options.TimeoutMs));
        var summary = await CollectionRunner.RunAsync(
            target.Requests,
            runOptions,
            sender,
            (index, row) => console.Iteration(index, iterations, row),
            console.Result,
            cancellationToken).ConfigureAwait(false);
        console.Summary(summary);

        // Both reports are attempted even when the first cannot be written.
        var junitWritten = await WriteReportAsync(options.JUnitPath, "JUnit", () => RunReports.JUnit(summary, info), console).ConfigureAwait(false);
        var htmlWritten = await WriteReportAsync(options.HtmlPath, "HTML", () => RunReports.Html(summary, info), console).ConfigureAwait(false);
        if (!junitWritten || !htmlWritten)
        {
            return ExitCodes.Usage;
        }

        return summary.Succeeded ? ExitCodes.Passed : ExitCodes.Failed;
    }

    private static async Task<bool> WriteReportAsync(string? path, string kind, Func<string> render, ConsoleOutput console)
    {
        if (path is null)
        {
            return true;
        }

        var fullPath = Path.GetFullPath(path);
        try
        {
            if (Path.GetDirectoryName(fullPath) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(fullPath, render(), new UTF8Encoding(false)).ConfigureAwait(false);
            console.Note(Messages.Format("Cli_ReportWritten", kind, fullPath));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            console.Fail(Messages.Format("Cli_ReportFailed", fullPath, exception.Message));
            return false;
        }
    }

    private static string Choices(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count == 0 ? Messages.Get("Cli_None") : string.Join(", ", list);
    }

    /// <summary>The requests that <c>[target]</c> names.</summary>
    private sealed record RunTarget(string Title, IReadOnlyList<RunRequest> Requests, string? Error)
    {
        // Each segment matches a key, else a name (ignoring case): "users/admin", "Users/Admin users".
        public static RunTarget Find(Workspace workspace, string? target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return new RunTarget(
                    workspace.Name,
                    [.. workspace.Collections.SelectMany(c => CollectionTree.EnumerateRequests(c).Select(r => new RunRequest(c.Key, r.Path, r.Request)))],
                    null);
            }

            var segments = target.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var collection = Match(workspace.Collections, segments[0], c => c.Key, c => c.Name);
            if (collection is null)
            {
                return Missing(segments[0], workspace.Name, workspace.Collections.Select(c => c.Name));
            }

            var path = new List<string>();
            string title = collection.Name;
            IReadOnlyList<CollectionNode> children = collection.Children;
            foreach (var segment in segments.Skip(1))
            {
                var node = Match(children, segment, n => n.Key, n => n.Name);
                if (node is null)
                {
                    return Missing(segment, title, children.Select(n => n.Name));
                }

                path.Add(node.Key);
                title = node.Name;
                children = node is Folder folder ? folder.Children : [];
            }

            var requests = CollectionTree.EnumerateRequests(collection)
                .Where(r => r.Path.Take(path.Count).SequenceEqual(path))
                .Select(r => new RunRequest(collection.Key, r.Path, r.Request))
                .ToList();
            return new RunTarget(title, requests, null);

            static RunTarget Missing(string name, string parent, IEnumerable<string> choices) =>
                new(string.Empty, [], Messages.Format("Cli_UnknownTarget", name, parent, Choices(choices)));
        }

        private static T? Match<T>(IEnumerable<T> items, string text, Func<T, string> key, Func<T, string> name)
            where T : class =>
            items.FirstOrDefault(i => key(i) == text) ?? items.FirstOrDefault(i => string.Equals(name(i), text, StringComparison.OrdinalIgnoreCase));
    }
}
