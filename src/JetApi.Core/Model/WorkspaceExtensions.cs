namespace JetApi.Core.Model;

/// <summary>Immutable edits of a workspace.</summary>
public static class WorkspaceExtensions
{
    /// <summary>Replaces the collection with the same key, or appends it when it is new.</summary>
    public static Workspace WithCollection(this Workspace workspace, ApiCollection collection)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(collection);

        var collections = workspace.Collections.ToList();
        var index = collections.FindIndex(c => c.Key == collection.Key);
        if (index >= 0)
        {
            collections[index] = collection;
        }
        else
        {
            collections.Add(collection);
        }

        return workspace with { Collections = collections };
    }

    /// <summary>Replaces the environment with the same key, or appends it when it is new.</summary>
    public static Workspace WithEnvironment(this Workspace workspace, ApiEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(environment);

        var environments = workspace.Environments.ToList();
        var index = environments.FindIndex(e => e.Key == environment.Key);
        if (index >= 0)
        {
            environments[index] = environment;
        }
        else
        {
            environments.Add(environment);
        }

        return workspace with { Environments = environments };
    }

    /// <summary>Finds a collection by key.</summary>
    public static ApiCollection? FindCollection(this Workspace workspace, string key)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return workspace.Collections.FirstOrDefault(c => c.Key == key);
    }

    /// <summary>Finds an environment by key.</summary>
    public static ApiEnvironment? FindEnvironment(this Workspace workspace, string? key)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return key is null ? null : workspace.Environments.FirstOrDefault(e => e.Key == key);
    }
}
