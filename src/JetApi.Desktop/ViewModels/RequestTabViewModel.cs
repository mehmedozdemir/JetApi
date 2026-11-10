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

    /// <summary>Replaces what <paramref name="tab"/> edits with <paramref name="request"/> (e.g. a pasted cURL command); not saved yet.</summary>
    void ReplaceTabRequest(RequestTabViewModel tab, RequestNode request);

    /// <summary>Closes a tab (asking first when it has unsaved changes).</summary>
    Task CloseTabAsync(RequestTabViewModel? tab);
}

/// <summary>A variable used in the URL or message, with its value for the hint line.</summary>
/// <param name="Placeholder">The placeholder, e.g. <c>{{baseUrl}}</c>.</param>
/// <param name="Value">Resolved value (masked when secret), or empty.</param>
/// <param name="Description">"source: Development environment" or "not defined".</param>
/// <param name="IsDefined">Whether it resolves.</param>
public sealed record VariableHint(string Placeholder, string Value, string Description, bool IsDefined)
{
    /// <summary>The variable name, without braces.</summary>
    public string Name => Placeholder.Length > 4 ? Placeholder[2..^2] : Placeholder;

    /// <summary>The layer the value comes from; <c>null</c> for dynamic or undefined variables.</summary>
    public VariableSource? Source { get; init; }

    /// <summary>The value is a secret (shown masked).</summary>
    public bool IsSecret { get; init; }
}

/// <summary>The address with its variables filled in, shown under the address field.</summary>
/// <param name="Text">The resolved address; empty when it has no variables.</param>
/// <param name="Issue">"1 undefined variable", or empty.</param>
public sealed record AddressPreview(string Text, string Issue)
{
    /// <summary>No variables, nothing to show.</summary>
    public static AddressPreview None { get; } = new(string.Empty, string.Empty);

    /// <summary>Show the line.</summary>
    public bool IsVisible => Text.Length > 0;

