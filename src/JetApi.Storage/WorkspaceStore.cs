using System.Text;
using JetApi.Core.Model;
using JetApi.Storage.Dto;
using JetApi.Storage.Yaml;
using YamlDotNet.Core;

namespace JetApi.Storage;

/// <summary>The result of loading a workspace.</summary>
/// <param name="Workspace">Everything that could be loaded.</param>
/// <param name="Diagnostics">Files that were skipped and why. Empty when the workspace loaded cleanly.</param>
public sealed record WorkspaceLoadResult(Workspace Workspace, IReadOnlyList<StorageDiagnostic> Diagnostics);

/// <summary>
/// Reads and writes a workspace as a directory of small YAML files (see docs/file-format.md),
/// designed to be committed to Git: one file per request, stable key order, defaults omitted,
/// LF line endings, and files are rewritten only when their content changes.
/// </summary>
/// <remarks>
/// <see cref="SaveAsync"/> never deletes files. Removing a collection, folder, request or
/// environment is an explicit operation, so files this version could not read (for example,
/// written by a newer JetApi) are never lost by accident.
/// </remarks>
public static class WorkspaceStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Returns true when <paramref name="rootDirectory"/> contains a workspace manifest.</summary>
    public static bool IsWorkspace(string rootDirectory) =>
        File.Exists(Path.Combine(rootDirectory, WorkspaceLayout.WorkspaceFileName));

    /// <summary>Loads the workspace in <paramref name="rootDirectory"/>.</summary>
    /// <exception cref="WorkspaceStorageException">The directory has no valid workspace manifest.</exception>
    public static async Task<WorkspaceLoadResult> LoadAsync(string rootDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var context = new LoadContext(Path.GetFullPath(rootDirectory), cancellationToken);
        var manifestPath = Path.Combine(context.Root, WorkspaceLayout.WorkspaceFileName);
        if (!File.Exists(manifestPath))
        {
            throw new WorkspaceStorageException(Messages.Format("Storage_NotAWorkspace", context.Root, WorkspaceLayout.WorkspaceFileName));
        }

        var manifest = await context.ReadAsync<WorkspaceFile>(manifestPath).ConfigureAwait(false);
        if (manifest is null)
        {
            var diagnostic = context.Diagnostics[0];
            throw new WorkspaceStorageException($"{diagnostic.FilePath}: {diagnostic.Message}");
        }

        var workspace = new Workspace
        {
            Name = manifest.Name!,
            Description = manifest.Description,
            GlobalVariables = DtoMapper.FromDto(manifest.Globals, "globals"),
            Environments = await LoadEnvironmentsAsync(context, manifest.Environments).ConfigureAwait(false),
            Collections = await LoadCollectionsAsync(context, manifest.Collections).ConfigureAwait(false),
        };

        return new WorkspaceLoadResult(workspace, [.. context.Diagnostics]);
    }

    /// <summary>Writes the whole workspace to <paramref name="rootDirectory"/>. Unchanged files are not touched; nothing is deleted.</summary>
    /// <exception cref="WorkspaceStorageException">A key is not a safe file name or two siblings share a key.</exception>
    public static async Task SaveAsync(string rootDirectory, Workspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(workspace);

        ValidateKeys(workspace);
        var root = Path.GetFullPath(rootDirectory);

        var manifest = new WorkspaceFile
        {
            SchemaVersion = WorkspaceLayout.SchemaVersion,
            Name = workspace.Name,
            Description = workspace.Description,
            Globals = DtoMapper.ToDto(workspace.GlobalVariables),
            Collections = workspace.Collections.Count == 0 ? null : [.. workspace.Collections.Select(c => c.Key)],
            Environments = workspace.Environments.Count == 0 ? null : [.. workspace.Environments.Select(e => e.Key)],
        };
        await WriteAsync(Path.Combine(root, WorkspaceLayout.WorkspaceFileName), manifest, cancellationToken).ConfigureAwait(false);

        foreach (var environment in workspace.Environments)
        {
            await SaveEnvironmentAsync(root, environment, cancellationToken).ConfigureAwait(false);
        }

        foreach (var collection in workspace.Collections)
        {
            await SaveCollectionAsync(root, collection, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Writes a single environment file.</summary>
    public static async Task SaveEnvironmentAsync(string rootDirectory, ApiEnvironment environment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        EnsureSafeSegment(environment.Key, Messages.Get("Storage_KindEnvironment"));

        var file = new EnvironmentFile
        {
            SchemaVersion = WorkspaceLayout.SchemaVersion,
            Name = environment.Name,
            Variables = DtoMapper.ToDto(environment.Variables),
        };
        var path = Path.Combine(rootDirectory, WorkspaceLayout.EnvironmentsDirectory, environment.Key + WorkspaceLayout.EnvironmentSuffix);
        await WriteAsync(path, file, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a collection directory: its manifest and every folder and request in it.</summary>
    public static async Task SaveCollectionAsync(string rootDirectory, ApiCollection collection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        EnsureSafeSegment(collection.Key, Messages.Get("Storage_KindCollection"));
        EnsureUniqueSiblings(collection.Children, Messages.Format("Storage_LocationCollection", collection.Key));

        var directory = Path.Combine(rootDirectory, WorkspaceLayout.CollectionsDirectory, collection.Key);
        var file = new CollectionFile
        {
            SchemaVersion = WorkspaceLayout.SchemaVersion,
            Name = collection.Name,
            Description = collection.Description,
            Auth = DtoMapper.ToDto(collection.Auth, AuthConfig.None),
            Variables = DtoMapper.ToDto(collection.Variables),
            Proto = DtoMapper.ToDto(collection.Proto),
            Scripts = DtoMapper.ToDto(collection.Scripts),
            Order = OrderOf(collection.Children),
        };
        await WriteAsync(Path.Combine(directory, WorkspaceLayout.CollectionFileName), file, cancellationToken).ConfigureAwait(false);
        await SaveChildrenAsync(directory, collection.Children, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes an environment file.</summary>
    public static Task DeleteEnvironmentAsync(string rootDirectory, string environmentKey)
    {
        EnsureSafeSegment(environmentKey, Messages.Get("Storage_KindEnvironment"));
        File.Delete(Path.Combine(rootDirectory, WorkspaceLayout.EnvironmentsDirectory, environmentKey + WorkspaceLayout.EnvironmentSuffix));
        return Task.CompletedTask;
    }

    /// <summary>Deletes a collection directory and everything in it.</summary>
    public static Task DeleteCollectionAsync(string rootDirectory, string collectionKey)
    {
        EnsureSafeSegment(collectionKey, Messages.Get("Storage_KindCollection"));
        var directory = Path.Combine(rootDirectory, WorkspaceLayout.CollectionsDirectory, collectionKey);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>Deletes a folder (recursively) or request inside a collection.</summary>
    /// <param name="rootDirectory">Workspace root.</param>
    /// <param name="collectionKey">Owning collection.</param>
    /// <param name="nodePath">Keys from the collection root to the node, e.g. <c>["admin", "delete-user"]</c>.</param>
    public static Task DeleteNodeAsync(string rootDirectory, string collectionKey, IReadOnlyList<string> nodePath)
    {
        ArgumentNullException.ThrowIfNull(nodePath);
        if (nodePath.Count == 0)
        {
            throw new ArgumentException("The node path must not be empty.", nameof(nodePath));
        }

        EnsureSafeSegment(collectionKey, Messages.Get("Storage_KindCollection"));
        foreach (var segment in nodePath)
        {
            EnsureSafeSegment(segment, Messages.Get("Storage_KindNode"));
        }

        var parent = Path.Combine([rootDirectory, WorkspaceLayout.CollectionsDirectory, collectionKey, .. nodePath.SkipLast(1)]);
        var key = nodePath[^1];
        var folder = Path.Combine(parent, key);
        if (File.Exists(Path.Combine(folder, WorkspaceLayout.FolderFileName)))
        {
            Directory.Delete(folder, recursive: true);
        }

        File.Delete(Path.Combine(parent, key + WorkspaceLayout.HttpRequestSuffix));
        File.Delete(Path.Combine(parent, key + WorkspaceLayout.GrpcRequestSuffix));
        return Task.CompletedTask;
    }

    // ---- Loading -------------------------------------------------------------------------

    private static async Task<IReadOnlyList<ApiEnvironment>> LoadEnvironmentsAsync(LoadContext context, List<string>? order)
    {
        var directory = Path.Combine(context.Root, WorkspaceLayout.EnvironmentsDirectory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var environments = new List<ApiEnvironment>();
        foreach (var path in Directory.EnumerateFiles(directory, "*" + WorkspaceLayout.EnvironmentSuffix))
        {
            var file = await context.ReadAsync<EnvironmentFile>(path).ConfigureAwait(false);
            if (file is not null && context.TryMap(path, () => new ApiEnvironment
            {
                Key = Path.GetFileName(path)[..^WorkspaceLayout.EnvironmentSuffix.Length],
                Name = file.Name!,
                Variables = DtoMapper.FromDto(file.Variables, "variables"),
            }, out var environment))
            {
                environments.Add(environment);
            }
        }

        return ApplyOrder(environments, e => e.Key, order);
    }

    private static async Task<IReadOnlyList<ApiCollection>> LoadCollectionsAsync(LoadContext context, List<string>? order)
    {
        var directory = Path.Combine(context.Root, WorkspaceLayout.CollectionsDirectory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var collections = new List<ApiCollection>();
        foreach (var collectionDirectory in Directory.EnumerateDirectories(directory))
        {
            var manifestPath = Path.Combine(collectionDirectory, WorkspaceLayout.CollectionFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var file = await context.ReadAsync<CollectionFile>(manifestPath).ConfigureAwait(false);
            if (file is null)
            {
                continue;
            }

            var children = await LoadChildrenAsync(context, collectionDirectory, file.Order).ConfigureAwait(false);
            if (context.TryMap(manifestPath, () => new ApiCollection
            {
                Key = Path.GetFileName(collectionDirectory),
                Name = file.Name!,
                Description = file.Description,
                Auth = DtoMapper.FromDto(file.Auth, AuthConfig.None),
                Variables = DtoMapper.FromDto(file.Variables, "variables"),
                Proto = DtoMapper.FromDto(file.Proto),
                Scripts = DtoMapper.FromDto(file.Scripts),
                Children = children,
            }, out var collection))
            {
                collections.Add(collection);
            }
        }

        return ApplyOrder(collections, c => c.Key, order);
    }

    private static async Task<IReadOnlyList<CollectionNode>> LoadChildrenAsync(LoadContext context, string directory, List<string>? order)
    {
        var children = new List<CollectionNode>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folderDirectory in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(folderDirectory, WorkspaceLayout.FolderFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var file = await context.ReadAsync<FolderFile>(manifestPath).ConfigureAwait(false);
            if (file is null)
            {
                continue;
            }

            var key = Path.GetFileName(folderDirectory);
            var folderChildren = await LoadChildrenAsync(context, folderDirectory, file.Order).ConfigureAwait(false);
            if (context.TryMap(manifestPath, () => new Folder
            {
                Key = key,
                Name = file.Name!,
                Description = file.Description,
                Auth = DtoMapper.FromDto(file.Auth, AuthConfig.Inherit),
                Scripts = DtoMapper.FromDto(file.Scripts),
                Children = folderChildren,
            }, out var folder) && context.TryClaimKey(keys, key, manifestPath))
            {
                children.Add(folder);
            }
        }

        foreach (var path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
        {
            var request = await LoadRequestAsync(context, path).ConfigureAwait(false);
            if (request is not null && context.TryClaimKey(keys, request.Key, path))
            {
                children.Add(request);
            }
        }

        return ApplyOrder(children, c => c.Key, order);
    }

    private static async Task<RequestNode?> LoadRequestAsync(LoadContext context, string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(WorkspaceLayout.HttpRequestSuffix, StringComparison.Ordinal))
        {
            var file = await context.ReadAsync<HttpRequestFile>(path).ConfigureAwait(false);
            var key = fileName[..^WorkspaceLayout.HttpRequestSuffix.Length];
            return file is not null && context.TryMap(path, () => DtoMapper.FromDto(file, key), out var request) ? request : null;
        }

        if (fileName.EndsWith(WorkspaceLayout.GrpcRequestSuffix, StringComparison.Ordinal))
        {
            var file = await context.ReadAsync<GrpcRequestFile>(path).ConfigureAwait(false);
            var key = fileName[..^WorkspaceLayout.GrpcRequestSuffix.Length];
            return file is not null && context.TryMap(path, () => DtoMapper.FromDto(file, key), out var request) ? request : null;
        }

        return null;
    }

    /// <summary>Items listed in <paramref name="order"/> come first, in that order; the rest follow sorted by key.</summary>
    private static List<T> ApplyOrder<T>(List<T> items, Func<T, string> keyOf, List<string>? order)
    {
        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in order ?? [])
        {
            position.TryAdd(key, position.Count);
        }

        return
        [
            .. items
                .OrderBy(item => position.TryGetValue(keyOf(item), out var index) ? index : int.MaxValue)
                .ThenBy(keyOf, StringComparer.Ordinal),
        ];
    }

    // ---- Saving --------------------------------------------------------------------------

    private static async Task SaveChildrenAsync(string directory, IReadOnlyList<CollectionNode> children, CancellationToken cancellationToken)
    {
        foreach (var child in children)
        {
            switch (child)
            {
                case Folder folder:
                    var folderDirectory = Path.Combine(directory, folder.Key);
                    var file = new FolderFile
                    {
                        SchemaVersion = WorkspaceLayout.SchemaVersion,
                        Name = folder.Name,
                        Description = folder.Description,
                        Auth = DtoMapper.ToDto(folder.Auth, AuthConfig.Inherit),
                        Scripts = DtoMapper.ToDto(folder.Scripts),
                        Order = OrderOf(folder.Children),
                    };
                    await WriteAsync(Path.Combine(folderDirectory, WorkspaceLayout.FolderFileName), file, cancellationToken).ConfigureAwait(false);
                    await SaveChildrenAsync(folderDirectory, folder.Children, cancellationToken).ConfigureAwait(false);
                    break;
                case HttpRequestDefinition http:
                    await WriteAsync(Path.Combine(directory, http.Key + WorkspaceLayout.HttpRequestSuffix), DtoMapper.ToDto(http), cancellationToken).ConfigureAwait(false);
                    break;
                case GrpcRequestDefinition grpc:
                    await WriteAsync(Path.Combine(directory, grpc.Key + WorkspaceLayout.GrpcRequestSuffix), DtoMapper.ToDto(grpc), cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported node type {child.GetType().Name}.");
            }
        }
    }

    private static List<string>? OrderOf(IReadOnlyList<CollectionNode> children) =>
        children.Count == 0 ? null : [.. children.Select(c => c.Key)];

    private static async Task WriteAsync<T>(string path, T content, CancellationToken cancellationToken)
    {
        var text = YamlConverter.Serialize(content);
        if (File.Exists(path) && string.Equals(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), text, StringComparison.Ordinal))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temporary file first so a crash never leaves a half-written file behind.
        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, text, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void ValidateKeys(Workspace workspace)
    {
        EnsureUnique(workspace.Collections.Select(c => c.Key), Messages.Get("Storage_LocationCollections"));
        EnsureUnique(workspace.Environments.Select(e => e.Key), Messages.Get("Storage_LocationEnvironments"));
    }

    private static void EnsureUniqueSiblings(IReadOnlyList<CollectionNode> children, string location)
    {
        EnsureUnique(children.Select(c => c.Key), location);
        foreach (var folder in children.OfType<Folder>())
        {
            EnsureUniqueSiblings(folder.Children, Messages.Format("Storage_LocationFolder", folder.Key));
        }

        foreach (var child in children)
        {
            EnsureSafeSegment(child.Key, Messages.Get("Storage_KindNode"));
        }
    }

    // Keys must be unique ignoring case because Windows and macOS file systems are case-insensitive.
    private static void EnsureUnique(IEnumerable<string> keys, string location)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (!seen.Add(key))
            {
                throw new WorkspaceStorageException(Messages.Format("Storage_DuplicateKey", key, location));
            }
        }
    }

    private static void EnsureSafeSegment(string key, string kind)
    {
        var isSafe = !string.IsNullOrWhiteSpace(key)
            && key is not ("." or "..")
            && key.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && key.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) < 0
            && !key.EndsWith('.')
            && !key.EndsWith(' ')
            && !WindowsReservedNames.Contains(key);
        if (!isSafe)
        {
            throw new WorkspaceStorageException(Messages.Format("Storage_UnsafeKey", kind, key));
        }
    }

    private sealed class LoadContext(string root, CancellationToken cancellationToken)
    {
        private readonly List<StorageDiagnostic> _diagnostics = [];

        public string Root { get; } = root;

        public List<StorageDiagnostic> Diagnostics => _diagnostics;

        public async Task<T?> ReadAsync<T>(string path)
            where T : class
        {
            try
            {
                var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                var file = YamlConverter.Deserialize<T>(text);
                if (file is null)
                {
                    Report(path, Messages.Get("Storage_EmptyFile"));
                    return null;
                }

                var schemaVersion = (int)typeof(T).GetProperty("SchemaVersion")!.GetValue(file)!;
                DtoMapper.EnsureSchemaVersion(schemaVersion);
                var name = (string?)typeof(T).GetProperty("Name")!.GetValue(file);
                DtoMapper.Required(name, "name");
                return file;
            }
            catch (YamlException exception)
            {
                Report(path, Messages.Format("Storage_InvalidYaml", exception.Start.Line, exception.Start.Column, (exception.InnerException ?? exception).Message));
            }
            catch (InvalidDataException exception)
            {
                Report(path, exception.Message);
            }
            catch (IOException exception)
            {
                Report(path, Messages.Format("Storage_ReadFailed", exception.Message));
            }
            catch (UnauthorizedAccessException exception)
            {
                Report(path, Messages.Format("Storage_ReadFailed", exception.Message));
            }

            return null;
        }

        public bool TryMap<T>(string path, Func<T> map, out T result)
        {
            try
            {
                result = map();
                return true;
            }
            catch (InvalidDataException exception)
            {
                Report(path, exception.Message);
                result = default!;
                return false;
            }
        }

        public bool TryClaimKey(HashSet<string> keys, string key, string path)
        {
            if (keys.Add(key))
            {
                return true;
            }

            Report(path, Messages.Format("Storage_DuplicateKeySkipped", key));
            return false;
        }

        private void Report(string path, string message) =>
            _diagnostics.Add(new StorageDiagnostic(Path.GetRelativePath(Root, path).Replace('\\', '/'), message));
    }
}
