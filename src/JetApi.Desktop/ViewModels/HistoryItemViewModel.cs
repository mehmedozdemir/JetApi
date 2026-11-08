using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.ViewModels;

/// <summary>A row of the history list.</summary>
public sealed class HistoryItemViewModel : ViewModelBase
{
    /// <summary>Creates the row.</summary>
    public HistoryItemViewModel(HistoryEntry entry)
    {
        Entry = entry;
    }

    /// <summary>The entry.</summary>
    public HistoryEntry Entry { get; }

    /// <summary>Method or gRPC.</summary>
    public string Badge => Entry.IsGrpc ? "gRPC" : Entry.Summary.Split(' ', 2)[0];

    /// <summary>Request name.</summary>
    public string Name => Entry.Name;

    /// <summary>URL or service/method.</summary>
    public string Detail => Entry.IsGrpc ? Entry.Summary : (Entry.Summary.Split(' ', 2) is [_, var url] ? url : Entry.Summary);

    /// <summary>"200 OK · 148 ms · 14:21".</summary>
    public string Meta => $"{Entry.Status} · {Formatting.Duration(TimeSpan.FromMilliseconds(Entry.DurationMs))} · {Entry.At.ToLocalTime().ToString("g", Loc.Instance.Culture)}";
}
