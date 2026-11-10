using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Desktop.Localization;

namespace JetApi.Desktop.ViewModels;

/// <summary>A result row of the command palette.</summary>
public sealed partial class PaletteItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Creates a row.</summary>
    public PaletteItemViewModel(string group, string badge, string label, string detail, string shortcut, Func<Task> execute)
    {
        Group = group;
        Badge = badge;
        Label = label;
        Detail = detail;
        Shortcut = shortcut;
        Execute = execute;
    }

    /// <summary>A command row with an icon (an <c>Icon.*</c> resource key).</summary>
    public static PaletteItemViewModel Command(string group, string iconKey, string label, string detail, string shortcut, Func<Task> execute) =>
        new(group, "›", label, detail, shortcut, execute) { IconKey = iconKey };

    /// <summary>Icon of a command row, or <c>null</c>.</summary>
    public string? IconKey { get; init; }

    /// <summary>Group heading.</summary>
    public string Group { get; }

    /// <summary>Method, gRPC or a symbol.</summary>
    public string Badge { get; }

    /// <summary>Main text.</summary>
    public string Label { get; }

    /// <summary>Secondary text.</summary>
    public string Detail { get; }

    /// <summary>Keyboard shortcut, if any.</summary>
    public string Shortcut { get; }

    /// <summary>The action.</summary>
    public Func<Task> Execute { get; }

    /// <summary>Runs the item from the palette (closes it first).</summary>
    public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand? RunCommand { get; internal set; }

    /// <summary>Badge is a request method.</summary>
    public bool IsRequest => Badge.Length > 1;
}

/// <summary>Searches requests, environments and commands (Ctrl+K).</summary>
public sealed partial class CommandPaletteViewModel : ViewModelBase
{
    private const int MaxResults = 60;

    private readonly IReadOnlyList<PaletteItemViewModel> _all;
    private readonly Action _close;

    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>Creates the palette over <paramref name="items"/>.</summary>
    public CommandPaletteViewModel(IReadOnlyList<PaletteItemViewModel> items, Action close)
    {
        _all = items;
        _close = close;
        foreach (var item in items)
        {
            item.RunCommand = new AsyncRelayCommand(() => RunAsync(item));
        }

        Filter();
    }

    /// <summary>Matching items, grouped in order.</summary>
    public ObservableCollection<PaletteItemViewModel> Results { get; } = [];

    /// <summary>The highlighted item.</summary>
    public PaletteItemViewModel? Selected => Results.FirstOrDefault(r => r.IsSelected);

    /// <summary>Nothing matches.</summary>
    public bool IsEmpty => Results.Count == 0;

    /// <summary>Moves the highlight.</summary>
    [RelayCommand]
    private void Move(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = Results.IndexOf(Selected!);
        Select(Results[(index + delta + Results.Count) % Results.Count]);
    }

    /// <summary>Runs the highlighted item.</summary>
    [RelayCommand]
    private Task ExecuteSelectedAsync() => Selected is { } item ? RunAsync(item) : Task.CompletedTask;

    /// <summary>Runs an item.</summary>
    [RelayCommand]
    private async Task RunAsync(PaletteItemViewModel item)
    {
        _close();
        await item.Execute().ConfigureAwait(true);
    }

    /// <summary>Closes the palette.</summary>
    [RelayCommand]
    private void Close() => _close();

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Query))
        {
            Filter();
        }
    }

    private void Filter()
    {
        var query = Query.Trim();
        var commandsOnly = query.StartsWith('>');
        if (commandsOnly)
        {
            query = query[1..].Trim();
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var commandsGroup = Loc.Get("Palette_Commands");

        // Case rules of the interface language, not of the machine: "install" finds "Install" on a Turkish Windows too.
        var compare = Loc.Instance.Culture.CompareInfo;
        Results.Clear();
        foreach (var item in _all.Where(i => (!commandsOnly || i.Group == commandsGroup)
            && terms.All(t => compare.IndexOf(i.Label + " " + i.Detail + " " + i.Badge, t, System.Globalization.CompareOptions.IgnoreCase) >= 0)).Take(MaxResults))
        {
            item.IsSelected = false;
            Results.Add(item);
        }

        if (Results.Count > 0)
        {
            Select(Results[0]);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Selected));
    }

    private void Select(PaletteItemViewModel item)
    {
        foreach (var result in Results)
        {
            result.IsSelected = ReferenceEquals(result, item);
        }

        OnPropertyChanged(nameof(Selected));
    }
}
