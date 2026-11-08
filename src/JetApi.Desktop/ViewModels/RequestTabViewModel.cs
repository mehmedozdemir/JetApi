using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Core.Model;
using JetApi.Core.Variables;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Storage;

namespace JetApi.Desktop.ViewModels;

/// <summary>What a request tab needs from the workspace it lives in.</summary>
public interface IRequestHost
{
    /// <summary>The open workspace.</summary>
    WorkspaceSession Session { get; }

    /// <summary>Runs requests.</summary>
    RequestRunner Runner { get; }

    /// <summary>Dialogs.</summary>
    IDialogService Dialogs { get; }

    /// <summary>Request history.</summary>
    HistoryStore History { get; }

    /// <summary>Ask before sending with undefined variables.</summary>
    bool ConfirmUndefinedVariables { get; }

    /// <summary>Opens the environment editor.</summary>
    void ShowEnvironments();

    /// <summary>Saves the tab's request; adds it to its collection when it is not saved yet.</summary>
    /// <returns>The path of the saved request.</returns>
    Task<IReadOnlyList<string>> SaveRequestAsync(RequestTabViewModel tab, RequestNode request);

    /// <summary>Records a sent request in the history list.</summary>
    void AddHistory(HistoryEntry entry);

    /// <summary>Opens the settings of a collection (name, auth, proto files).</summary>
    void OpenCollectionSettings(string collectionKey);

    /// <summary>Closes a tab (asking first when it has unsaved changes).</summary>
    Task CloseTabAsync(RequestTabViewModel? tab);
}

/// <summary>A variable used in the URL or message, with its value for the hint line.</summary>
/// <param name="Placeholder">The placeholder, e.g. <c>{{baseUrl}}</c>.</param>
/// <param name="Value">Resolved value (masked when secret), or empty.</param>
/// <param name="Description">"source: Development environment" or "not defined".</param>
/// <param name="IsDefined">Whether it resolves.</param>
public sealed record VariableHint(string Placeholder, string Value, string Description, bool IsDefined);

