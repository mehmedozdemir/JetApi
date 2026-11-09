using JetApi.Core.Model;
using JetApi.Core.Variables;

namespace JetApi.Desktop.Services;

/// <summary>Sets one variable in the environment, collection or global variables of a workspace and saves it.</summary>
public static class VariableWriter
{
    /// <summary>The raw (unresolved) value of <paramref name="name"/> in <paramref name="scope"/>, or <c>null</c>.</summary>
    public static Variable? Find(WorkspaceSession session, string? collectionKey, VariableSource scope, string name)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Variables(session, collectionKey, scope)?.LastOrDefault(v => v.IsEnabled && v.Name == name)
            ?? Variables(session, collectionKey, scope)?.LastOrDefault(v => v.Name == name);
    }

    /// <summary>
    /// Sets <paramref name="name"/> to <paramref name="value"/> in <paramref name="scope"/>: an existing variable keeps
    /// its secret flag and is enabled; otherwise a new one is added at the end.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no active environment, or the collection does not exist.</exception>
    public static async Task SetAsync(WorkspaceSession session, string? collectionKey, VariableSource scope, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        switch (scope)
        {
            case VariableSource.Environment:
                var environment = session.ActiveEnvironment ?? throw new InvalidOperationException("No active environment.");
                await session.SaveEnvironmentAsync(environment with { Variables = With(environment.Variables, name, value) }).ConfigureAwait(true);
                break;
            case VariableSource.Collection:
                var collection = (collectionKey is null ? null : session.Workspace.FindCollection(collectionKey)) ?? throw new InvalidOperationException("Unknown collection.");
                await session.SaveCollectionAsync(collection with { Variables = With(collection.Variables, name, value) }).ConfigureAwait(true);
                break;
            case VariableSource.Global:
                await session.SaveGlobalsAsync(With(session.Workspace.GlobalVariables, name, value)).ConfigureAwait(true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Only environment, collection and global variables can be set.");
        }
    }

    private static IReadOnlyList<Variable>? Variables(WorkspaceSession session, string? collectionKey, VariableSource scope) => scope switch
    {
        VariableSource.Environment => session.ActiveEnvironment?.Variables,
        VariableSource.Collection => collectionKey is null ? null : session.Workspace.FindCollection(collectionKey)?.Variables,
        VariableSource.Global => session.Workspace.GlobalVariables,
        _ => null,
    };

    private static List<Variable> With(IReadOnlyList<Variable> variables, string name, string value)
    {
        var list = variables.ToList();

        // The enabled one wins when resolving, so that is the one to change.
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
