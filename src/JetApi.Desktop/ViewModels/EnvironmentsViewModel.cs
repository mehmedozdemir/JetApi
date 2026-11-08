using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Core.Model;
using JetApi.Core.Variables;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.ViewModels;

/// <summary>Kinds of variable scopes shown in the environment editor.</summary>
public enum ScopeKind
{
    /// <summary>An environment.</summary>
    Environment,

    /// <summary>Workspace globals.</summary>
    Global,

    /// <summary>A collection's variables.</summary>
    Collection,
}

/// <summary>An entry of the scope list.</summary>
public sealed class ScopeItemViewModel : ViewModelBase
{
    /// <summary>Creates the entry.</summary>
    public ScopeItemViewModel(ScopeKind kind, string key, string name, bool isActive, int count)
    {
        Kind = kind;
        Key = key;
        Name = name;
        IsActive = isActive;
        Count = count;
    }

    /// <summary>Scope kind.</summary>
    public ScopeKind Kind { get; }

    /// <summary>Environment or collection key; empty for globals.</summary>
    public string Key { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>The active environment.</summary>
    public bool IsActive { get; }

    /// <summary>Number of variables.</summary>
    public int Count { get; }

    /// <summary>Is an environment.</summary>
    public bool IsEnvironment => Kind == ScopeKind.Environment;

    /// <summary>Selects this scope in the editor.</summary>
    public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand? SelectCommand { get; internal set; }

    /// <summary>Looks like a production environment (gets the Git warning).</summary>
    public bool IsProduction => IsEnvironment && (Key.Contains("prod", StringComparison.OrdinalIgnoreCase) || Name.Contains("prod", StringComparison.OrdinalIgnoreCase));
}

/// <summary>An editable variable row.</summary>
public sealed partial class VariableRowViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _isSecret;

    [ObservableProperty]
    private string _resolved = string.Empty;

    [ObservableProperty]
    private bool _isRevealed;

    /// <summary>The empty row at the end.</summary>
    public bool IsPlaceholder => Name.Length == 0 && Value.Length == 0;

    /// <summary>Removes this row.</summary>
    public CommunityToolkit.Mvvm.Input.IRelayCommand? RemoveCommand { get; internal set; }

    /// <summary>Value as shown: masked when secret and not revealed.</summary>
    public bool IsMasked => IsSecret && !IsRevealed;

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsSecret) or nameof(IsRevealed))
        {
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(IsMasked)));
        }
    }
}

