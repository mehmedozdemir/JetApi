using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.ViewModels;

/// <summary>The About section of the settings window: version, platform, links, release notes and updates.</summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly Func<Task> _showReleaseNotes;

    [ObservableProperty]
    private bool _copied;

    /// <summary>Creates the section.</summary>
    /// <param name="info">This copy of JetApi.</param>
    /// <param name="dialogs">Clipboard and browser.</param>
    /// <param name="updates">Update checks; <c>null</c> hides them.</param>
    /// <param name="showReleaseNotes">Opens the release notes window.</param>
    public AboutViewModel(AppInfo info, IDialogService dialogs, UpdatesViewModel? updates, Func<Task> showReleaseNotes)
    {
        Info = info;
        _dialogs = dialogs;
        Updates = updates;
        _showReleaseNotes = showReleaseNotes;
    }

    /// <summary>This copy of JetApi.</summary>
    public AppInfo Info { get; }

    /// <summary>Update checks.</summary>
    public UpdatesViewModel? Updates { get; }

    /// <summary>Update checks are available.</summary>
    public bool HasUpdates => Updates is not null;

    /// <summary>"Version 0.3.0 (129a129)".</summary>
    public string VersionText => Loc.Format("About_Version", Info.DisplayVersion);

    /// <summary>".NET 10.0.1 · Avalonia 12.1.5".</summary>
    public string RuntimeText => $"{Info.Runtime} · Avalonia {Info.AvaloniaVersion}";

    /// <summary>"Microsoft Windows 10.0.26300 · X64".</summary>
    public string OsText => $"{Info.Os} · {Info.Architecture}";

    /// <summary>Copies version and platform details for a bug report.</summary>
    [RelayCommand]
    private async Task CopyInfoAsync()
    {
        await _dialogs.CopyToClipboardAsync(Info.ToReport()).ConfigureAwait(true);
        Copied = true;
    }

    /// <summary>Opens the release notes.</summary>
    [RelayCommand]
    private Task ShowReleaseNotesAsync() => _showReleaseNotes();

    /// <summary>Opens the GitHub page.</summary>
    [RelayCommand]
    private Task OpenRepositoryAsync() => _dialogs.OpenBrowserAsync(AppInfo.RepositoryUrl);

    /// <summary>Opens the license.</summary>
    [RelayCommand]
    private Task OpenLicenseAsync() => _dialogs.OpenBrowserAsync(AppInfo.LicenseUrl);

    /// <summary>Opens the issue tracker.</summary>
    [RelayCommand]
    private Task ReportIssueAsync() => _dialogs.OpenBrowserAsync(AppInfo.IssuesUrl);
}
