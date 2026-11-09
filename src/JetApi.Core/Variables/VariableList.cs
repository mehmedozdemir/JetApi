using JetApi.Core.Model;

namespace JetApi.Core.Variables;

/// <summary>Changes to a list of variables (environment, collection or globals).</summary>
public static class VariableList
{
    /// <summary>
    /// Sets <paramref name="name"/> to <paramref name="value"/>: the variable that wins when resolving (the last enabled one,
    /// else the last one) keeps its secret flag and is enabled; without one, a new variable is added at the end.
    /// </summary>
    public static IReadOnlyList<Variable> Set(IReadOnlyList<Variable> variables, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        var list = variables.ToList();
        var index = list.FindLastIndex(v => v.Name == name && v.IsEnabled);
        if (index < 0)
        {
            index = list.FindLastIndex(v => v.Name == name);
        }

        if (index < 0)
        {
            list.Add(new Variable(name, value));
        }
        else
        {
            list[index] = list[index] with { Value = value, IsEnabled = true };
        }

        return list;
    }
}
