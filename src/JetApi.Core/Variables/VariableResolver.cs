using System.Text;

namespace JetApi.Core.Variables;

/// <summary>Why a placeholder could not be resolved.</summary>
public enum VariableIssueKind
{
    /// <summary>No layer defines the variable.</summary>
    Undefined,

    /// <summary>The variable references itself, directly or indirectly.</summary>
    Cycle,
}

/// <summary>A placeholder that was left unresolved.</summary>
/// <param name="Name">The variable name.</param>
/// <param name="Kind">The reason.</param>
public sealed record VariableIssue(string Name, VariableIssueKind Kind);

/// <summary>The result of resolving a template.</summary>
/// <param name="Value">The text with all resolvable placeholders substituted. Unresolved placeholders are kept verbatim.</param>
/// <param name="Issues">Placeholders that could not be resolved, in order of first appearance, without duplicates.</param>
public sealed record ResolvedText(string Value, IReadOnlyList<VariableIssue> Issues)
{
    /// <summary>True when every placeholder was resolved.</summary>
    public bool IsComplete => Issues.Count == 0;
}

/// <summary>A resolved variable and where it came from, for UI hints.</summary>
/// <param name="Value">The fully resolved value.</param>
/// <param name="Source">The winning layer, or <c>null</c> for dynamic variables.</param>
/// <param name="IsSecret">Whether the value should be masked.</param>
public sealed record VariableLookup(string Value, VariableSource? Source, bool IsSecret);

/// <summary>A <c>{{name}}</c> placeholder found in a text.</summary>
/// <param name="Start">Index of the first <c>{</c>.</param>
/// <param name="Length">Length including both braces pairs.</param>
/// <param name="Name">The variable name, trimmed.</param>
public sealed record VariablePlaceholder(int Start, int Length, string Name)
{
    /// <summary>Index just after the closing braces.</summary>
    public int End => Start + Length;

    /// <summary>True when <paramref name="index"/> falls inside the placeholder.</summary>
    public bool Contains(int index) => index >= Start && index < End;
}

/// <summary>
/// Substitutes <c>{{name}}</c> placeholders using layered variables (global &lt; collection &lt;
/// environment &lt; runtime) and built-in dynamic variables. Values may reference other variables;
/// cycles are detected and reported instead of looping. Text that merely looks like a placeholder
/// but contains characters that are not allowed in names (for example <c>{{ a b }}</c>) is left as-is.
/// </summary>
public sealed class VariableResolver
{
    /// <summary>Maximum nesting depth of variables referencing variables.</summary>
    public const int MaxDepth = 16;

    private const string Open = "{{";
    private const string Close = "}}";

    private readonly IReadOnlyList<VariableLayer> _layersByPrecedence;
    private readonly DynamicVariables _dynamicVariables;

    /// <summary>Creates a resolver. Layer order does not matter; precedence comes from <see cref="VariableLayer.Source"/>.</summary>
    public VariableResolver(IEnumerable<VariableLayer> layers, DynamicVariables? dynamicVariables = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        _layersByPrecedence = [.. layers.OrderByDescending(layer => layer.Source)];
        _dynamicVariables = dynamicVariables ?? new DynamicVariables();
    }

    /// <summary>A resolver with no variables (dynamic variables still work).</summary>
    public static VariableResolver Empty { get; } = new([]);

    /// <summary>Resolves all placeholders in <paramref name="template"/>.</summary>
    public ResolvedText Resolve(string? template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return new ResolvedText(string.Empty, []);
        }

        var issues = new List<VariableIssue>();
        var value = ResolveCore(template, new Stack<string>(), issues);
        return new ResolvedText(value, issues);
    }

    /// <summary>Looks up a single variable by name and resolves its value.</summary>
    public VariableLookup? Lookup(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_dynamicVariables.TryEvaluate(name, out var dynamicValue))
        {
            return new VariableLookup(dynamicValue, null, false);
        }

        foreach (var layer in _layersByPrecedence)
        {
            if (layer.TryGet(name, out var variable))
            {
                var resolved = ResolveCore(variable.Value, new Stack<string>([name]), []);
                return new VariableLookup(resolved, layer.Source, variable.IsSecret);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="name"/> can be used in a <c>{{name}}</c> placeholder: ASCII letters, digits,
    /// <c>_</c>, <c>-</c>, <c>.</c> and <c>$</c>.
    /// </summary>
    public static bool IsValidVariableName(string? name) => IsValidName(name.AsSpan());

    /// <summary>Returns the distinct variable names referenced by <paramref name="template"/>, without resolving them.</summary>
    public static IReadOnlyList<string> FindReferences(string? template)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(template))
        {
            return names;
        }

        var position = 0;
        while (TryFindPlaceholder(template, position, out _, out var end, out var name))
        {
            if (!names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }

            position = end;
        }

        return names;
    }

    /// <summary>
    /// Returns every placeholder in <paramref name="text"/> with its position, in order, including repeats.
    /// Editors use this to highlight variables and to find the one under the pointer.
    /// </summary>
    public static IReadOnlyList<VariablePlaceholder> FindPlaceholders(string? text)
    {
        var placeholders = new List<VariablePlaceholder>();
        if (string.IsNullOrEmpty(text))
        {
            return placeholders;
        }

        var position = 0;
        while (TryFindPlaceholder(text, position, out var start, out var end, out var name))
        {
            placeholders.Add(new VariablePlaceholder(start, end - start, name));
            position = end;
        }

        return placeholders;
    }

    private string ResolveCore(string template, Stack<string> resolving, List<VariableIssue> issues)
    {
        var builder = new StringBuilder(template.Length);
        var position = 0;
        while (TryFindPlaceholder(template, position, out var start, out var end, out var name))
        {
            builder.Append(template, position, start - position);
            builder.Append(ResolvePlaceholder(template[start..end], name, resolving, issues));
            position = end;
        }

        builder.Append(template, position, template.Length - position);
        return builder.ToString();
    }

    private string ResolvePlaceholder(string placeholder, string name, Stack<string> resolving, List<VariableIssue> issues)
    {
        if (_dynamicVariables.TryEvaluate(name, out var dynamicValue))
        {
            return dynamicValue;
        }

        if (resolving.Contains(name) || resolving.Count >= MaxDepth)
        {
            AddIssue(issues, new VariableIssue(name, VariableIssueKind.Cycle));
            return placeholder;
        }

        foreach (var layer in _layersByPrecedence)
        {
            if (layer.TryGet(name, out var variable))
            {
                resolving.Push(name);
                var resolved = ResolveCore(variable.Value, resolving, issues);
                resolving.Pop();
                return resolved;
            }
        }

        AddIssue(issues, new VariableIssue(name, VariableIssueKind.Undefined));
        return placeholder;
    }

    private static void AddIssue(List<VariableIssue> issues, VariableIssue issue)
    {
        if (!issues.Contains(issue))
        {
            issues.Add(issue);
        }
    }

    private static bool TryFindPlaceholder(string text, int from, out int start, out int end, out string name)
    {
        var searchFrom = from;
        while (searchFrom < text.Length)
        {
            start = text.IndexOf(Open, searchFrom, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            var close = text.IndexOf(Close, start + Open.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            var candidate = text.AsSpan(start + Open.Length, close - start - Open.Length).Trim();
            if (IsValidName(candidate))
            {
                end = close + Close.Length;
                name = candidate.ToString();
                return true;
            }

            searchFrom = start + 1;
        }

        start = end = -1;
        name = string.Empty;
        return false;
    }

    private static bool IsValidName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty)
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.' or '$'))
            {
                return false;
            }
        }

        return true;
    }
}
