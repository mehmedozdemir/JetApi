using System.Text.Json;
using JetApi.Core.Model;
using JetApi.Desktop.Localization;
using JetApi.Storage;

namespace JetApi.Desktop.Services;

/// <summary>
/// An open workspace: the current model, the active environment and every edit, each written to disk
/// immediately. The in-memory model is immutable; every edit swaps in a new <see cref="Workspace"/>
/// and raises <see cref="Changed"/>.
/// </summary>
public sealed class WorkspaceSession
{
    private const string LocalStateFileName = "state.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private WorkspaceSession(string root, Workspace workspace, IReadOnlyList<StorageDiagnostic> diagnostics)
    {
        Root = root;
        Workspace = workspace;
        Diagnostics = diagnostics;
    }

    /// <summary>Raised after the model changed (edit, reload, environment switch).</summary>
    public event EventHandler? Changed;

    /// <summary>Absolute path of the workspace directory.</summary>
    public string Root { get; }

    /// <summary>The current model.</summary>
    public Workspace Workspace { get; private set; }

    /// <summary>Files that could not be loaded the last time the workspace was read.</summary>
    public IReadOnlyList<StorageDiagnostic> Diagnostics { get; private set; }

    /// <summary>Key of the active environment, or <c>null</c> for none.</summary>
    public string? ActiveEnvironmentKey { get; private set; }

    /// <summary>The active environment, if any.</summary>
    public ApiEnvironment? ActiveEnvironment => Workspace.FindEnvironment(ActiveEnvironmentKey);

    private string LocalStateDirectory => Path.Combine(Root, WorkspaceLayout.LocalStateDirectory);

    /// <summary>Opens an existing workspace.</summary>
    /// <exception cref="WorkspaceStorageException">The directory is not a valid workspace.</exception>
    public static async Task<WorkspaceSession> OpenAsync(string root, CancellationToken cancellationToken = default)
    {
        var fullRoot = Path.GetFullPath(root);
        var result = await WorkspaceStore.LoadAsync(fullRoot, cancellationToken).ConfigureAwait(false);
        var session = new WorkspaceSession(fullRoot, result.Workspace, result.Diagnostics);
        session.ActiveEnvironmentKey = session.ReadLocalState()?.ActiveEnvironment is { } key && result.Workspace.FindEnvironment(key) is not null
            ? key
            : (result.Workspace.Environments.Count > 0 ? result.Workspace.Environments[0].Key : null);
        return session;
    }

