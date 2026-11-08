using JetApi.Storage;

namespace JetApi.Desktop.Services;

/// <summary>
/// Watches a workspace for changes made outside JetApi (e.g. <c>git pull</c>, an editor) and reports
/// them once things settle. Changes JetApi itself just wrote are ignored.
/// </summary>
public sealed class WorkspaceWatcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan OwnWriteWindow = TimeSpan.FromSeconds(1.5);

    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly Action _onExternalChange;
    private DateTime _ignoreUntil = DateTime.MinValue;

    /// <summary>Starts watching <paramref name="root"/>.</summary>
    public WorkspaceWatcher(string root, Action onExternalChange)
    {
        _onExternalChange = onExternalChange;
        _timer = new Timer(_ => _onExternalChange(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Deleted += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Call right after JetApi writes files, so its own writes are not reported.</summary>
    public void IgnoreOwnWrites() => _ignoreUntil = DateTime.UtcNow + OwnWriteWindow;

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (DateTime.UtcNow < _ignoreUntil || !IsRelevant(e.FullPath))
        {
            return;
        }

        _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    private static bool IsRelevant(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/" + WorkspaceLayout.LocalStateDirectory + "/", StringComparison.Ordinal)
            || normalized.Contains("/.git/", StringComparison.Ordinal)
            || normalized.EndsWith(".tmp", StringComparison.Ordinal))
        {
            return false;
        }

        return normalized.EndsWith(".yaml", StringComparison.Ordinal) || normalized.EndsWith(".proto", StringComparison.Ordinal) || !Path.HasExtension(normalized);
    }
}
