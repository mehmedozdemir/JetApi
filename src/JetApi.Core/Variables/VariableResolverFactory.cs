using JetApi.Core.Model;

namespace JetApi.Core.Variables;

/// <summary>Builds a <see cref="VariableResolver"/> for a request in a workspace.</summary>
public static class VariableResolverFactory
{
    /// <summary>Creates a resolver from the workspace globals, collection variables, active environment and runtime values.</summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="collection">The collection the request belongs to, if any.</param>
    /// <param name="environment">The active environment, if any.</param>
    /// <param name="runtimeVariables">Values that override everything, e.g. a data-file row.</param>
    /// <param name="dynamicVariables">Dynamic variable provider; defaults to the system clock.</param>
    public static VariableResolver Create(
        Workspace workspace,
        ApiCollection? collection,
        ApiEnvironment? environment,
        IEnumerable<Variable>? runtimeVariables = null,
        DynamicVariables? dynamicVariables = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var layers = new List<VariableLayer> { new(VariableSource.Global, workspace.GlobalVariables) };
        if (collection is not null)
        {
            layers.Add(new VariableLayer(VariableSource.Collection, collection.Variables));
        }

        if (environment is not null)
        {
            layers.Add(new VariableLayer(VariableSource.Environment, environment.Variables));
        }

        if (runtimeVariables is not null)
        {
            layers.Add(new VariableLayer(VariableSource.Runtime, runtimeVariables));
        }

        return new VariableResolver(layers, dynamicVariables);
    }
}
