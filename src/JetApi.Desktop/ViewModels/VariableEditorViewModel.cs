using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Core.Variables;
using JetApi.Desktop.Localization;

namespace JetApi.Desktop.ViewModels;

/// <summary>
/// The card shown over a <c>{{variable}}</c> in a request editor: its value and source, editable in place.
/// Also used to turn selected text into a new variable ("Set as variable").
/// </summary>
public sealed partial class VariableEditorViewModel : ViewModelBase
{
    private readonly IVariableStore? _store;
    private readonly string _savedValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder), nameof(OverwriteWarning))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverwriteWarning))]
    private VariableScopeOption? _selectedScope;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    private VariableEditorViewModel(IVariableStore? store, string name, string value, bool isNew, VariableHint? hint)
    {
        _store = store;
        _name = name;
        _value = value;
        _savedValue = value;
        IsNew = isNew;
        Hint = hint;
        Scopes = store?.Scopes ?? [];
        _selectedScope = hint?.Source is { } source ? Scopes.FirstOrDefault(s => s.Source == source) : (Scopes.Count > 0 ? Scopes[0] : null);
    }

    /// <summary>Raised after a successful save with the variable name.</summary>
    public event EventHandler<string>? Saved;

    /// <summary>Raised when the card should close without saving (Escape).</summary>
    public event EventHandler? Dismissed;

    /// <summary>The card for a variable under the pointer.</summary>
    public static VariableEditorViewModel ForHint(VariableContext context, VariableHint hint)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hint);
        var raw = hint.Source is { } source ? context.Store?.Find(source, hint.Name)?.Value : null;
        return new VariableEditorViewModel(context.Store, hint.Name, raw ?? string.Empty, isNew: false, hint);
    }

    /// <summary>The card that turns <paramref name="selectedText"/> into a new variable.</summary>
    public static VariableEditorViewModel ForSelection(VariableContext context, string selectedText)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(selectedText);
        return new VariableEditorViewModel(context.Store, string.Empty, selectedText, isNew: true, null);
    }

    /// <summary>The hint the card was opened for; <c>null</c> for a new variable.</summary>
    public VariableHint? Hint { get; }

    /// <summary>Creating a variable from selected text: the name is typed in.</summary>
    public bool IsNew { get; }

    /// <summary>"{{name}}".</summary>
    public string Placeholder => "{{" + Name + "}}";

    /// <summary>The variable resolves.</summary>
    public bool IsDefined => Hint?.IsDefined ?? false;

    /// <summary>Not defined yet (hovered placeholder).</summary>
    public bool IsUndefined => Hint is { IsDefined: false };

    /// <summary>"Development" or "not defined".</summary>
    public string Description => Hint?.Source is { } source && Scopes.FirstOrDefault(s => s.Source == source) is { } scope ? scope.Label : Hint?.Description ?? Loc.Get("Var_NewTitle");

    /// <summary>Where it can be saved.</summary>
    public IReadOnlyList<VariableScopeOption> Scopes { get; }

    /// <summary>The value can be edited here (stored variables, or new ones).</summary>
    public bool CanEdit => _store is not null && Scopes.Count > 0 && (Hint is not { IsDefined: true } || Hint.Source is VariableSource.Environment or VariableSource.Collection or VariableSource.Global);

    /// <summary>Read-only (dynamic or runtime value): the value is shown as text.</summary>
    public bool IsReadOnly => !CanEdit;

    /// <summary>Choose where to save (new or undefined variables).</summary>
    public bool ShowScopeChoice => CanEdit && !IsDefined;

    /// <summary>The value is a secret: the box masks it.</summary>
    public bool IsSecret => Hint?.IsSecret ?? false;

    /// <summary>The resolved value, when it differs from what is stored (it references other variables).</summary>
    public string? ResolvedValue => IsDefined && !IsSecret && Hint!.Value != Value && Value.Contains("{{", StringComparison.Ordinal) ? Hint.Value : null;

    /// <summary>The value of a read-only variable.</summary>
    public string ShownValue => Hint?.Value ?? string.Empty;

    /// <summary>The value was edited.</summary>
    public bool IsChanged => Value != _savedValue;

    /// <summary>Keep the card open even when the pointer leaves it.</summary>
    public bool KeepsOpen => IsNew || IsChanged || IsSaving || Error is not null;

    /// <summary>"Save", "Add" or "Create".</summary>
    public string SaveText => Loc.Get(IsNew ? "Var_Create" : IsDefined ? "Btn_Save" : "Btn_Add");

    /// <summary>Warns that saving replaces a variable of the same name in the chosen scope.</summary>
    public string? OverwriteWarning =>
        IsNew && SelectedScope is { } scope && Name.Length > 0 && _store?.Find(scope.Source, Name.Trim()) is not null
            ? Loc.Get("Var_WillOverwrite")
            : null;

    /// <summary>Saves the variable.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var name = Name.Trim();
        if (!VariableResolver.IsValidVariableName(name))
        {
            Error = Loc.Get(name.Length == 0 ? "Var_NameRequired" : "Var_NameInvalid");
            return;
        }

        if (SelectedScope is not { } scope || _store is null)
        {
            return;
        }

        IsSaving = true;
        try
        {
            Error = await _store.SaveAsync(scope.Source, name, Value).ConfigureAwait(true);
        }
        finally
        {
            IsSaving = false;
        }

        if (Error is null)
        {
            Saved?.Invoke(this, name);
        }
    }

    /// <summary>Closes without saving.</summary>
    [RelayCommand]
    private void Dismiss() => Dismissed?.Invoke(this, EventArgs.Empty);

    private bool CanSave() => CanEdit && !IsSaving && (IsNew ? Name.Trim().Length > 0 : !IsDefined || IsChanged);

    partial void OnNameChanged(string value) => Error = null;
}
