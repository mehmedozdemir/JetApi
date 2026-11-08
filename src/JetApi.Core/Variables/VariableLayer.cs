using JetApi.Core.Model;

namespace JetApi.Core.Variables;

/// <summary>Where a variable value comes from. Higher values win over lower ones.</summary>
public enum VariableSource
{
    /// <summary>Workspace-wide globals.</summary>
    Global = 0,

    /// <summary>Collection variables.</summary>
    Collection = 1,

    /// <summary>The active environment.</summary>
    Environment = 2,

    /// <summary>Values set while running (data files, scripts, runner overrides).</summary>
    Runtime = 3,
}

/// <summary>A set of variables from one <see cref="VariableSource"/>.</summary>
public sealed class VariableLayer
{
    private readonly Dictionary<string, Variable> _variables;

    /// <summary>Creates a layer; disabled variables are dropped, later duplicates win.</summary>
    public VariableLayer(VariableSource source, IEnumerable<Variable> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        Source = source;
        _variables = new Dictionary<string, Variable>(StringComparer.Ordinal);
        foreach (var variable in variables.Where(v => v.IsEnabled))
        {
            _variables[variable.Name] = variable;
        }
    }

    /// <summary>The layer's source.</summary>
    public VariableSource Source { get; }

    /// <summary>Looks up a variable by exact (case-sensitive) name.</summary>
    public bool TryGet(string name, out Variable variable) => _variables.TryGetValue(name, out variable!);
}
