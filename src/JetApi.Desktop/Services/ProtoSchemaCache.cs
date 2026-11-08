using System.Collections.Concurrent;
using JetApi.Core.Model;
using JetApi.Grpc.Schema;

namespace JetApi.Desktop.Services;

/// <summary>
/// Loads each collection's proto files once and reloads them automatically when any <c>.proto</c>
/// file under the import paths changes, so edits to proto files are picked up without a restart.
/// </summary>
public sealed class ProtoSchemaCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Returns the schema of <paramref name="collection"/>, reloading it if its proto files changed.</summary>
    public ProtoLoadResult Get(string workspaceRoot, ApiCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var cacheKey = workspaceRoot + "|" + collection.Key;
        var stamp = ComputeStamp(workspaceRoot, collection.Proto);
        if (_entries.TryGetValue(cacheKey, out var entry) && entry.Settings == collection.Proto && entry.Stamp == stamp)
        {
            return entry.Result;
        }

        var result = ProtoSchemaLoader.Load(collection.Proto, workspaceRoot);
        _entries[cacheKey] = new Entry(collection.Proto, stamp, result);
        return result;
    }

    /// <summary>Forgets every cached schema.</summary>
    public void Clear() => _entries.Clear();

    private static string ComputeStamp(string workspaceRoot, ProtoSettings settings)
    {
        var directories = settings.ImportPaths.Count == 0
            ? [workspaceRoot]
            : settings.ImportPaths.Select(path => Path.GetFullPath(path, workspaceRoot));
        long latest = 0;
        var count = 0;
        foreach (var directory in directories.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.proto", SearchOption.AllDirectories))
            {
                latest = Math.Max(latest, File.GetLastWriteTimeUtc(file).Ticks);
                count++;
            }
        }

        return $"{count}:{latest}";
    }

    private sealed record Entry(ProtoSettings Settings, string Stamp, ProtoLoadResult Result);
}
