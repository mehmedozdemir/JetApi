using JetApi.Core.Variables;

namespace JetApi.Desktop.ViewModels;

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
    public VariableContext(Func<string, VariableHint> describe)
    {
        ArgumentNullException.ThrowIfNull(describe);
        _describe = describe;
    }

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

    /// <summary>Describes the placeholder that contains <paramref name="index"/>, or returns <c>null</c>.</summary>
    public VariableHint? DescribeAt(string? text, int index)
    {
        var placeholder = VariableResolver.FindPlaceholders(text).FirstOrDefault(p => p.Contains(index));
        return placeholder is null ? null : Describe(placeholder.Name);
    }
}
