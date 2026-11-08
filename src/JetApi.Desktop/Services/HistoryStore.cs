using System.Text.Json;
using JetApi.Core.Model;
using JetApi.Storage;

namespace JetApi.Desktop.Services;

/// <summary>A request that was sent, as it was at that moment.</summary>
/// <param name="At">When it was sent.</param>
/// <param name="IsGrpc">True for gRPC.</param>
/// <param name="Name">Request name.</param>
/// <param name="Summary">Method and URL, or service/method.</param>
/// <param name="Status">Status shown in the list, e.g. <c>200</c>, <c>NotFound</c>, <c>Failed</c>.</param>
/// <param name="DurationMs">Total duration.</param>
/// <param name="CollectionKey">Collection the request belonged to.</param>
/// <param name="Path">Path of the saved request, if it was saved.</param>
/// <param name="Yaml">The request definition (unresolved, as in its file).</param>
public sealed record HistoryEntry(
    DateTimeOffset At,
    bool IsGrpc,
    string Name,
    string Summary,
    string Status,
    long DurationMs,
    string? CollectionKey,
    IReadOnlyList<string>? Path,
    string Yaml)
{
    /// <summary>Restores the request definition.</summary>
    public RequestNode ToRequest() => RequestFiles.Deserialize(Yaml, (Path is { Count: > 0 } p ? p[^1] : "history"), IsGrpc);
}

/// <summary>
/// Per-machine request history in <c>.jetapi/history.jsonl</c> (git-ignored). Stores request
/// definitions as written in files, never resolved variable values.
/// </summary>
public sealed class HistoryStore
{
    /// <summary>Maximum number of entries kept.</summary>
    public const int MaxEntries = 500;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _filePath;
    private readonly List<HistoryEntry> _entries;

    /// <summary>Opens the history of the workspace at <paramref name="workspaceRoot"/>.</summary>
    public HistoryStore(string workspaceRoot)
    {
        _filePath = System.IO.Path.Combine(workspaceRoot, WorkspaceLayout.LocalStateDirectory, "history.jsonl");
        _entries = Read(_filePath);
    }

    /// <summary>Entries, most recent first.</summary>
    public IReadOnlyList<HistoryEntry> Entries => _entries;

    /// <summary>Adds an entry and persists it.</summary>
    public void Add(HistoryEntry entry)
    {
        _entries.Insert(0, entry);
        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_filePath)!);
            var temporary = _filePath + ".tmp";
            File.WriteAllLines(temporary, _entries.AsEnumerable().Reverse().Select(e => JsonSerializer.Serialize(e, JsonOptions)));
            File.Move(temporary, _filePath, overwrite: true);
        }
        catch (IOException)
        {
            // History is a convenience; failing to persist it must not interrupt the user.
        }
    }

    /// <summary>Removes all entries.</summary>
    public void Clear()
    {
        _entries.Clear();
        File.Delete(_filePath);
    }

    private static List<HistoryEntry> Read(string path)
    {
        var entries = new List<HistoryEntry>();
        if (!File.Exists(path))
        {
            return entries;
        }

        foreach (var line in File.ReadLines(path))
        {
            try
            {
                if (JsonSerializer.Deserialize<HistoryEntry>(line, JsonOptions) is { } entry)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Skip a damaged line rather than losing the whole history.
            }
        }

        entries.Reverse();
        return entries.Take(MaxEntries).ToList();
    }
}