    /// <summary>
    /// Creates a workspace in <paramref name="root"/> (which must not already be one) with a
    /// <c>dev</c> environment, an example collection and a <c>.gitignore</c> for local state.
    /// </summary>
    public static async Task<WorkspaceSession> CreateAsync(string root, string name, CancellationToken cancellationToken = default)
    {
        var fullRoot = Path.GetFullPath(root);
        if (WorkspaceStore.IsWorkspace(fullRoot))
        {
            throw new WorkspaceStorageException(Loc.Format("Error_WorkspaceExists", fullRoot));
        }

        var workspace = new Workspace
        {
            Name = name,
            Environments =
            [
                new ApiEnvironment { Key = "dev", Name = "Development", Variables = [new("baseUrl", "https://httpbin.org")] },
            ],
            Collections =
            [
                new ApiCollection
                {
                    Key = "examples",
                    Name = "Examples",
                    Children =
                    [
                        new HttpRequestDefinition { Key = "get-example", Name = "Get example", Url = "{{baseUrl}}/get", QueryParameters = [new("hello", "world")] },
                        new HttpRequestDefinition
                        {
                            Key = "post-json",
                            Name = "Post JSON",
                            Method = "POST",
                            Url = "{{baseUrl}}/post",
                            Body = HttpBody.Json("{\n  \"id\": \"{{$guid}}\",\n  \"createdAt\": \"{{$isoTimestamp}}\"\n}"),
                        },
                    ],
                },
            ],
        };

        await WorkspaceStore.SaveAsync(fullRoot, workspace, cancellationToken).ConfigureAwait(false);
        EnsureGitIgnore(fullRoot);
        return await OpenAsync(fullRoot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Re-reads the workspace from disk, e.g. after <c>git pull</c>.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var result = await WorkspaceStore.LoadAsync(Root, cancellationToken).ConfigureAwait(false);
        Workspace = result.Workspace;
        Diagnostics = result.Diagnostics;
        if (Workspace.FindEnvironment(ActiveEnvironmentKey) is null)
        {
            ActiveEnvironmentKey = Workspace.Environments.Count > 0 ? Workspace.Environments[0].Key : null;
        }

        OnChanged();
    }

    /// <summary>Selects the active environment (or none) and remembers it for this machine.</summary>
    public void SetActiveEnvironment(string? key)
    {
        if (key is not null && Workspace.FindEnvironment(key) is null)
        {
            throw new ArgumentException($"Unknown environment '{key}'.", nameof(key));
        }

        ActiveEnvironmentKey = key;
        WriteLocalState(new LocalState(key));
        OnChanged();
    }

    /// <summary>Replaces the node at <paramref name="path"/> and saves the collection.</summary>
    public Task SaveNodeAsync(string collectionKey, IReadOnlyList<string> path, CollectionNode node, CancellationToken cancellationToken = default) =>
        UpdateCollectionAsync(collectionKey, collection => CollectionTree.Replace(collection, path, node), cancellationToken);

    /// <summary>Adds a node under <paramref name="parentPath"/> with a key derived from its name and saves the collection.</summary>
    /// <returns>The path of the new node.</returns>
    public async Task<IReadOnlyList<string>> AddNodeAsync(string collectionKey, IReadOnlyList<string> parentPath, CollectionNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        var collection = RequireCollection(collectionKey);
        var key = NodeKey.CreateUnique(node.Name, CollectionTree.ChildrenOf(collection, parentPath).Select(c => c.Key));
        await UpdateCollectionAsync(collectionKey, c => CollectionTree.Add(c, parentPath, node with { Key = key }), cancellationToken).ConfigureAwait(false);
        return [.. parentPath, key];
    }

    /// <summary>Deletes a folder or request.</summary>
    public async Task DeleteNodeAsync(string collectionKey, IReadOnlyList<string> path, CancellationToken cancellationToken = default)
    {
        var collection = CollectionTree.Remove(RequireCollection(collectionKey), path);
        await WorkspaceStore.DeleteNodeAsync(Root, collectionKey, path).ConfigureAwait(false);
        await WorkspaceStore.SaveCollectionAsync(Root, collection, cancellationToken).ConfigureAwait(false);
        Workspace = Workspace.WithCollection(collection);
        OnChanged();
    }

    /// <summary>Moves a node to another folder (or the collection root) of the same collection.</summary>
    /// <returns>The node's new path.</returns>
    public async Task<IReadOnlyList<string>> MoveNodeAsync(string collectionKey, IReadOnlyList<string> path, IReadOnlyList<string> newParentPath, int? index = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var collection = RequireCollection(collectionKey);
        var node = CollectionTree.Find(collection, path) ?? throw new ArgumentException("Node not found.", nameof(path));
        var sameParent = path.SkipLast(1).SequenceEqual(newParentPath);
        var key = sameParent ? node.Key : NodeKey.CreateUnique(node.Name, CollectionTree.ChildrenOf(collection, newParentPath).Select(c => c.Key));
        var moved = CollectionTree.Add(CollectionTree.Remove(collection, path), newParentPath, node with { Key = key }, index);

        if (!sameParent)
        {
            await WorkspaceStore.DeleteNodeAsync(Root, collectionKey, path).ConfigureAwait(false);
        }

        await WorkspaceStore.SaveCollectionAsync(Root, moved, cancellationToken).ConfigureAwait(false);
        Workspace = Workspace.WithCollection(moved);
        OnChanged();
        return [.. newParentPath, key];
    }

    /// <summary>Creates a collection with a key derived from <paramref name="name"/>.</summary>
    public async Task<string> AddCollectionAsync(string name, CancellationToken cancellationToken = default)
    {
        var key = NodeKey.CreateUnique(name, Workspace.Collections.Select(c => c.Key));
        var collection = new ApiCollection { Key = key, Name = name };
        await WorkspaceStore.SaveCollectionAsync(Root, collection, cancellationToken).ConfigureAwait(false);
        Workspace = Workspace.WithCollection(collection);
        await SaveManifestAsync(cancellationToken).ConfigureAwait(false);
        OnChanged();
        return key;
    }

    /// <summary>Adds a complete collection with all its folders and requests (an import); its key must be new.</summary>
    public async Task ImportCollectionAsync(ApiCollection collection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (Workspace.FindCollection(collection.Key) is not null)
        {
            throw new ArgumentException("A collection with this key already exists.", nameof(collection));
        }

        await WorkspaceStore.SaveCollectionAsync(Root, collection, cancellationToken).ConfigureAwait(false);
        Workspace = Workspace.WithCollection(collection);
        await SaveManifestAsync(cancellationToken).ConfigureAwait(false);
        OnChanged();
    }

    /// <summary>Replaces a collection's own settings (name, auth, variables, proto) and saves it.</summary>
    public async Task SaveCollectionAsync(ApiCollection collection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        RequireCollection(collection.Key);
        await WorkspaceStore.SaveCollectionAsync(Root, collection, cancellationToken).ConfigureAwait(false);
        Workspace = Workspace.WithCollection(collection);
        OnChanged();
    }

    /// <summary>Deletes a collection and all its files.</summary>
    public async Task DeleteCollectionAsync(string collectionKey, CancellationToken cancellationToken = default)
    {
        RequireCollection(collectionKey);
        await WorkspaceStore.DeleteCollectionAsync(Root, collectionKey).ConfigureAwait(false);
        Workspace = Workspace with { Collections = [.. Workspace.Collections.Where(c => c.Key != collectionKey)] };
        await SaveManifestAsync(cancellationToken).ConfigureAwait(false);
        OnChanged();
    }

    /// <summary>Creates or replaces an environment.</summary>
    public async Task SaveEnvironmentAsync(ApiEnvironment environment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var isNew = Workspace.FindEnvironment(environment.Key) is null;
        await WorkspaceStore.SaveEnvironmentAsync(Root, environment, cancellationToken).ConfigureAwait(false);
        Workspace = Workspace.WithEnvironment(environment);
        if (isNew)
        {
            await SaveManifestAsync(cancellationToken).ConfigureAwait(false);
        }

        OnChanged();
    }

    /// <summary>Creates an environment with a key derived from <paramref name="name"/>.</summary>
    public async Task<string> AddEnvironmentAsync(string name, IReadOnlyList<Variable>? variables = null, CancellationToken cancellationToken = default)
    {
        var key = NodeKey.CreateUnique(name, Workspace.Environments.Select(e => e.Key));
        await SaveEnvironmentAsync(new ApiEnvironment { Key = key, Name = name, Variables = variables ?? [] }, cancellationToken).ConfigureAwait(false);
        return key;
    }

    /// <summary>Deletes an environment; if it was active, none is active afterwards.</summary>
    public async Task DeleteEnvironmentAsync(string key, CancellationToken cancellationToken = default)
    {
        await WorkspaceStore.DeleteEnvironmentAsync(Root, key).ConfigureAwait(false);
        Workspace = Workspace with { Environments = [.. Workspace.Environments.Where(e => e.Key != key)] };
        await SaveManifestAsync(cancellationToken).ConfigureAwait(false);
        if (ActiveEnvironmentKey == key)
        {
            ActiveEnvironmentKey = null;
            WriteLocalState(new LocalState(null));
        }

        OnChanged();
    }

    /// <summary>Saves the workspace-wide global variables.</summary>
    public async Task SaveGlobalsAsync(IReadOnlyList<Variable> globals, CancellationToken cancellationToken = default)
    {
        Workspace = Workspace with { GlobalVariables = globals };
        await SaveManifestAsync(cancellationToken).ConfigureAwait(false);
        OnChanged();
    }

    private async Task UpdateCollectionAsync(string collectionKey, Func<ApiCollection, ApiCollection> update, CancellationToken cancellationToken)
    {
        var collection = update(RequireCollection(collectionKey));
        await WorkspaceStore.SaveCollectionAsync(Root, collection, cancellationToken).ConfigureAwait(false);
        Workspace = Workspace.WithCollection(collection);
        OnChanged();
    }

    // The manifest holds names, globals and the order of collections and environments.
    // Saving the whole workspace only rewrites files whose content changed.
    private Task SaveManifestAsync(CancellationToken cancellationToken) =>
        WorkspaceStore.SaveAsync(Root, Workspace, cancellationToken);

    private ApiCollection RequireCollection(string key) =>
        Workspace.FindCollection(key) ?? throw new ArgumentException($"Unknown collection '{key}'.", nameof(key));

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private LocalState? ReadLocalState()
    {
        try
        {
            var path = Path.Combine(LocalStateDirectory, LocalStateFileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<LocalState>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void WriteLocalState(LocalState state)
    {
        try
        {
            Directory.CreateDirectory(LocalStateDirectory);
            File.WriteAllText(Path.Combine(LocalStateDirectory, LocalStateFileName), JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (IOException)
        {
            // Local state is a convenience; failing to persist it must not break the app.
        }
    }

    private static void EnsureGitIgnore(string root)
    {
        var path = Path.Combine(root, ".gitignore");
        var entry = WorkspaceLayout.LocalStateDirectory + "/";
        var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        if (!existing.Split('\n').Any(line => line.Trim() == entry))
        {
            var prefix = existing.Length == 0 || existing.EndsWith('\n') ? string.Empty : "\n";
            File.AppendAllText(path, $"{prefix}# JetApi: per-machine history and UI state\n{entry}\n");
        }
    }

    private sealed record LocalState(string? ActiveEnvironment);
}