/// <summary>The environment and variable editor.</summary>
public sealed partial class EnvironmentsViewModel : ViewModelBase
{
    private readonly WorkspaceSession _session;
    private readonly IDialogService _dialogs;
    private readonly Action _close;
    private bool _loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(FileName), nameof(ShowProductionWarning), nameof(CanMakeActive), nameof(IsEnvironmentScope))]
    private ScopeItemViewModel? _selectedScope;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevealLabel))]
    private bool _revealSecrets;

    [ObservableProperty]
    private bool _isDirty;

    /// <summary>Creates the editor.</summary>
    public EnvironmentsViewModel(WorkspaceSession session, IDialogService dialogs, Action close)
    {
        _session = session;
        _dialogs = dialogs;
        _close = close;
        Refresh(session.ActiveEnvironmentKey is { } key ? (ScopeKind.Environment, key) : null);
    }

    /// <summary>Environments.</summary>
    public ObservableCollection<ScopeItemViewModel> Environments { get; } = [];

    /// <summary>Globals and collections.</summary>
    public ObservableCollection<ScopeItemViewModel> OtherScopes { get; } = [];

    /// <summary>Variables of the selected scope, plus an empty row.</summary>
    public ObservableCollection<VariableRowViewModel> Rows { get; } = [];

    /// <summary>Heading.</summary>
    public string Title => SelectedScope?.Name ?? string.Empty;

    /// <summary>File holding the scope.</summary>
    public string FileName => SelectedScope switch
    {
        { Kind: ScopeKind.Environment } s => $"environments/{s.Key}.yaml",
        { Kind: ScopeKind.Collection } s => $"collections/{s.Key}/collection.yaml",
        { Kind: ScopeKind.Global } => "jetapi.yaml",
        _ => string.Empty,
    };

    /// <summary>Show the "this file goes to Git" warning.</summary>
    public bool ShowProductionWarning => SelectedScope?.IsProduction ?? false;

    /// <summary>The selected scope is an environment.</summary>
    public bool IsEnvironmentScope => SelectedScope?.IsEnvironment ?? false;

    /// <summary>The selected environment can be made active.</summary>
    public bool CanMakeActive => SelectedScope is { IsEnvironment: true, IsActive: false };

    /// <summary>Reveal button label.</summary>
    public string RevealLabel => Loc.Get(RevealSecrets ? "Env_Hide" : "Env_Reveal");

    /// <summary>No environments exist.</summary>
    public bool HasNoEnvironments => Environments.Count == 0;

    /// <summary>Selects a scope.</summary>
    [RelayCommand]
    private async Task SelectAsync(ScopeItemViewModel scope)
    {
        if (IsDirty)
        {
            await SaveAsync().ConfigureAwait(true);
        }

        SelectedScope = scope;
    }

    /// <summary>Closes the editor (saving pending edits).</summary>
    [RelayCommand]
    private async Task CloseAsync()
    {
        if (IsDirty)
        {
            await SaveAsync().ConfigureAwait(true);
        }

        _close();
    }

    /// <summary>Saves the edited scope.</summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        if (SelectedScope is not { } scope)
        {
            return;
        }

        var variables = Rows.Where(r => !r.IsPlaceholder).Select(r => new Variable(r.Name.Trim(), r.Value, r.IsEnabled, r.IsSecret)).ToList();
        try
        {
            switch (scope.Kind)
            {
                case ScopeKind.Environment:
                    var environment = _session.Workspace.FindEnvironment(scope.Key)!;
                    await _session.SaveEnvironmentAsync(environment with { Variables = variables }).ConfigureAwait(true);
                    break;
                case ScopeKind.Global:
                    await _session.SaveGlobalsAsync(variables).ConfigureAwait(true);
                    break;
                case ScopeKind.Collection:
                    var collection = _session.Workspace.FindCollection(scope.Key)!;
                    await _session.SaveCollectionAsync(collection with { Variables = variables }).ConfigureAwait(true);
                    break;
            }

            IsDirty = false;
            Refresh((scope.Kind, scope.Key));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(Loc.Format("Error_Save", exception.Message)).ConfigureAwait(true);
        }
    }

    /// <summary>Makes the selected environment active.</summary>
    [RelayCommand]
    private void MakeActive()
    {
        if (SelectedScope is { IsEnvironment: true } scope)
        {
            _session.SetActiveEnvironment(scope.Key);
            Refresh((scope.Kind, scope.Key));
        }
    }

    /// <summary>Creates an environment.</summary>
    [RelayCommand]
    private async Task AddEnvironmentAsync()
    {
        var name = await _dialogs.PromptTextAsync(Loc.Get("Env_New"), Loc.Get("Env_Name"), Loc.Get("Prompt_NewEnvironmentName")).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(name))
        {
            var key = await _session.AddEnvironmentAsync(name.Trim()).ConfigureAwait(true);
            Refresh((ScopeKind.Environment, key));
        }
    }

    /// <summary>Copies the selected environment with all its variables.</summary>
    [RelayCommand]
    private async Task DuplicateAsync()
    {
        if (SelectedScope is not { IsEnvironment: true } scope || _session.Workspace.FindEnvironment(scope.Key) is not { } source)
        {
            return;
        }

        var key = await _session.AddEnvironmentAsync(Loc.Format("Copy_Suffix", source.Name), source.Variables).ConfigureAwait(true);
        Refresh((ScopeKind.Environment, key));
    }

    /// <summary>Deletes the selected environment.</summary>
    [RelayCommand]
    private async Task DeleteEnvironmentAsync()
    {
        if (SelectedScope is not { IsEnvironment: true } scope)
        {
            return;
        }

        if (await _dialogs.ConfirmAsync(Loc.Get("Env_Delete"), Loc.Format("Confirm_Delete", scope.Name), Loc.Get("Btn_Delete"), isDestructive: true).ConfigureAwait(true))
        {
            await _session.DeleteEnvironmentAsync(scope.Key).ConfigureAwait(true);
            IsDirty = false;
            Refresh(null);
        }
    }

    /// <summary>Removes a variable row.</summary>
    [RelayCommand]
    private void RemoveRow(VariableRowViewModel row)
    {
        if (!row.IsPlaceholder)
        {
            row.PropertyChanged -= OnRowChanged;
            Rows.Remove(row);
            OnRowsEdited();
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(SelectedScope))
        {
            LoadRows();
        }
        else if (e.PropertyName == nameof(RevealSecrets))
        {
            foreach (var row in Rows)
            {
                row.IsRevealed = RevealSecrets;
            }

            UpdateResolved();
        }
    }

    private void Refresh((ScopeKind Kind, string Key)? select)
    {
        var workspace = _session.Workspace;
        Environments.Clear();
        foreach (var environment in workspace.Environments)
        {
            Environments.Add(WithSelect(new ScopeItemViewModel(ScopeKind.Environment, environment.Key, environment.Name, environment.Key == _session.ActiveEnvironmentKey, environment.Variables.Count)));
        }

        OtherScopes.Clear();
        OtherScopes.Add(WithSelect(new ScopeItemViewModel(ScopeKind.Global, string.Empty, Loc.Get("Env_Global"), false, workspace.GlobalVariables.Count)));
        foreach (var collection in workspace.Collections)
        {
            OtherScopes.Add(WithSelect(new ScopeItemViewModel(ScopeKind.Collection, collection.Key, Loc.Format("Env_CollectionScope", collection.Name), false, collection.Variables.Count)));
        }

        var all = Environments.Concat(OtherScopes).ToList();
        SelectedScope = (select is { } target ? all.FirstOrDefault(s => s.Kind == target.Kind && s.Key == target.Key) : null)
            ?? (Environments.Count > 0 ? Environments[0] : OtherScopes[0]);
        OnPropertyChanged(nameof(HasNoEnvironments));
    }

    private ScopeItemViewModel WithSelect(ScopeItemViewModel scope)
    {
        scope.SelectCommand = new AsyncRelayCommand(() => SelectAsync(scope));
        return scope;
    }

    /// <summary>Shows or masks secret values.</summary>
    [RelayCommand]
    private void ToggleReveal() => RevealSecrets = !RevealSecrets;

    private void LoadRows()
    {
        _loading = true;
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();
        var variables = SelectedScope switch
        {
            { Kind: ScopeKind.Environment } s => _session.Workspace.FindEnvironment(s.Key)?.Variables ?? [],
            { Kind: ScopeKind.Collection } s => _session.Workspace.FindCollection(s.Key)?.Variables ?? [],
            { Kind: ScopeKind.Global } => _session.Workspace.GlobalVariables,
            _ => [],
        };
        foreach (var variable in variables)
        {
            AddRow(new VariableRowViewModel { IsEnabled = variable.IsEnabled, Name = variable.Name, Value = variable.Value, IsSecret = variable.IsSecret, IsRevealed = RevealSecrets });
        }

        AddRow(new VariableRowViewModel { IsRevealed = RevealSecrets });
        _loading = false;
        IsDirty = false;
        UpdateResolved();
    }

    private void AddRow(VariableRowViewModel row)
    {
        row.RemoveCommand = new RelayCommand(() => RemoveRow(row));
        row.PropertyChanged += OnRowChanged;
        Rows.Add(row);
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loading || e.PropertyName is nameof(VariableRowViewModel.Resolved) or nameof(VariableRowViewModel.IsRevealed) or nameof(VariableRowViewModel.IsMasked))
        {
            return;
        }

        if (Rows.Count == 0 || !Rows[^1].IsPlaceholder)
        {
            AddRow(new VariableRowViewModel { IsRevealed = RevealSecrets });
        }

        OnRowsEdited();
    }

    private void OnRowsEdited()
    {
        IsDirty = true;
        UpdateResolved();
    }

    // Shows what each variable resolves to, using the edited values of this scope
    // on top of the workspace (an environment is resolved with globals beneath it).
    private void UpdateResolved()
    {
        var edited = Rows.Where(r => !r.IsPlaceholder).Select(r => new Variable(r.Name.Trim(), r.Value, r.IsEnabled, r.IsSecret)).ToList();
        var layers = new List<VariableLayer>();
        switch (SelectedScope?.Kind)
        {
            case ScopeKind.Environment:
                layers.Add(new VariableLayer(VariableSource.Global, _session.Workspace.GlobalVariables));
                layers.Add(new VariableLayer(VariableSource.Environment, edited));
                break;
            case ScopeKind.Collection:
                layers.Add(new VariableLayer(VariableSource.Global, _session.Workspace.GlobalVariables));
                layers.Add(new VariableLayer(VariableSource.Collection, edited));
                if (_session.ActiveEnvironment is { } active)
                {
                    layers.Add(new VariableLayer(VariableSource.Environment, active.Variables));
                }

                break;
            default:
                layers.Add(new VariableLayer(VariableSource.Global, edited));
                break;
        }

        var resolver = new VariableResolver(layers);
        foreach (var row in Rows)
        {
            if (row.IsPlaceholder)
            {
                row.Resolved = string.Empty;
            }
            else if (!row.IsEnabled)
            {
                row.Resolved = Loc.Get("Env_Disabled");
            }
            else
            {
                var value = resolver.Resolve(row.Value).Value;
                row.Resolved = row.IsSecret && !RevealSecrets && value.Length > 0 ? "••••••••" : value;
            }
        }
    }
}
