using JetApi.Core.Model;
using JetApi.Core.Variables;

namespace JetApi.Desktop.ViewModels;

/// <summary>A place a variable can be saved: the active environment, the collection or the globals.</summary>
/// <param name="Source">The layer.</param>
/// <param name="Label">"Environment: Development", "Collection: User Service", "Global".</param>
public sealed record VariableScopeOption(VariableSource Source, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>A variable offered while typing <c>{{</c> or choosing a name.</summary>
/// <param name="Name">The name, e.g. <c>baseUrl</c> or <c>$guid</c>.</param>
/// <param name="Value">The resolved value (masked when secret); empty for dynamic variables.</param>
/// <param name="Source">"Environment: Development", "Global (whole workspace)", "dynamic variable".</param>
/// <param name="IsDynamic">A built-in dynamic variable.</param>
public sealed record VariableSuggestion(string Name, string Value, string Source, bool IsDynamic)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>Reads and writes the variables a request can see, for editing them from the request editors.</summary>
public interface IVariableStore
{
    /// <summary>Names of every enabled environment, collection and global variable the request can see.</summary>
    IReadOnlyList<string> Names();

    /// <summary>Where variables can be saved; the environment only when one is active.</summary>
    IReadOnlyList<VariableScopeOption> Scopes { get; }

    /// <summary>The variable as stored (value unresolved), or <c>null</c>.</summary>
    Variable? Find(VariableSource scope, string name);

    /// <summary>Sets a variable and saves it.</summary>
    /// <returns>An error message, or <c>null</c> on success.</returns>
    Task<string?> SaveAsync(VariableSource scope, string name, string value);
}

/// <summary>
/// The variables a request tab can see, for highlighting and hover hints in its editors.
/// A tab replaces its context when the active environment or the workspace changes, so editors
/// only need to redraw when the instance changes.
/// </summary>
public sealed class VariableContext
{
    private readonly Func<string, VariableHint> _describe;
    private readonly Dictionary<string, bool> _defined = new(StringComparer.Ordinal);

    /// <summary>Creates a context from a function that describes one variable by name.</summary>
    /// <param name="describe">Describes one variable.</param>
    /// <param name="store">Saves edits; <c>null</c> for read-only hints.</param>
    public VariableContext(Func<string, VariableHint> describe, IVariableStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(describe);
        _describe = describe;
        Store = store;
    }

    /// <summary>Saves edits made in the hover card; <c>null</c> when variables cannot be edited here.</summary>
    public IVariableStore? Store { get; }

    /// <summary>Describes a variable: its value (masked when secret) and where it comes from.</summary>
    public VariableHint Describe(string name) => _describe(name);

    /// <summary>Whether the variable resolves. Cached, because editors ask on every redraw.</summary>
    public bool IsDefined(string name)
    {
        if (!_defined.TryGetValue(name, out var defined))
        {
            defined = _describe(name).IsDefined;
            _defined[name] = defined;
        }

        return defined;
    }

    /// <summary>
    /// Every variable the request can see, sorted by name, with its value and where it comes from; the built-in
    /// dynamic variables follow when <paramref name="includeDynamic"/> is set.
    /// </summary>
    public IReadOnlyList<VariableSuggestion> Suggestions(bool includeDynamic = true)
    {
        var list = new List<VariableSuggestion>();
        foreach (var name in (Store?.Names() ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase))
        {
            var hint = Describe(name);
            var source = hint.Source is { } layer && Store!.Scopes.FirstOrDefault(s => s.Source == layer) is { } scope ? scope.Label : hint.Description;
            list.Add(new VariableSuggestion(name, hint.Value, source, false));
        }

        if (includeDynamic)
        {
            list.AddRange(DynamicVariables.SupportedNames.Select(n => new VariableSuggestion("$" + n, string.Empty, Describe("$" + n).Description, true)));
        }

        return list;
    }

    /// <summary>Describes the placeholder that contains <paramref name="index"/>, or returns <c>null</c>.</summary>
    public VariableHint? DescribeAt(string? text, int index)
    {
        var placeholder = VariableResolver.FindPlaceholders(text).FirstOrDefault(p => p.Contains(index));
        return placeholder is null ? null : Describe(placeholder.Name);
    }
}
