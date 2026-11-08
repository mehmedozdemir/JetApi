using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.ViewModels;

/// <summary>A recent workspace on the welcome screen.</summary>
public sealed class RecentItemViewModel : ViewModelBase
{
    /// <summary>Creates the item.</summary>
    public RecentItemViewModel(RecentWorkspace workspace)
    {
        Workspace = workspace;
    }

    /// <summary>The recent workspace.</summary>
    public RecentWorkspace Workspace { get; }

    /// <summary>Name.</summary>
    public string Name => Workspace.Name;

    /// <summary>Path.</summary>
    public string Path => Workspace.Path;

    /// <summary>First letter, for the avatar.</summary>
    public string Initial => Workspace.Name.Length == 0 ? "?" : Workspace.Name[..1].ToUpper(Loc.Instance.Culture);

    /// <summary>Opens this workspace.</summary>
    public IAsyncRelayCommand? OpenCommand { get; internal set; }

    /// <summary>Removes this workspace from the list.</summary>
    public IRelayCommand? RemoveCommand { get; internal set; }

    /// <summary>When it was last opened.</summary>
    public string LastOpened => Workspace.LastOpened.ToLocalTime().ToString("g", Loc.Instance.Culture);
}

/// <summary>The screen shown when no workspace is open.</summary>
public sealed partial class WelcomeViewModel : ViewModelBase
{
    private readonly IMainHost _host;

    /// <summary>Creates the screen.</summary>
    public WelcomeViewModel(IMainHost host)
    {
        _host = host;
        Refresh();
    }

    /// <summary>Recent workspaces.</summary>
    public ObservableCollection<RecentItemViewModel> Recent { get; } = [];

    /// <summary>No recent workspaces.</summary>
    public bool HasNoRecent => Recent.Count == 0;

    /// <summary>Turkish selected.</summary>
    public bool IsTurkish => Loc.Instance.IsTurkish;

    /// <summary>Picks and opens a workspace folder.</summary>
    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folder = await _host.Dialogs.PickFolderAsync(Loc.Get("Welcome_PickFolder")).ConfigureAwait(true);
        if (folder is not null)
        {
            await _host.OpenWorkspaceAsync(folder, offerCreate: true).ConfigureAwait(true);
        }
    }

    /// <summary>Picks a folder and creates a workspace in it.</summary>
    [RelayCommand]
    private async Task CreateWorkspaceAsync()
    {
        var folder = await _host.Dialogs.PickFolderAsync(Loc.Get("Welcome_PickEmptyFolder")).ConfigureAwait(true);
        if (folder is not null)
        {
            await _host.CreateWorkspaceAsync(folder).ConfigureAwait(true);
        }
    }

    /// <summary>Opens a recent workspace.</summary>
    [RelayCommand]
    private Task OpenRecentAsync(RecentItemViewModel item) => _host.OpenWorkspaceAsync(item.Path, offerCreate: false);

    /// <summary>Removes a recent workspace from the list.</summary>
    [RelayCommand]
    private void RemoveRecent(RecentItemViewModel item)
    {
        _host.RemoveRecent(item.Path);
        Refresh();
    }

    /// <summary>Sets the language.</summary>
    [RelayCommand]
    private void SetLanguage(string language) => _host.SetLanguage(language);

    /// <summary>Reloads the recent list.</summary>
    public void Refresh()
    {
        Recent.Clear();
        foreach (var workspace in _host.Settings.RecentWorkspaces)
        {
            var item = new RecentItemViewModel(workspace);
            item.OpenCommand = new AsyncRelayCommand(() => OpenRecentAsync(item));
            item.RemoveCommand = new RelayCommand(() => RemoveRecent(item));
            Recent.Add(item);
        }

        OnPropertyChanged(nameof(HasNoRecent));
    }
}
