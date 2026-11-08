using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Core.Model;

using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.ViewModels;

/// <summary>One editable row of a key/value table (params, headers, form fields, metadata).</summary>
public sealed partial class KeyValueRowViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isFile;

    /// <summary>True for the always-present empty row at the end that adds a new entry when typed into.</summary>
    public bool IsPlaceholder => Key.Length == 0 && Value.Length == 0 && Description.Length == 0;

    /// <summary>Removes this row from its table.</summary>
    public CommunityToolkit.Mvvm.Input.IRelayCommand? RemoveCommand { get; internal set; }
}

/// <summary>
/// An editable table that always ends with one empty row; typing into it adds a new entry.
/// Empty rows are not saved.
/// </summary>
public sealed partial class KeyValueListViewModel : ViewModelBase
{
    private bool _suspendChanged;

    /// <summary>Creates the list from saved entries.</summary>
    public KeyValueListViewModel(IEnumerable<KeyValueEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            AddRow(new KeyValueRowViewModel { IsEnabled = entry.IsEnabled, Key = entry.Key, Value = entry.Value, Description = entry.Description ?? string.Empty });
        }

        EnsurePlaceholder();
    }

    /// <summary>Raised when any row changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The rows, including the trailing placeholder.</summary>
    public ObservableCollection<KeyValueRowViewModel> Rows { get; } = [];

    /// <summary>Number of enabled, non-empty rows (shown as the tab badge).</summary>
    public int ActiveCount => Rows.Count(r => !r.IsPlaceholder && r.IsEnabled);

    /// <summary>Badge text for the section tab; empty when there are no rows.</summary>
    public string Badge => ActiveCount == 0 ? string.Empty : ActiveCount.ToString(Loc.Instance.Culture);

    /// <summary>The entries to save: every row except empty ones.</summary>
    public IReadOnlyList<KeyValueEntry> ToEntries() =>
        [.. Rows.Where(r => !r.IsPlaceholder).Select(r => new KeyValueEntry(r.Key, r.Value, r.IsEnabled, r.Description.Length == 0 ? null : r.Description))];

    /// <summary>Rows as multipart fields (rows marked as file become file parts).</summary>
    public IReadOnlyList<MultipartField> ToMultipartFields() =>
        [.. Rows.Where(r => !r.IsPlaceholder).Select(r => new MultipartField(r.Key, r.Value, r.IsFile ? MultipartFieldKind.File : MultipartFieldKind.Text, r.IsEnabled))];

    /// <summary>Replaces all rows with multipart fields.</summary>
    public static KeyValueListViewModel FromMultipart(IEnumerable<MultipartField> fields)
    {
        var list = new KeyValueListViewModel([]);
        list._suspendChanged = true;
        list.Rows.Clear();
        foreach (var field in fields)
        {
            list.AddRow(new KeyValueRowViewModel { IsEnabled = field.IsEnabled, Key = field.Name, Value = field.Value, IsFile = field.Kind == MultipartFieldKind.File });
        }

        list.EnsurePlaceholder();
        list._suspendChanged = false;
        return list;
    }

    /// <summary>Removes a row.</summary>
    [RelayCommand]
    private void Remove(KeyValueRowViewModel row)
    {
        if (row.IsPlaceholder)
        {
            return;
        }

        row.PropertyChanged -= OnRowChanged;
        Rows.Remove(row);
        EnsurePlaceholder();
        RaiseChanged();
    }

    private void AddRow(KeyValueRowViewModel row)
    {
        row.RemoveCommand = new RelayCommand(() => Remove(row));
        row.PropertyChanged += OnRowChanged;
        Rows.Add(row);
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        EnsurePlaceholder();
        RaiseChanged();
    }

    private void EnsurePlaceholder()
    {
        if (Rows.Count == 0 || !Rows[^1].IsPlaceholder)
        {
            AddRow(new KeyValueRowViewModel());
        }
    }

    private void RaiseChanged()
    {
        if (_suspendChanged)
        {
            return;
        }

        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(Badge));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
