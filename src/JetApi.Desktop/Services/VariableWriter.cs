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
                await session.SaveEnvironmentAsync(environment with { Variables = VariableList.Set(environment.Variables, name, value) }).ConfigureAwait(true);
                break;
            case VariableSource.Collection:
                var collection = (collectionKey is null ? null : session.Workspace.FindCollection(collectionKey)) ?? throw new InvalidOperationException("Unknown collection.");
                await session.SaveCollectionAsync(collection with { Variables = VariableList.Set(collection.Variables, name, value) }).ConfigureAwait(true);
                break;
            case VariableSource.Global:
                await session.SaveGlobalsAsync(VariableList.Set(session.Workspace.GlobalVariables, name, value)).ConfigureAwait(true);
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

    /// <summary>
    /// Stores an extracted value where the extraction says (runtime, environment, collection or global).
    /// </summary>
    /// <returns>Why it could not, or <c>null</c>.</returns>
    public static async Task<string?> StoreExtractionAsync(WorkspaceSession session, string collectionKey, Extraction extraction, string value)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(extraction);
        if (!VariableResolver.IsValidVariableName(extraction.Variable))
        {
            return Localization.Loc.Get(extraction.Variable.Length == 0 ? "Var_NameRequired" : "Var_NameInvalid");
        }

        if (extraction.To == ExtractionTarget.Runtime)
        {
            session.SetRuntimeVariable(extraction.Variable, value);
            return null;
        }

        if (extraction.To == ExtractionTarget.Environment && session.ActiveEnvironment is null)
        {
            return Localization.Loc.Get("Tests_NoEnvironment");
        }

        var layer = extraction.To switch
        {
            ExtractionTarget.Environment => VariableSource.Environment,
            ExtractionTarget.Collection => VariableSource.Collection,
            _ => VariableSource.Global,
        };
        try
        {
            await SetAsync(session, collectionKey, layer, extraction.Variable, value).ConfigureAwait(true);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JetApi.Storage.WorkspaceStorageException or InvalidOperationException)
        {
            return Localization.Loc.Format("Error_Save", exception.Message);
        }
    }
}
