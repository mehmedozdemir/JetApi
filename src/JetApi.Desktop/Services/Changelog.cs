using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JetApi.Desktop.Services;

/// <summary>One version's notes.</summary>
/// <param name="Version">The version; <c>null</c> for the "Unreleased" section.</param>
/// <param name="Date">Release date, when given.</param>
/// <param name="Markdown">The section body (without its heading).</param>
public sealed record ReleaseNotes(ReleaseVersion? Version, DateOnly? Date, string Markdown)
{
    /// <summary>Where relative links in the notes point: the repository at this version's tag.</summary>
    public Uri LinkBase => new(AppInfo.RepositoryUrl + "/blob/" + (Version is null ? "HEAD" : "v" + Version) + "/");
}

/// <summary>
/// Reads <c>CHANGELOG.md</c> (Keep a Changelog: <c>## [0.3.0] - 2026-11-09</c> sections). The app carries its own copy as a
/// resource, so release notes need no network.
/// </summary>
public static partial class Changelog
{
    private static readonly Lazy<IReadOnlyList<ReleaseNotes>> Bundled = new(() => Parse(ReadBundled()));

    /// <summary>The notes shipped with this build, newest first; empty "Unreleased" sections are left out.</summary>
    public static IReadOnlyList<ReleaseNotes> Current => Bundled.Value;

    /// <summary>Splits a changelog into version sections, in file order (newest first).</summary>
    public static IReadOnlyList<ReleaseNotes> Parse(string changelog)
    {
        ArgumentNullException.ThrowIfNull(changelog);
        var sections = new List<ReleaseNotes>();
        ReleaseVersion? version = null;
        DateOnly? date = null;
        StringBuilder? body = null;

        void Flush()
        {
            if (body is not null && body.ToString().Trim() is { Length: > 0 } text)
            {
                sections.Add(new ReleaseNotes(version, date, text));
            }
        }

        foreach (var line in changelog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (SectionPattern().Match(line) is { Success: true } section)
            {
                Flush();
                var label = section.Groups[1].Value;
                version = ReleaseVersion.TryParse(label);
                if (version is null && !label.Equals("Unreleased", StringComparison.OrdinalIgnoreCase))
                {
                    body = null;
                    continue;
                }

                date = DateOnly.TryParseExact(section.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
                body = new StringBuilder();
                continue;
            }

            // Link reference definitions at the end of the file ("[0.3.0]: https://…") are not notes.
            if (body is not null && !LinkDefinitionPattern().IsMatch(line))
            {
                body.Append(line).Append('\n');
            }
        }

        Flush();
        return sections;
    }

    /// <summary>The section for <paramref name="version"/>, or <c>null</c>.</summary>
    public static ReleaseNotes? Find(IReadOnlyList<ReleaseNotes> notes, ReleaseVersion version)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return notes.FirstOrDefault(n => n.Version == version);
    }

    private static string ReadBundled()
    {
        using var stream = typeof(Changelog).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"^##\s+\[([^\]]+)\](?:\s*-\s*(\d{4}-\d{2}-\d{2}))?")]
    private static partial Regex SectionPattern();

    [GeneratedRegex(@"^\[[^\]]+\]:\s+\S+")]
    private static partial Regex LinkDefinitionPattern();
}
