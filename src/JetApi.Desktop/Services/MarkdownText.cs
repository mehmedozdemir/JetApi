using System.Text;
using System.Text.RegularExpressions;

namespace JetApi.Desktop.Services;

/// <summary>Kinds of Markdown blocks the app renders.</summary>
public enum MarkdownBlockKind
{
    /// <summary><c>## Heading</c>.</summary>
    Heading,

    /// <summary>Plain paragraph.</summary>
    Paragraph,

    /// <summary><c>- item</c>; <see cref="MarkdownBlock.Level"/> is the nesting depth (0 for top level).</summary>
    Bullet,

    /// <summary>A fenced code block, shown as is.</summary>
    Code,
}

/// <summary>Kinds of inline Markdown spans.</summary>
public enum MarkdownInlineKind
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary><c>`code`</c>.</summary>
    Code,

    /// <summary><c>**strong**</c>.</summary>
    Strong,

    /// <summary><c>*emphasis*</c>.</summary>
    Emphasis,

    /// <summary><c>[text](url)</c>.</summary>
    Link,
}

/// <summary>An inline span.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Text">Visible text.</param>
/// <param name="Link">Target of a link; absolute.</param>
public sealed record MarkdownInline(MarkdownInlineKind Kind, string Text, Uri? Link = null);

/// <summary>A block: heading (with level), paragraph, bullet (with depth) or code.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Level">Heading level (1-6) or bullet depth (0…).</param>
/// <param name="Inlines">Content.</param>
public sealed record MarkdownBlock(MarkdownBlockKind Kind, int Level, IReadOnlyList<MarkdownInline> Inlines);

/// <summary>
/// The small Markdown subset the changelog and release notes use: headings, paragraphs, nested bullets, fenced code,
/// <c>`code`</c>, <c>**strong**</c>, <c>*emphasis*</c> and links. Anything else is kept as text. UI-free.
/// </summary>
public static partial class MarkdownText
{
    /// <summary>Parses <paramref name="markdown"/>; relative links resolve against <paramref name="linkBase"/> (dropped when it is null).</summary>
    public static IReadOnlyList<MarkdownBlock> Parse(string markdown, Uri? linkBase = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var blocks = new List<MarkdownBlock>();
        var pending = new StringBuilder();
        var pendingKind = MarkdownBlockKind.Paragraph;
        var pendingLevel = 0;
        var bulletIndents = new List<int>();
        var inFence = false;

        void Flush()
        {
            if (pending.Length == 0)
            {
                return;
            }

            var text = pending.ToString();
            blocks.Add(new MarkdownBlock(
                pendingKind,
                pendingLevel,
                pendingKind == MarkdownBlockKind.Code ? [new MarkdownInline(MarkdownInlineKind.Text, text)] : ParseInlines(text, linkBase)));
            pending.Clear();
        }

        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                inFence = !inFence;
                pendingKind = MarkdownBlockKind.Code;
                pendingLevel = 0;
                continue;
            }

            if (inFence)
            {
                pending.Append(pending.Length > 0 ? "\n" : string.Empty).Append(raw);
                continue;
            }

            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            if (HeadingPattern().Match(line) is { Success: true } heading)
            {
                Flush();
                bulletIndents.Clear();
                pendingKind = MarkdownBlockKind.Heading;
                pendingLevel = heading.Groups[1].Length;
                pending.Append(heading.Groups[2].Value.Trim());
                Flush();
                continue;
            }

            if (BulletPattern().Match(line) is { Success: true } bullet)
            {
                Flush();
                var indent = bullet.Groups[1].Length;
                while (bulletIndents.Count > 0 && bulletIndents[^1] > indent)
                {
                    bulletIndents.RemoveAt(bulletIndents.Count - 1);
                }

                if (bulletIndents.Count == 0 || bulletIndents[^1] < indent)
                {
                    bulletIndents.Add(indent);
                }

                pendingKind = MarkdownBlockKind.Bullet;
                pendingLevel = bulletIndents.Count - 1;
                pending.Append(bullet.Groups[2].Value.Trim());
                continue;
            }

            // A continuation line: indented under a bullet, or the next line of a paragraph.
            if (pending.Length > 0)
            {
                pending.Append(' ').Append(line.Trim());
                continue;
            }

            bulletIndents.Clear();
            pendingKind = MarkdownBlockKind.Paragraph;
            pendingLevel = 0;
            pending.Append(line.Trim());
        }

        Flush();
        return blocks;
    }

    /// <summary>Parses inline spans of one block.</summary>
    public static IReadOnlyList<MarkdownInline> ParseInlines(string text, Uri? linkBase = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var inlines = new List<MarkdownInline>();
        var plain = new StringBuilder();

        void Add(MarkdownInlineKind kind, string value, Uri? link = null)
        {
            if (plain.Length > 0)
            {
                inlines.Add(new MarkdownInline(MarkdownInlineKind.Text, plain.ToString()));
                plain.Clear();
            }

            if (value.Length > 0)
            {
                inlines.Add(new MarkdownInline(kind, value, link));
            }
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && "\\`*_[]()#-".Contains(text[i + 1], StringComparison.Ordinal))
            {
                plain.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (c == '`' && text.IndexOf('`', i + 1) is var codeEnd and > 0)
            {
                Add(MarkdownInlineKind.Code, text[(i + 1)..codeEnd]);
                i = codeEnd + 1;
                continue;
            }

            if (c == '*' && i + 1 < text.Length && text[i + 1] == '*' && text.IndexOf("**", i + 2, StringComparison.Ordinal) is var strongEnd and > 0)
            {
                Add(MarkdownInlineKind.Strong, StripMarks(text[(i + 2)..strongEnd]));
                i = strongEnd + 2;
                continue;
            }

            if (c == '*' && i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]) && text.IndexOf('*', i + 1) is var emphasisEnd and > 0)
            {
                Add(MarkdownInlineKind.Emphasis, StripMarks(text[(i + 1)..emphasisEnd]));
                i = emphasisEnd + 1;
                continue;
            }

            if (c == '[' && LinkPattern().Match(text, i) is { Success: true } link && link.Index == i)
            {
                var label = StripMarks(link.Groups[1].Value);
                var target = Resolve(link.Groups[2].Value, linkBase);
                if (target is null)
                {
                    plain.Append(label);
                }
                else
                {
                    Add(MarkdownInlineKind.Link, label, target);
                }

                i += link.Length;
                continue;
            }

            plain.Append(c);
            i++;
        }

        Add(MarkdownInlineKind.Text, string.Empty);
        return inlines;
    }

    /// <summary>Plain text of a block (for tests and screen readers).</summary>
    public static string PlainText(MarkdownBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return string.Concat(block.Inlines.Select(i => i.Text));
    }

    // Absolute http(s) links stay; relative ones ("docs/cli.md", "../x.md#a") resolve against the base; others are dropped.
    private static Uri? Resolve(string target, Uri? linkBase)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var absolute))
        {
            return absolute.Scheme is "http" or "https" ? absolute : null;
        }

        return linkBase is not null && Uri.TryCreate(linkBase, target, out var resolved) && resolved.Scheme is "http" or "https" ? resolved : null;
    }

    private static string StripMarks(string text) => text.Replace("`", string.Empty, StringComparison.Ordinal).Replace("**", string.Empty, StringComparison.Ordinal);

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^(\s*)[-*+]\s+(.*)$")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex LinkPattern();
}
