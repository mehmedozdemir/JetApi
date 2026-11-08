using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Core.Model;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Storage;

namespace JetApi.Desktop.ViewModels;

/// <summary>An entry of the environment picker.</summary>
/// <param name="Key">Environment key; <c>null</c> for "no environment".</param>
/// <param name="Name">Display name.</param>
public sealed record EnvironmentOption(string? Key, string Name)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>What the main window needs from its host (theme, language, closing).</summary>
public interface IWorkspaceHost
{
    /// <summary>Per-user settings.</summary>
    AppSettings Settings { get; }

    /// <summary>Closes the workspace and returns to the welcome screen.</summary>
    void CloseWorkspace();

    /// <summary>Cycles the color theme.</summary>
    void ToggleTheme();

    /// <summary>Switches between Turkish and English.</summary>
    void ToggleLanguage();

    /// <summary>Runs <paramref name="action"/> on the UI thread.</summary>
    void Post(Action action);
}

/// <summary>An open workspace: sidebar tree, tabs, environments and overlays.</summary>
public sealed partial class WorkspaceViewModel : ViewModelBase, IRequestHost, IDisposable
{
    private readonly IWorkspaceHost _host;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private WorkspaceWatcher? _watcher;
    private bool _updatingEnvironment;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTabs))]
    private RequestTabViewModel? _selectedTab;

    [ObservableProperty]
    private EnvironmentOption? _selectedEnvironment;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isHistoryVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlay))]
    private EnvironmentsViewModel? _environments;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlay))]
    private NodeSettingsViewModel? _nodeSettings;

    [ObservableProperty]
    private CommandPaletteViewModel? _palette;

    [ObservableProperty]
    private bool _isDiagnosticsVisible;

    /// <summary>Creates the view model for an opened session.</summary>
    public WorkspaceViewModel(WorkspaceSession session, RequestRunner runner, IDialogService dialogs, IWorkspaceHost host, bool watchFiles = true)
    {
        Session = session;
        Runner = runner;
        Dialogs = dialogs;
        _host = host;
        History = new HistoryStore(session.Root);
        GitBranch = GitInfo.TryGetBranch(session.Root);

        foreach (var collection in session.Workspace.Collections)
        {
            _expanded.Add(collection.Key + "/");
        }

        RebuildTree();
        RebuildEnvironments();
        RebuildHistory();
        IsDiagnosticsVisible = session.Diagnostics.Count > 0;
        session.Changed += (_, _) => _host.Post(OnSessionChanged);
        if (watchFiles)
        {
            _watcher = new WorkspaceWatcher(session.Root, () => _host.Post(() => _ = ReloadAsync()));
        }
    }

    /// <inheritdoc />
    public WorkspaceSession Session { get; }

    /// <inheritdoc />
    public RequestRunner Runner { get; }

    /// <inheritdoc />
    public IDialogService Dialogs { get; }

    /// <inheritdoc />
    public HistoryStore History { get; }

    /// <inheritdoc />
    public bool ConfirmUndefinedVariables => _host.Settings.ConfirmUndefinedVariables;

    /// <summary>Workspace name.</summary>
    public string WorkspaceName => Session.Workspace.Name;

    /// <summary>Workspace directory.</summary>
    public string RootPath => Session.Root;

    /// <summary>Current Git branch, if the workspace is in a repository.</summary>
    public string? GitBranch { get; private set; }

    /// <summary>"git: main".</summary>
    public string GitText => GitBranch is null ? string.Empty : Loc.Format("Status_Git", GitBranch);

    /// <summary>Collections in the sidebar.</summary>
    public ObservableCollection<TreeNodeViewModel> Collections { get; } = [];

    /// <summary>Open tabs.</summary>
    public ObservableCollection<RequestTabViewModel> Tabs { get; } = [];

    /// <summary>Environment picker entries.</summary>
    public ObservableCollection<EnvironmentOption> EnvironmentOptions { get; } = [];

    /// <summary>History list.</summary>
    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];

    /// <summary>Files skipped while loading.</summary>
    public IReadOnlyList<StorageDiagnostic> Diagnostics => Session.Diagnostics;

    /// <summary>"2 files were skipped…".</summary>
    public string DiagnosticsTitle => Loc.Format("Diag_Skipped", Session.Diagnostics.Count);

    /// <summary>Any tab is open.</summary>
    public bool HasTabs => Tabs.Count > 0;

    /// <summary>The workspace has no collections.</summary>
    public bool HasNoCollections => Collections.Count == 0;

    /// <summary>An overlay page (environments, settings) covers the editor.</summary>
    public bool HasOverlay => Environments is not null || NodeSettings is not null;

    /// <summary>The history list is empty.</summary>
    public bool HasNoHistory => HistoryItems.Count == 0;

    /// <summary>Unsaved tabs, for the status bar.</summary>
    public string UnsavedText
    {
        get
        {
            var count = Tabs.Count(t => t.IsDirty);
            return count == 0 ? Loc.Get("Status_Saved") : Loc.Format("Status_Unsaved", count);
        }
    }

    /// <summary>Opens a request or toggles a container.</summary>
    [RelayCommand]
    public void OpenNode(TreeNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.IsContainer)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }

        OpenRequest(node.CollectionKey, node.Path);
    }

    /// <summary>Opens the request at <paramref name="path"/> or focuses its tab.</summary>
    public RequestTabViewModel? OpenRequest(string collectionKey, IReadOnlyList<string> path)
    {
        var identity = $"{collectionKey}/{string.Join('/', path)}";
        if (Tabs.FirstOrDefault(t => t.Identity == identity) is { } existing)
        {
            SelectedTab = existing;
            return existing;
        }

        var collection = Session.Workspace.FindCollection(collectionKey);
        if (collection is null || CollectionTree.Find(collection, path) is not RequestNode request)
        {
            return null;
        }

        return AddTab(CreateTab(collectionKey, path, request));
    }

    /// <summary>Creates an HTTP request in the given collection or folder (default: first collection).</summary>
    [RelayCommand]
    private Task NewHttpRequestAsync(TreeNodeViewModel? parent) =>
        CreateRequestAsync(parent, name => new HttpRequestDefinition { Key = "new", Name = name, Url = Session.Workspace.Environments.Any(e => e.Variables.Any(v => v.Name == "baseUrl")) ? "{{baseUrl}}/" : string.Empty });

    /// <summary>Creates a gRPC request.</summary>
    [RelayCommand]
    private Task NewGrpcRequestAsync(TreeNodeViewModel? parent) =>
        CreateRequestAsync(parent, name => new GrpcRequestDefinition { Key = "new", Name = name, ServerUrl = "http://localhost:5000" });

    /// <summary>Creates a folder.</summary>
    [RelayCommand]
    private async Task NewFolderAsync(TreeNodeViewModel? parent)
    {
        var target = ContainerOf(parent);
        if (target is null)
        {
            return;
        }

        var name = await Dialogs.PromptTextAsync(Loc.Get("Menu_NewFolder"), Loc.Get("Prompt_Name"), Loc.Get("Prompt_NewFolderName")).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(name))
        {
            await RunSavingAsync(async () =>
            {
                var path = await Session.AddNodeAsync(target.Value.CollectionKey, target.Value.Path, new Folder { Key = "new", Name = name.Trim() }).ConfigureAwait(true);
                _expanded.Add($"{target.Value.CollectionKey}/{string.Join('/', target.Value.Path)}");
                _expanded.Add($"{target.Value.CollectionKey}/{string.Join('/', path)}");
            }).ConfigureAwait(true);
        }
    }

    /// <summary>Creates a collection.</summary>
    [RelayCommand]
    private async Task NewCollectionAsync()
    {
        var name = await Dialogs.PromptTextAsync(Loc.Get("Side_NewCollection"), Loc.Get("Prompt_Name"), Loc.Get("Prompt_NewCollectionName")).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(name))
        {
            await RunSavingAsync(async () =>
            {
                var key = await Session.AddCollectionAsync(name.Trim()).ConfigureAwait(true);
                _expanded.Add(key + "/");
            }).ConfigureAwait(true);
        }
    }

    /// <summary>Renames a node (its key and file name stay the same).</summary>
    [RelayCommand]
    private async Task RenameAsync(TreeNodeViewModel node)
    {
        var name = await Dialogs.PromptTextAsync(Loc.Get("Menu_Rename"), Loc.Get("Prompt_Name"), node.Name).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == node.Name)
        {
            return;
        }

        await RunSavingAsync(async () =>
        {
            var collection = Session.Workspace.FindCollection(node.CollectionKey)!;
            if (node.IsCollection)
            {
                await Session.SaveCollectionAsync(collection with { Name = name.Trim() }).ConfigureAwait(true);
                return;
            }

            var current = CollectionTree.Find(collection, node.Path)!;
            var renamed = current with { Name = name.Trim() };
            await Session.SaveNodeAsync(node.CollectionKey, node.Path, renamed).ConfigureAwait(true);
            if (renamed is RequestNode request && FindTab(node) is { } tab)
            {
                tab.OnSavedElsewhere(request);
            }
        }).ConfigureAwait(true);
    }

    /// <summary>Duplicates a request or folder next to the original.</summary>
    [RelayCommand]
    private async Task DuplicateAsync(TreeNodeViewModel node)
    {
        if (node.IsCollection)
        {
            return;
        }

        await RunSavingAsync(async () =>
        {
            var collection = Session.Workspace.FindCollection(node.CollectionKey)!;
            var original = CollectionTree.Find(collection, node.Path)!;
            var parent = node.Path.SkipLast(1).ToList();
            var index = CollectionTree.ChildrenOf(collection, parent).ToList().FindIndex(c => c.Key == original.Key) + 1;
            var copy = original with { Name = Loc.Format("Copy_Suffix", original.Name) };
            var path = await Session.AddNodeAsync(node.CollectionKey, parent, copy).ConfigureAwait(true);

            // AddNodeAsync appends; move the copy right after the original.
            await Session.MoveNodeAsync(node.CollectionKey, path, parent, index).ConfigureAwait(true);
            if (copy is RequestNode)
            {
                OpenRequest(node.CollectionKey, path);
            }
        }).ConfigureAwait(true);
    }

    /// <summary>Deletes a collection, folder or request after confirmation.</summary>
    [RelayCommand]
    private async Task DeleteAsync(TreeNodeViewModel node)
    {
        var message = Loc.Format(node.IsCollection ? "Confirm_DeleteCollection" : "Confirm_Delete", node.Name);
        if (!await Dialogs.ConfirmAsync(Loc.Get("Menu_Delete"), message, Loc.Get("Btn_Delete"), isDestructive: true).ConfigureAwait(true))
        {
            return;
        }

        await RunSavingAsync(async () =>
        {
            var prefix = node.Identity.TrimEnd('/');
            foreach (var tab in Tabs.Where(t => t.Path is not null && t.CollectionKey == node.CollectionKey
                && (node.IsCollection || $"{t.CollectionKey}/{string.Join('/', t.Path)}" == prefix || $"{t.CollectionKey}/{string.Join('/', t.Path)}".StartsWith(prefix + "/", StringComparison.Ordinal))).ToList())
            {
                RemoveTab(tab);
            }

            if (node.IsCollection)
            {
                await Session.DeleteCollectionAsync(node.CollectionKey).ConfigureAwait(true);
            }
            else
            {
                await Session.DeleteNodeAsync(node.CollectionKey, node.Path).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    /// <summary>Moves a node to the top level of its collection.</summary>
    [RelayCommand]
    private Task MoveToRootAsync(TreeNodeViewModel node) =>
        node.Path.Count <= 1 ? Task.CompletedTask : MoveAsync(node, null);

    /// <summary>Moves <paramref name="node"/> into <paramref name="target"/> (a folder or collection) or next to it (a request).</summary>
    public async Task MoveAsync(TreeNodeViewModel node, TreeNodeViewModel? target)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.IsCollection || (target is not null && target.CollectionKey != node.CollectionKey))
        {
            return;
        }

        var newParent = target switch
        {
            null => [],
            { IsContainer: true } => target.Path,
            _ => target.Path.SkipLast(1).ToList(),
        };

        await RunSavingAsync(async () =>
        {
            var collection = Session.Workspace.FindCollection(node.CollectionKey)!;
            int? index = target is { IsRequest: true }
                ? CollectionTree.ChildrenOf(collection, newParent).ToList().FindIndex(c => c.Key == target.Path[^1])
                : null;
            var oldIdentity = $"{node.CollectionKey}/{string.Join('/', node.Path)}";
            var newPath = await Session.MoveNodeAsync(node.CollectionKey, node.Path, newParent, index).ConfigureAwait(true);
            if (node.IsRequest && Tabs.FirstOrDefault(t => t.Identity == oldIdentity) is { } tab && !tab.IsDirty)
            {
                var replacement = CreateTab(node.CollectionKey, newPath, (RequestNode)CollectionTree.Find(Session.Workspace.FindCollection(node.CollectionKey)!, newPath)!);
                ReplaceTab(tab, replacement);
            }
        }).ConfigureAwait(true);
    }

    /// <summary>Opens collection or folder settings.</summary>
    [RelayCommand]
    private void OpenNodeSettings(TreeNodeViewModel node)
    {
        if (node.IsContainer)
        {
            Environments = null;
            NodeSettings = new NodeSettingsViewModel(Session, Dialogs, node.CollectionKey, node.Path, _ => NodeSettings = null);
        }
    }

    /// <summary>Closes a tab, asking first when it has unsaved changes.</summary>
    [RelayCommand]
    public async Task CloseTabAsync(RequestTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null)
        {
            return;
        }

        if (tab.IsDirty && tab.Path is not null
            && !await Dialogs.ConfirmAsync(Loc.Get("Btn_Close"), Loc.Format("Confirm_CloseUnsaved", tab.Name), Loc.Get("Btn_Close"), isDestructive: true).ConfigureAwait(true))
        {
            return;
        }

        RemoveTab(tab);
    }

    /// <summary>Saves the selected tab.</summary>
    [RelayCommand]
    private Task SaveSelectedAsync() => SelectedTab?.SaveAsync() ?? Task.CompletedTask;

    /// <summary>Sends the selected tab.</summary>
    [RelayCommand]
    private Task SendSelectedAsync() => SelectedTab?.SendAsync() ?? Task.CompletedTask;

    /// <summary>Opens the environment editor.</summary>
    [RelayCommand]
    public void ShowEnvironments()
    {
        NodeSettings = null;
        Environments = new EnvironmentsViewModel(Session, Dialogs, () => Environments = null);
    }

    /// <summary>Shows or hides the history list in the sidebar.</summary>
    [RelayCommand]
    private void ToggleHistory() => IsHistoryVisible = !IsHistoryVisible;

    /// <summary>Opens a history entry in a new (unsaved) tab.</summary>
    [RelayCommand]
    private async Task OpenHistoryEntryAsync(HistoryItemViewModel item)
    {
        try
        {
            var request = item.Entry.ToRequest();
            var collectionKey = item.Entry.CollectionKey is { } key && Session.Workspace.FindCollection(key) is not null
                ? key
                : (Session.Workspace.Collections.Count > 0 ? Session.Workspace.Collections[0].Key : null);
            if (collectionKey is not null)
            {
                AddTab(CreateTab(collectionKey, null, request));
            }
        }
        catch (InvalidDataException exception)
        {
            await Dialogs.ShowErrorAsync(exception.Message).ConfigureAwait(true);
        }
    }

    /// <summary>Clears the history.</summary>
    [RelayCommand]
    private void ClearHistory()
    {
        History.Clear();
        RebuildHistory();
    }

    /// <summary>Opens the command palette.</summary>
    [RelayCommand]
    public void OpenPalette() => Palette = new CommandPaletteViewModel(BuildPaletteItems(), () => Palette = null);

    /// <summary>Re-reads the workspace from disk.</summary>
    [RelayCommand]
    public async Task ReloadAsync()
    {
        try
        {
            await Session.ReloadAsync().ConfigureAwait(true);
            IsDiagnosticsVisible = Session.Diagnostics.Count > 0;
            OnPropertyChanged(nameof(Diagnostics));
            OnPropertyChanged(nameof(DiagnosticsTitle));
        }
        catch (WorkspaceStorageException exception)
        {
            await Dialogs.ShowErrorAsync(Loc.Format("Error_Open", exception.Message)).ConfigureAwait(true);
        }
    }

    /// <summary>Hides the skipped-files banner.</summary>
    [RelayCommand]
    private void DismissDiagnostics() => IsDiagnosticsVisible = false;

    /// <summary>Closes the workspace.</summary>
    [RelayCommand]
    private void CloseWorkspace() => _host.CloseWorkspace();

    /// <summary>Cycles the theme.</summary>
    [RelayCommand]
    private void ToggleTheme() => _host.ToggleTheme();

    /// <summary>Switches the language.</summary>
    [RelayCommand]
    private void ToggleLanguage() => _host.ToggleLanguage();

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SaveRequestAsync(RequestTabViewModel tab, RequestNode request)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _watcher?.IgnoreOwnWrites();
        if (tab.Path is { } path && Session.Workspace.FindCollection(tab.CollectionKey) is { } collection && CollectionTree.Find(collection, path) is not null)
        {
            await Session.SaveNodeAsync(tab.CollectionKey, path, request).ConfigureAwait(true);
            return path;
        }

        return await Session.AddNodeAsync(tab.CollectionKey, [], request).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public void AddHistory(HistoryEntry entry)
    {
        History.Add(entry);
        RebuildHistory();
    }

    /// <inheritdoc />
    public void OpenCollectionSettings(string collectionKey)
    {
        if (Session.Workspace.FindCollection(collectionKey) is not null)
        {
            Environments = null;
            NodeSettings = new NodeSettingsViewModel(Session, Dialogs, collectionKey, [], _ => NodeSettings = null);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        foreach (var tab in Tabs)
        {
            tab.Dispose();
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(SelectedEnvironment) when !_updatingEnvironment && SelectedEnvironment is not null:
                if (SelectedEnvironment.Key != Session.ActiveEnvironmentKey)
                {
                    Session.SetActiveEnvironment(SelectedEnvironment.Key);
                }

                break;
            case nameof(FilterText):
                foreach (var collection in Collections)
                {
                    collection.ApplyFilter(FilterText.Trim());
                }

                break;
        }
    }

    private async Task CreateRequestAsync(TreeNodeViewModel? parent, Func<string, RequestNode> create)
    {
        var target = ContainerOf(parent);
        if (target is null)
        {
            if (Session.Workspace.Collections.Count == 0)
            {
                await NewCollectionAsync().ConfigureAwait(true);
                target = ContainerOf(null);
            }

            if (target is null)
            {
                return;
            }
        }

        var name = await Dialogs.PromptTextAsync(Loc.Get("Btn_NewTab"), Loc.Get("Prompt_Name"), Loc.Get("Prompt_NewRequestName")).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunSavingAsync(async () =>
        {
            var (collectionKey, parentPath) = target.Value;
            var path = await Session.AddNodeAsync(collectionKey, parentPath, create(name.Trim())).ConfigureAwait(true);
            _expanded.Add($"{collectionKey}/{string.Join('/', parentPath)}");
            RebuildTree();
            OpenRequest(collectionKey, path);
        }).ConfigureAwait(true);
    }

    // The collection/folder to add into: the node itself, the folder containing a request,
    // the collection of the selected tab, or the first collection.
    private (string CollectionKey, IReadOnlyList<string> Path)? ContainerOf(TreeNodeViewModel? node) => node switch
    {
        { IsContainer: true } => (node.CollectionKey, node.Path),
        { } request => (request.CollectionKey, request.Path.SkipLast(1).ToList()),
        null when SelectedTab is { } tab && Session.Workspace.FindCollection(tab.CollectionKey) is not null
            => (tab.CollectionKey, tab.Path is { Count: > 1 } path && CollectionTree.Find(Session.Workspace.FindCollection(tab.CollectionKey)!, [.. path.SkipLast(1)]) is Folder ? [.. path.SkipLast(1)] : []),
        null when Session.Workspace.Collections.Count > 0 => (Session.Workspace.Collections[0].Key, []),
        _ => null,
    };

    private RequestTabViewModel CreateTab(string collectionKey, IReadOnlyList<string>? path, RequestNode request)
    {
        RequestTabViewModel tab = request switch
        {
            GrpcRequestDefinition grpc => new GrpcRequestTabViewModel(this, collectionKey, path, grpc),
            HttpRequestDefinition http => new HttpRequestTabViewModel(this, collectionKey, path, http),
            _ => throw new NotSupportedException(request.GetType().Name),
        };
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RequestTabViewModel.IsDirty))
            {
                OnPropertyChanged(nameof(UnsavedText));
            }
        };
        return tab;
    }

    private RequestTabViewModel AddTab(RequestTabViewModel tab)
    {
        Tabs.Add(tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(UnsavedText));
        return tab;
    }

    private void RemoveTab(RequestTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();
        if (SelectedTab == tab || SelectedTab is null)
        {
            SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        }

        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(UnsavedText));
    }

    private void ReplaceTab(RequestTabViewModel old, RequestTabViewModel replacement)
    {
        var index = Tabs.IndexOf(old);
        var wasSelected = SelectedTab == old;
        Tabs[index] = replacement;
        old.Dispose();
        if (wasSelected)
        {
            SelectedTab = replacement;
        }
    }

    private RequestTabViewModel? FindTab(TreeNodeViewModel node) => Tabs.FirstOrDefault(t => t.Identity == node.Identity);

    private async Task RunSavingAsync(Func<Task> action)
    {
        _watcher?.IgnoreOwnWrites();
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or WorkspaceStorageException or ArgumentException)
        {
            await Dialogs.ShowErrorAsync(Loc.Format("Error_Save", exception.Message)).ConfigureAwait(true);
        }
        finally
        {
            _watcher?.IgnoreOwnWrites();
        }
    }

    private void OnSessionChanged()
    {
        RebuildTree();
        RebuildEnvironments();
        RefreshTabsFromDisk();
        foreach (var tab in Tabs)
        {
            tab.OnContextChanged();
        }

        GitBranch = GitInfo.TryGetBranch(Session.Root);
        OnPropertyChanged(nameof(WorkspaceName));
        OnPropertyChanged(nameof(GitText));
        OnPropertyChanged(nameof(HasNoCollections));
    }

    // After a reload, clean tabs show the new file content; tabs whose request disappeared are kept as unsaved.
    private void RefreshTabsFromDisk()
    {
        foreach (var tab in Tabs.ToList())
        {
            if (tab.Path is null || tab.IsDirty)
            {
                continue;
            }

            var current = Session.Workspace.FindCollection(tab.CollectionKey) is { } collection ? CollectionTree.Find(collection, tab.Path) as RequestNode : null;
            if (current is not null && RequestFiles.Serialize(current) != RequestFiles.Serialize(tab.BuildRequest()))
            {
                ReplaceTab(tab, CreateTab(tab.CollectionKey, tab.Path, current));
            }
        }
    }

    private void RebuildTree()
    {
        foreach (var node in Collections.SelectMany(Flatten))
        {
            if (node.IsExpanded)
            {
                _expanded.Add(node.Identity);
            }
            else
            {
                _expanded.Remove(node.Identity);
            }
        }

        Collections.Clear();
        foreach (var collection in Session.Workspace.Collections)
        {
            var root = TreeNodeViewModel.FromCollection(collection, _expanded);
            if (FilterText.Length > 0)
            {
                root.ApplyFilter(FilterText.Trim());
            }

            Collections.Add(root);
        }

        OnPropertyChanged(nameof(HasNoCollections));
    }

    private static IEnumerable<TreeNodeViewModel> Flatten(TreeNodeViewModel node) =>
        node.Children.SelectMany(Flatten).Prepend(node);

    private void RebuildEnvironments()
    {
        _updatingEnvironment = true;
        EnvironmentOptions.Clear();
        EnvironmentOptions.Add(new EnvironmentOption(null, Loc.Get("Top_NoEnvironment")));
        foreach (var environment in Session.Workspace.Environments)
        {
            EnvironmentOptions.Add(new EnvironmentOption(environment.Key, environment.Name));
        }

        SelectedEnvironment = EnvironmentOptions.FirstOrDefault(o => o.Key == Session.ActiveEnvironmentKey) ?? EnvironmentOptions[0];
        _updatingEnvironment = false;
    }

    private void RebuildHistory()
    {
        HistoryItems.Clear();
        foreach (var entry in History.Entries)
        {
            HistoryItems.Add(new HistoryItemViewModel(entry));
        }

        OnPropertyChanged(nameof(HasNoHistory));
    }

    private List<PaletteItemViewModel> BuildPaletteItems()
    {
        var items = new List<PaletteItemViewModel>();
        var requests = Loc.Get("Palette_Requests");
        foreach (var collection in Session.Workspace.Collections)
        {
            foreach (var (path, request) in CollectionTree.EnumerateRequests(collection))
            {
                var folders = path.Count > 1
                    ? " / " + string.Join(" / ", Enumerable.Range(1, path.Count - 1).Select(length => CollectionTree.Find(collection, [.. path.Take(length)])!.Name))
                    : string.Empty;
                var badge = request is GrpcRequestDefinition ? "gRPC" : ((HttpRequestDefinition)request).Method;
                var detail = request is GrpcRequestDefinition grpc ? $"{collection.Name}{folders} · {grpc.Service}/{grpc.Method}" : collection.Name + folders;
                var capturedPath = path;
                var key = collection.Key;
                items.Add(new PaletteItemViewModel(requests, badge, request.Name, detail, string.Empty, () =>
                {
                    OpenRequest(key, capturedPath);
                    return Task.CompletedTask;
                }));
            }
        }

        var environments = Loc.Get("Palette_Environments");
        foreach (var option in EnvironmentOptions)
        {
            var captured = option;
            items.Add(new PaletteItemViewModel(
                environments,
                "›",
                Loc.Format("Palette_SwitchEnv", option.Name),
                Loc.Get(option.Key == Session.ActiveEnvironmentKey ? "Palette_ActiveEnv" : "Palette_SwitchHint"),
                string.Empty,
                () =>
                {
                    SelectedEnvironment = captured;
                    return Task.CompletedTask;
                }));
        }

        var commands = Loc.Get("Palette_Commands");
        items.Add(new PaletteItemViewModel(commands, "›", Loc.Get("Cmd_NewHttp"), string.Empty, "Ctrl N", () => NewHttpRequestAsync(null)));
        items.Add(new PaletteItemViewModel(commands, "›", Loc.Get("Cmd_NewGrpc"), string.Empty, string.Empty, () => NewGrpcRequestAsync(null)));
        items.Add(new PaletteItemViewModel(commands, "›", Loc.Get("Cmd_NewCollection"), string.Empty, string.Empty, NewCollectionAsync));
        items.Add(new PaletteItemViewModel(commands, "›", Loc.Get("Cmd_Environments"), string.Empty, "Ctrl E", () =>
        {
            ShowEnvironments();
            return Task.CompletedTask;
        }));
        items.Add(new PaletteItemViewModel(commands, "›", Loc.Get("Cmd_History"), string.Empty, string.Empty, () =>
        {
            IsHistoryVisible = true;
            return Task.CompletedTask;
        }));
        items.Add(new PaletteItemViewModel(commands, "›", Loc.Get("Cmd_Reload"), string.Empty, string.Empty, ReloadAsync));
        items.Add(new PaletteItemViewModel(commands, "›", Loc.Get("Cmd_ToggleTheme"), string.Empty, string.Empty, () =>
        {
            _host.ToggleTheme();
            return Task.CompletedTask;
        }));
        items.Add(new PaletteItemViewModel(commands, "›", Loc.Get("Cmd_ToggleLanguage"), string.Empty, string.Empty, () =>
        {
            _host.ToggleLanguage();
            return Task.CompletedTask;
        }));
        items.Add(new PaletteItemViewModel(commands, "›", Loc.Get("Cmd_CloseWorkspace"), string.Empty, string.Empty, () =>
        {
            _host.CloseWorkspace();
            return Task.CompletedTask;
        }));
        return items;
    }
}