/// <summary>An open request tab (HTTP or gRPC).</summary>
public abstract partial class RequestTabViewModel : ViewModelBase, IDisposable
{
    private string _savedYaml;
    private CancellationTokenSource? _sending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Breadcrumb))]
    private string _name;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    /// <summary>Creates a tab.</summary>
    protected RequestTabViewModel(IRequestHost host, string collectionKey, IReadOnlyList<string>? path, RequestNode request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Host = host;
        CollectionKey = collectionKey;
        Path = path;
        _name = request.Name;
        Key = request.Key;
        Description = request.Description;
        _savedYaml = path is null ? string.Empty : RequestFiles.Serialize(request);
        Variables = CreateVariableContext();
    }

    /// <summary>Variables visible to this tab, for highlighting and hover hints in editors.</summary>
    [ObservableProperty]
    public partial VariableContext Variables { get; private set; }

    /// <summary>The workspace.</summary>
    protected IRequestHost Host { get; }

    /// <summary>Owning collection.</summary>
    public string CollectionKey { get; }

    /// <summary>Path in the collection; <c>null</c> when the request is not saved (e.g. opened from history).</summary>
    public IReadOnlyList<string>? Path { get; private set; }

    /// <summary>The request key.</summary>
    protected string Key { get; private set; }

    /// <summary>Markdown description, kept as is.</summary>
    protected string? Description { get; }

    /// <summary>True for gRPC tabs.</summary>
    public abstract bool IsGrpc { get; }

    /// <summary>Badge text: method or "gRPC".</summary>
    public abstract string Badge { get; }

    /// <summary>Text of the send button.</summary>
    public string SendLabel => Loc.Get(IsGrpc ? "Btn_Invoke" : "Btn_Send");

    /// <summary>"User Service / Admin / Create user".</summary>
    public string Breadcrumb => string.Join("  /  ", [.. ParentNames(), Name]);

    /// <summary>"User Service / Admin /", shown before the request name; empty when the request has no collection.</summary>
    public string BreadcrumbParent => ParentNames() is { Count: > 0 } parents ? string.Join(" / ", parents) + " /" : string.Empty;

    private List<string> ParentNames()
    {
        var parts = new List<string>();
        var collection = Host.Session.Workspace.FindCollection(CollectionKey);
        if (collection is null)
        {
            return parts;
        }

        parts.Add(collection.Name);
        if (Path is not null)
        {
            for (var length = 1; length < Path.Count; length++)
            {
                if (CollectionTree.Find(collection, [.. Path.Take(length)]) is { } node)
                {
                    parts.Add(node.Name);
                }
            }
        }

        return parts;
    }

    /// <summary>Identifies the tab for "open or focus".</summary>
    public string Identity => Path is null ? $"unsaved:{GetHashCode()}" : $"{CollectionKey}/{string.Join('/', Path)}";

    /// <summary>The request as currently edited.</summary>
    public abstract RequestNode BuildRequest();

    /// <summary>Sends the request.</summary>
    [RelayCommand]
    public async Task SendAsync()
    {
        if (IsBusy)
        {
            return;
        }

        _sending?.Dispose();
        _sending = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            await SendCoreAsync(_sending.Token).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Cancels a running request.</summary>
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _sending?.Cancel();

    /// <summary>Closes this tab.</summary>
    [RelayCommand]
    private Task CloseAsync() => Host.CloseTabAsync(this);

    /// <summary>Saves the request to its file.</summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        var request = BuildRequest();
        try
        {
            Path = await Host.SaveRequestAsync(this, request).ConfigureAwait(true);
            Key = Path[^1];
            _savedYaml = RequestFiles.Serialize(BuildRequest());
            UpdateDirty();
            OnPropertyChanged(nameof(Breadcrumb));
            OnPropertyChanged(nameof(BreadcrumbParent));
            OnPropertyChanged(nameof(Identity));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or WorkspaceStorageException or ArgumentException)
        {
            await Host.Dialogs.ShowErrorAsync(Loc.Format("Error_Save", exception.Message)).ConfigureAwait(true);
        }
    }

    /// <summary>Called when the saved copy changed on disk (e.g. renamed in the tree).</summary>
    public void OnSavedElsewhere(RequestNode saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        Name = saved.Name;
        _savedYaml = RequestFiles.Serialize(saved);
        UpdateDirty();
    }

    /// <summary>Cancels a running request and releases resources when the tab closes.</summary>
    public void Dispose()
    {
        _sending?.Cancel();
        _sending?.Dispose();
        _sending = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>Called after the active environment or workspace changed.</summary>
    public virtual void OnContextChanged()
    {
        Variables = CreateVariableContext();
        OnPropertyChanged(nameof(Breadcrumb));
        OnPropertyChanged(nameof(BreadcrumbParent));
    }

    /// <summary>Sends using the current editor state.</summary>
    protected abstract Task SendCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets or renews the OAuth 2.0 token before sending when the request uses OAuth 2.0 (set or inherited).
    /// </summary>
    /// <returns>An error for the response pane, or <c>null</c>.</returns>
    protected async Task<string?> EnsureOAuthTokenAsync(AuthConfig requestAuth, CancellationToken cancellationToken)
    {
        if (RequestRunner.ResolveOAuth(Host.Session, CollectionKey, Path ?? [Key], requestAuth) is not { } oauth)
        {
            return null;
        }

        var error = await Host.Runner.EnsureOAuthTokenAsync(Host.Session, oauth, cancellationToken).ConfigureAwait(true);
        OnTokenChanged();
        return error;
    }

    /// <summary>Called after sending got or renewed an OAuth 2.0 token, so the auth editor can show it.</summary>
    protected virtual void OnTokenChanged()
    {
    }

    /// <summary>Recomputes <see cref="IsDirty"/>; call after any edit.</summary>
    protected void UpdateDirty() => IsDirty = Path is null || RequestFiles.Serialize(BuildRequest()) != _savedYaml;

    /// <summary>Asks whether to send when variables are undefined. Returns true to send.</summary>
    protected async Task<bool> ConfirmVariablesAsync(IReadOnlyList<VariableIssue> issues)
    {
        if (issues.Count == 0 || !Host.ConfirmUndefinedVariables)
        {
            return true;
        }

        var choice = await Host.Dialogs.ConfirmUndefinedVariablesAsync(issues, Host.Session.ActiveEnvironment?.Name).ConfigureAwait(true);
        if (choice == UndefinedVariablesChoice.OpenEnvironments)
        {
            Host.ShowEnvironments();
        }

        return choice == UndefinedVariablesChoice.SendAnyway;
    }

    /// <summary>Hints for every variable referenced in <paramref name="templates"/>.</summary>
    protected IReadOnlyList<VariableHint> BuildHints(params string[] templates) =>
        [.. templates.SelectMany(VariableResolver.FindReferences).Distinct(StringComparer.Ordinal).Select(Variables.Describe)];

    private VariableContext CreateVariableContext()
    {
        var resolver = RequestRunner.CreateResolver(Host.Session, CollectionKey);
        return new VariableContext(name =>
        {
            var lookup = resolver.Lookup(name);
            var placeholder = "{{" + name + "}}";
            return lookup is null
                ? new VariableHint(placeholder, string.Empty, Loc.Get("Req_VariableUndefined"), false)
                : new VariableHint(placeholder, lookup.IsSecret ? "••••••••" : lookup.Value, DescribeSource(lookup.Source), true);
        });
    }

    private string DescribeSource(VariableSource? source) => source switch
    {
        VariableSource.Environment => Loc.Format("Req_SourceEnvironment", Host.Session.ActiveEnvironment?.Name ?? string.Empty),
        VariableSource.Collection => Loc.Format("Req_SourceCollection", Host.Session.Workspace.FindCollection(CollectionKey)?.Name ?? string.Empty),
        VariableSource.Global => Loc.Get("Req_SourceGlobal"),
        null => Loc.Get("Req_SourceDynamic"),
        _ => Loc.Get("Req_SourceRuntime"),
    };
}
