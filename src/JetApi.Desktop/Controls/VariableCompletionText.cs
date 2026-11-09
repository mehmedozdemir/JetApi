using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Controls;

/// <summary>A started placeholder before the caret: <c>{{</c> followed by the part of a name typed so far.</summary>
/// <param name="Start">Index just after <c>{{</c>.</param>
/// <param name="Prefix">The name typed so far (may be empty).</param>
internal sealed record CompletionPrefix(int Start, string Prefix);

/// <summary>An edit that inserts a chosen variable: replace <see cref="Length"/> characters at <see cref="Start"/>.</summary>
/// <param name="Start">Where the replaced text starts.</param>
/// <param name="Length">How many characters to replace.</param>
/// <param name="Insert">The text to insert.</param>
/// <param name="Caret">Where the caret goes afterwards (after the closing braces).</param>
internal sealed record CompletionEdit(int Start, int Length, string Insert, int Caret);

/// <summary>The text rules of <c>{{</c> completion, kept apart from the UI so they can be tested.</summary>
internal static class VariableCompletionText
{
    /// <summary>Finds a started placeholder that ends at <paramref name="caret"/>, e.g. <c>{{bas|</c>.</summary>
    public static CompletionPrefix? FindPrefix(string? text, int caret)
    {
        if (string.IsNullOrEmpty(text) || caret < 2 || caret > text.Length)
        {
            return null;
        }

        var start = caret;
        while (start > 0 && IsNameChar(text[start - 1]))
        {
            start--;
        }

        if (start < 2 || text[start - 1] != '{' || text[start - 2] != '{')
        {
            return null;
        }

        // "{{{" is not a placeholder start.
        return start >= 3 && text[start - 3] == '{' ? null : new CompletionPrefix(start, text[start..caret]);
    }

    /// <summary>
    /// The edit that puts <paramref name="name"/> into the started placeholder: the rest of a name after the caret is
    /// replaced too, and <c>}}</c> is added unless it already follows.
    /// </summary>
    public static CompletionEdit Apply(string text, CompletionPrefix prefix, string name)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(name);
        var end = prefix.Start;
        while (end < text.Length && IsNameChar(text[end]))
        {
            end++;
        }

        var closed = text.AsSpan(end).StartsWith("}}", StringComparison.Ordinal);
        var insert = closed ? name : name + "}}";
        return new CompletionEdit(prefix.Start, end - prefix.Start, insert, prefix.Start + name.Length + 2);
    }

    /// <summary>Suggestions whose name contains <paramref name="prefix"/>; names that start with it come first.</summary>
    public static IReadOnlyList<VariableSuggestion> Filter(IReadOnlyList<VariableSuggestion> all, string prefix)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(prefix);
        return
        [
            .. all.Where(s => s.Name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(s => s.IsDynamic ? 1 : 0),
        ];
    }

    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '$';
}
