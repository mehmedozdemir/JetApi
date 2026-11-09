using System.Globalization;
using JetApi.Core.Variables;

namespace JetApi.Cli;

/// <summary>What the command line asks for.</summary>
internal enum CliCommand
{
    /// <summary>Run requests.</summary>
    Run,

    /// <summary>Print the usage.</summary>
    Help,

    /// <summary>Print the version.</summary>
    Version,
}

/// <summary>Options of <c>jetapi run</c>.</summary>
internal sealed record CliOptions
{
    /// <summary>The workspace folder.</summary>
    public string Workspace { get; init; } = string.Empty;

    /// <summary>"collection", "collection/folder" or "collection/folder/request" (keys or names); every collection when <c>null</c>.</summary>
    public string? Target { get; init; }

    /// <summary>Environment key or name.</summary>
    public string? Environment { get; init; }

    /// <summary>CSV or JSON data file.</summary>
    public string? DataFile { get; init; }

    /// <summary>Iterations without a data file.</summary>
    public int Iterations { get; init; } = 1;

    /// <summary>Delay between requests.</summary>
    public int DelayMs { get; init; }

    /// <summary>Stop at the first failure.</summary>
    public bool Bail { get; init; }

    /// <summary>Timeout of requests that do not set their own.</summary>
    public int TimeoutMs { get; init; } = 30_000;

    /// <summary>Variables from <c>--var</c>, over the workspace's.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Variables { get; init; } = [];

    /// <summary>JUnit XML report file.</summary>
    public string? JUnitPath { get; init; }

    /// <summary>HTML report file.</summary>
    public string? HtmlPath { get; init; }

    /// <summary>Write extracted values to the environment, collection or globals.</summary>
    public bool Persist { get; init; }

    /// <summary>"en" or "tr".</summary>
    public string? Language { get; init; }

    /// <summary>No colors.</summary>
    public bool NoColor { get; init; }
}

/// <summary>The parsed command line, or why it could not be parsed.</summary>
internal sealed record CliParseResult(CliCommand Command, CliOptions Options, string? Error)
{
    public bool IsSuccess => Error is null;
}

/// <summary>Parses <c>jetapi</c> arguments.</summary>
internal static class CliParser
{
    /// <summary>The <c>--lang</c> value, read before anything is printed.</summary>
    public static string? Language(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--lang" && i + 1 < args.Count)
            {
                return args[i + 1];
            }

            if (args[i].StartsWith("--lang=", StringComparison.Ordinal))
            {
                return args[i]["--lang=".Length..];
            }
        }

        return null;
    }

    /// <summary>Parses the arguments.</summary>
    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new CliOptions();
        if (args.Count == 0 || args[0] is "-h" or "--help" or "help" || args.Contains("--help") || args.Contains("-h"))
        {
            return new CliParseResult(CliCommand.Help, options, null);
        }

        if (args[0] is "--version" or "-v" or "version")
        {
            return new CliParseResult(CliCommand.Version, options, null);
        }

        if (args[0] != "run")
        {
            return Fail(Messages.Format("Cli_UnknownCommand", args[0]));
        }

        var positional = new List<string>();
        var variables = new List<KeyValuePair<string, string>>();
        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith('-') || arg == "-")
            {
                positional.Add(arg);
                continue;
            }

            var name = arg;
            string? inline = null;
            var equals = arg.IndexOf('=', StringComparison.Ordinal);
            if (arg.StartsWith("--", StringComparison.Ordinal) && equals > 0)
            {
                name = arg[..equals];
                inline = arg[(equals + 1)..];
            }

            string? error = null;
            switch (name)
            {
                case "--bail":
                    options = options with { Bail = true };
                    continue;
                case "--persist":
                    options = options with { Persist = true };
                    continue;
                case "--no-color":
                    options = options with { NoColor = true };
                    continue;
            }

            if (name is not ("-e" or "--env" or "-d" or "--data" or "-n" or "--iterations" or "--delay" or "--timeout" or "--junit" or "--html" or "--lang" or "--var"))
            {
                return Fail(Messages.Format("Cli_UnknownOption", name));
            }

            if (Value() is not { } value)
            {
                return Fail(Messages.Format("Cli_MissingValue", name));
            }

            switch (name)
            {
                case "-e" or "--env":
                    options = options with { Environment = value };
                    break;
                case "-d" or "--data":
                    options = options with { DataFile = value };
                    break;
                case "-n" or "--iterations":
                    options = options with { Iterations = Number(value, 1, ref error) };
                    break;
                case "--delay":
                    options = options with { DelayMs = Number(value, 0, ref error) };
                    break;
                case "--timeout":
                    options = options with { TimeoutMs = Number(value, 1, ref error) };
                    break;
                case "--junit":
                    options = options with { JUnitPath = value };
                    break;
                case "--html":
                    options = options with { HtmlPath = value };
                    break;
                case "--lang":
                    error = value is "en" or "tr" ? null : Messages.Format("Cli_InvalidLanguage", value);
                    options = options with { Language = value };
                    break;
                case "--var":
                    var separator = value.IndexOf('=', StringComparison.Ordinal);
                    if (separator <= 0 || !VariableResolver.IsValidVariableName(value[..separator]))
                    {
                        error = Messages.Format("Cli_InvalidVar", value);
                    }
                    else
                    {
                        variables.Add(new(value[..separator], value[(separator + 1)..]));
                    }

                    break;
                default:
                    error = Messages.Format("Cli_UnknownOption", name);
                    break;
            }

            if (error is not null)
            {
                return Fail(error);
            }

            string? Value()
            {
                if (inline is not null)
                {
                    return inline;
                }

                if (i + 1 < args.Count)
                {
                    return args[++i];
                }

                return null;
            }

            int Number(string text, int minimum, ref string? problem)
            {
                if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum)
                {
                    return number;
                }

                problem = Messages.Format("Cli_InvalidNumber", name, minimum, text);
                return minimum;
            }
        }

        if (positional.Count == 0)
        {
            return Fail(Messages.Get("Cli_MissingWorkspace"));
        }

        if (positional.Count > 2)
        {
            return Fail(Messages.Format("Cli_UnexpectedArgument", positional[2]));
        }

        return new CliParseResult(
            CliCommand.Run,
            options with { Workspace = positional[0], Target = positional.Count > 1 ? positional[1] : null, Variables = variables },
            null);

        CliParseResult Fail(string error) => new(CliCommand.Run, options, error);
    }
}