    /// <summary>Some variables are undefined.</summary>
    public bool HasIssue => Issue.Length > 0;
}

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
        Scripts = request.Scripts;
        _savedYaml = path is null ? string.Empty : RequestFiles.Serialize(request);
        Variables = CreateVariableContext();
        Tests = new TestsEditorViewModel(request, () =>
        {
            UpdateDirty();
            OnPropertyChanged(nameof(Tests));
        });
    }

    /// <summary>The Tests tab: checks and extractions (ADR-0005).</summary>
    public TestsEditorViewModel Tests { get; }

    /// <summary>Results of the checks and extractions of the last send; <c>null</c> when none ran.</summary>
    [ObservableProperty]
    public partial TestRunViewModel? TestRun { get; private set; }

    /// <summary>A test run is shown (the Test results tab is there).</summary>
    public bool HasTestRun => TestRun is not null;

    /// <summary>"3/4" on the Test results tab.</summary>
    public string TestRunBadge => TestRun?.Badge ?? string.Empty;

    /// <summary>A check failed in the last run (the badge is red, otherwise green).</summary>
    public bool TestRunFailed => TestRun is { AllPassed: false };

    /// <summary>All checks passed in the last run.</summary>
    public bool TestRunPassed => TestRun is { AllPassed: true, HasChecks: true };

    partial void OnTestRunChanged(TestRunViewModel? value)
    {
        OnPropertyChanged(nameof(HasTestRun));
        OnPropertyChanged(nameof(TestRunBadge));
        OnPropertyChanged(nameof(TestRunFailed));
        OnPropertyChanged(nameof(TestRunPassed));
    }

    /// <summary>Opens the Tests section of the request.</summary>
    protected abstract void ShowTestsSection();

    /// <summary>Adds a check for a value picked in the response (right-click on a JSON value).</summary>
    [RelayCommand]
    private void AddCheckFromResponse(JetApi.Testing.JsonPath.JsonPathHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        Tests.Add(hit.IsContainer
            ? new ResponseCheck(CheckSource.Json, CheckOperator.Exists, hit.Path)
            : new ResponseCheck(CheckSource.Json, CheckOperator.Equals, hit.Path, hit.Value!));
        ShowTestsSection();
    }

    /// <summary>Adds an extraction for a value picked in the response.</summary>
    [RelayCommand]
    private void AddExtractionFromResponse(JetApi.Testing.JsonPath.JsonPathHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        Tests.Add(new Extraction(ExtractionSource.Json, hit.Path, VariableNameFor(hit.Name)));
        ShowTestsSection();
    }

    /// <summary>A variable name from a member name: characters a placeholder cannot hold become "_".</summary>
    internal static string VariableNameFor(string name)
    {
        var cleaned = new string([.. name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_')]).Trim('_');
        return cleaned.Length == 0 ? "value" : cleaned;
    }

    /// <summary>
    /// Runs the checks and extractions of the request on <paramref name="response"/>, stores the extracted values and shows
    /// the results. Nothing runs when the request has none.
    /// </summary>
    protected async Task RunTestsAsync(JetApi.Testing.ResponseSnapshot response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var checks = Tests.ToChecks();
        var extractions = Tests.ToExtractions();
        if (!checks.Any(c => c.IsEnabled) && !extractions.Any(e => e.IsEnabled))
        {
            return;
        }

        var resolver = RequestRunner.CreateResolver(Host.Session, CollectionKey);
        string Resolve(string text) => resolver.Resolve(text).Value;
        var checkResults = JetApi.Testing.ResponseChecker.Run(response, checks, Resolve);
        var stored = new List<JetApi.Testing.ExtractionResult>();
        foreach (var result in JetApi.Testing.ResponseExtractor.Run(response, extractions, Resolve))
        {
            var error = result.IsSuccess ? await StoreAsync(result.Extraction, result.Value!).ConfigureAwait(true) : null;
            stored.Add(error is null ? result : result with { Value = null, Error = error });
        }

        Tests.ShowResults(checkResults, stored);
        TestRun = new TestRunViewModel(checkResults, stored);
    }

    // Puts an extracted value where the extraction says; returns why it could not, or null.
    private Task<string?> StoreAsync(Extraction extraction, string value) => VariableWriter.StoreExtractionAsync(Host.Session, CollectionKey, extraction, value);

    /// <summary>Variables visible to this tab, for highlighting and hover hints in editors.</summary>
    [ObservableProperty]
    public partial VariableContext Variables { get; private set; }

    /// <summary>The workspace.</summary>
    protected IRequestHost Host { get; }

    /// <summary>Owning collection.</summary>
    public string CollectionKey { get; private set; }

    /// <summary>Path in the collection; <c>null</c> when the request is not saved (e.g. opened from history).</summary>
    public IReadOnlyList<string>? Path { get; private set; }

    /// <summary>The request key.</summary>
    protected string Key { get; private set; }

    /// <summary>Markdown description, kept as is.</summary>
    protected string? Description { get; }

    /// <summary>Postman scripts kept from an import: saved with the request as they are, never run (ADR-0010).</summary>
    protected ScriptSet Scripts { get; }

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

    /// <summary>Takes over the saved state of <paramref name="previous"/>, which this tab replaces, so unsaved changes still show.</summary>
    internal void ContinueFrom(RequestTabViewModel previous)
    {
        _savedYaml = previous._savedYaml;
        UpdateDirty();
    }

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

        // Results of an earlier send must not look like this one's.
        TestRun = null;
        Tests.ShowResults([], []);
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

    /// <summary>Follows the saved request after it was moved in the tree; unsaved edits stay in the tab.</summary>
    public void Relocate(string collectionKey, IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        CollectionKey = collectionKey;
        Path = path;
        Key = path[^1];
        OnPropertyChanged(nameof(Identity));
        OnContextChanged();
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

    /// <summary>
    /// Where an address with variables really goes: defined variables are replaced by their values (secrets stay
    /// masked); dynamic variables (new on every send) and undefined ones stay as written.
    /// </summary>
    /// <returns>The preview, or an empty one when <paramref name="template"/> has no variables.</returns>
    protected AddressPreview BuildPreview(string? template)
    {
        var placeholders = VariableResolver.FindPlaceholders(template);
        if (placeholders.Count == 0)
        {
            return AddressPreview.None;
        }

        var text = new System.Text.StringBuilder();
        var undefined = 0;
        var at = 0;
        foreach (var placeholder in placeholders)
        {
            text.Append(template, at, placeholder.Start - at);
            var hint = Variables.Describe(placeholder.Name);
            if (!hint.IsDefined)
            {
                undefined++;
            }

            text.Append(hint.IsDefined && hint.Source is not null ? hint.Value : template!.Substring(placeholder.Start, placeholder.Length));
            at = placeholder.End;
        }

        text.Append(template, at, template!.Length - at);
        return new AddressPreview(text.ToString(), undefined == 0 ? string.Empty : Loc.Format("Req_UndefinedCount", undefined));
    }

    private VariableContext CreateVariableContext()
    {
        var resolver = RequestRunner.CreateResolver(Host.Session, CollectionKey);
        return new VariableContext(
            name =>
            {
                var lookup = resolver.Lookup(name);
                var placeholder = "{{" + name + "}}";
                return lookup is null
                    ? new VariableHint(placeholder, string.Empty, Loc.Get("Req_VariableUndefined"), false)
                    : new VariableHint(placeholder, lookup.IsSecret ? "••••••••" : lookup.Value, DescribeSource(lookup.Source), true) { Source = lookup.Source, IsSecret = lookup.IsSecret };
            },
            new TabVariableStore(Host.Session, CollectionKey));
    }

    // Saves variables edited from the request editors (hover card, "set as variable").
    private sealed class TabVariableStore(WorkspaceSession session, string collectionKey) : IVariableStore
    {
        public IReadOnlyList<VariableScopeOption> Scopes
        {
            get
            {
                var scopes = new List<VariableScopeOption>();
                if (session.ActiveEnvironment is { } environment)
                {
                    scopes.Add(new(VariableSource.Environment, Loc.Format("Var_ScopeEnvironment", environment.Name)));
                }

                scopes.Add(new(VariableSource.Collection, Loc.Format("Var_ScopeCollection", session.Workspace.FindCollection(collectionKey)?.Name ?? collectionKey)));
                scopes.Add(new(VariableSource.Global, Loc.Get("Var_ScopeGlobal")));
                return scopes;
            }
        }

        public Variable? Find(VariableSource scope, string name) => VariableWriter.Find(session, collectionKey, scope, name);

        public string ShortSource(VariableSource? source) => source switch
        {
            VariableSource.Environment => session.ActiveEnvironment?.Name ?? string.Empty,
            VariableSource.Collection => session.Workspace.FindCollection(collectionKey)?.Name ?? collectionKey,
            VariableSource.Global => Loc.Get("Var_SourceGlobal"),
            _ => string.Empty,
        };

        public IReadOnlyList<string> Names() =>
        [
            .. (session.ActiveEnvironment?.Variables ?? []).Concat(session.Workspace.FindCollection(collectionKey)?.Variables ?? []).Concat(session.Workspace.GlobalVariables)
                .Where(v => v.IsEnabled && VariableResolver.IsValidVariableName(v.Name))
                .Select(v => v.Name)
                .Distinct(StringComparer.Ordinal),
        ];

        public async Task<string?> SaveAsync(VariableSource scope, string name, string value)
        {
            try
            {
                await VariableWriter.SetAsync(session, collectionKey, scope, name, value).ConfigureAwait(true);
                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or WorkspaceStorageException or InvalidOperationException)
            {
                return Loc.Format("Error_Save", exception.Message);
            }
        }
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
