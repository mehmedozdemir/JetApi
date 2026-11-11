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

    // Written for "no environment" so it is told apart from "never chosen" (which opens the first environment).
    private const string NoEnvironment = "";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private WorkspaceSession(string root, Workspace workspace, IReadOnlyList<StorageDiagnostic> diagnostics)
    {
        Root = root;
        Workspace = workspace;
        Diagnostics = diagnostics;
        Tokens = new OAuth2TokenStore(root);
    }

    /// <summary>Raised after the model changed (edit, reload, environment switch).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a runtime variable is set or the runtime variables are cleared.</summary>
    public event EventHandler? RuntimeVariablesChanged;

    private readonly Dictionary<string, string> _runtimeVariables = new(StringComparer.Ordinal);

    /// <summary>
    /// Values taken from responses (extractions with <c>to: runtime</c>) while the workspace is open: they override every
    /// other variable and are never written to a file.
    /// </summary>
    public IReadOnlyDictionary<string, string> RuntimeVariables => _runtimeVariables;

    /// <summary>Sets a runtime variable.</summary>
    public void SetRuntimeVariable(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _runtimeVariables[name] = value;
        RuntimeVariablesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Forgets every runtime variable.</summary>
    public void ClearRuntimeVariables()
    {
        _runtimeVariables.Clear();
        RuntimeVariablesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Absolute path of the workspace directory.</summary>
    public string Root { get; }

    /// <summary>Cookies set by servers while the workspace is open (used when the cookie jar is on); never written to disk.</summary>
    public System.Net.CookieContainer Cookies { get; private set; } = new();

    /// <summary>Forgets every cookie.</summary>
    public void ClearCookies() => Cookies = new System.Net.CookieContainer();

    /// <summary>OAuth 2.0 tokens of this workspace on this machine (<c>.jetapi/tokens.json</c>, not in Git).</summary>
    public OAuth2TokenStore Tokens { get; }

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
        session.ActiveEnvironmentKey = session.ReadLocalState()?.ActiveEnvironment switch
        {
            // "No environment" was chosen on purpose.
            NoEnvironment => null,
            { } key when result.Workspace.FindEnvironment(key) is not null => key,
            _ => result.Workspace.Environments.Count > 0 ? result.Workspace.Environments[0].Key : null,
        };
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
        WriteLocalState((ReadLocalState() ?? new LocalState(null)) with { ActiveEnvironment = key ?? NoEnvironment });
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

    /// <summary>
    /// Moves a folder or request to <paramref name="newParentPath"/> of <paramref name="targetCollectionKey"/> (the same or
    /// another collection), before the sibling <paramref name="beforeKey"/> or at the end when it is <c>null</c>. The node
    /// keeps its key unless the new folder already has one like it. The new place is written before the old one is removed.
    /// </summary>
    /// <returns>The node's new path, or <c>null</c> when it was already there (nothing is written).</returns>
    public async Task<IReadOnlyList<string>?> MoveNodeAsync(
        string collectionKey, IReadOnlyList<string> path, string targetCollectionKey, IReadOnlyList<string> newParentPath, string? beforeKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(newParentPath);
        var source = RequireCollection(collectionKey);
        var node = CollectionTree.Find(source, path) ?? throw new ArgumentException("Node not found.", nameof(path));
        var sameCollection = collectionKey == targetCollectionKey;
        if (sameCollection && newParentPath.Count >= path.Count && newParentPath.Take(path.Count).SequenceEqual(path))
        {
            throw new ArgumentException("A folder cannot be moved into itself.", nameof(newParentPath));
        }

        var sameParent = sameCollection && path.SkipLast(1).SequenceEqual(newParentPath);
        var removed = CollectionTree.Remove(source, path);
        var target = sameCollection ? removed : RequireCollection(targetCollectionKey);
        var siblings = CollectionTree.ChildrenOf(target, newParentPath).ToList();
        var index = beforeKey is null ? siblings.Count : siblings.FindIndex(c => c.Key == beforeKey);
        if (index < 0)
        {
            throw new ArgumentException($"'{beforeKey}' is not in the target folder.", nameof(beforeKey));
        }

        if (sameParent && CollectionTree.ChildrenOf(source, newParentPath).ToList().FindIndex(c => c.Key == node.Key) == index)
        {
            return null;
        }

        var key = sameParent ? node.Key : NodeKey.CreateUnique(node.Name, siblings.Select(c => c.Key));
        var moved = CollectionTree.Add(target, newParentPath, node with { Key = key }, index);
        await WorkspaceStore.SaveCollectionAsync(Root, moved, cancellationToken).ConfigureAwait(false);
        if (!sameParent)
        {
            await WorkspaceStore.DeleteNodeAsync(Root, collectionKey, path).ConfigureAwait(false);
        }

        var workspace = Workspace.WithCollection(moved);
        if (!sameCollection)
        {
            await WorkspaceStore.SaveCollectionAsync(Root, removed, cancellationToken).ConfigureAwait(false);
            workspace = workspace.WithCollection(removed);
        }

        Workspace = workspace;
        OnChanged();
        return [.. newParentPath, key];
    }

    /// <summary>Moves a collection before <paramref name="beforeKey"/>, or to the end when it is <c>null</c>.</summary>
    /// <returns><c>false</c> when it was already there (nothing is written).</returns>
    public async Task<bool> MoveCollectionAsync(string collectionKey, string? beforeKey, CancellationToken cancellationToken = default)
    {
        var collection = RequireCollection(collectionKey);
        var others = Workspace.Collections.Where(c => c.Key != collectionKey).ToList();
        var index = beforeKey is null ? others.Count : others.FindIndex(c => c.Key == beforeKey);
        if (index < 0)
        {
            throw new ArgumentException($"Unknown collection '{beforeKey}'.", nameof(beforeKey));
        }

        others.Insert(index, collection);
        if (others.Select(c => c.Key).SequenceEqual(Workspace.Collections.Select(c => c.Key)))
        {
            return false;
        }

        Workspace = Workspace with { Collections = others };
        await SaveManifestAsync(cancellationToken).ConfigureAwait(false);
        OnChanged();
        return true;
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
            WriteLocalState((ReadLocalState() ?? new LocalState(null)) with { ActiveEnvironment = null });
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

    /// <summary>The request tabs that were open when this workspace was last used on this machine.</summary>
    public (IReadOnlyList<OpenTab> Tabs, int Selected) ReadOpenTabs() =>
        ReadLocalState() is { OpenTabs: { } tabs } state ? (tabs, state.SelectedTab) : ([], -1);

    /// <summary>Remembers the open request tabs (in <c>.jetapi/state.json</c>, not in Git).</summary>
    public void WriteOpenTabs(IReadOnlyList<OpenTab> tabs, int selected) =>
        WriteLocalState((ReadLocalState() ?? new LocalState(ActiveEnvironmentKey)) with { OpenTabs = tabs, SelectedTab = selected });

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

    private sealed record LocalState(string? ActiveEnvironment, IReadOnlyList<OpenTab>? OpenTabs = null, int SelectedTab = -1);
}

/// <summary>A saved request that was open in a tab.</summary>
/// <param name="Collection">Collection key.</param>
/// <param name="Path">Node keys from the collection to the request.</param>
/// <param name="Pinned">The tab was pinned (kept first, not closed by "close others" or "close all").</param>
public sealed record OpenTab(string Collection, IReadOnlyList<string> Path, bool Pinned = false);
