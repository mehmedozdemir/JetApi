using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.ViewModels;

/// <summary>Where the update dialog is.</summary>
public enum UpdateStage
{
    /// <summary>Showing what is new; nothing downloaded yet.</summary>
    Ready,

    /// <summary>Downloading and verifying.</summary>
    Downloading,

    /// <summary>Downloaded and verified; a restart installs it.</summary>
    Downloaded,

    /// <summary>Replacing files and restarting.</summary>
    Installing,
}

/// <summary>
/// The update dialog: what is new since the running version, then download (checked against <c>SHA256SUMS.txt</c>) and
/// restart to install. Copies that cannot update themselves open the release page instead.
/// </summary>
public sealed partial class UpdateDialogViewModel : ViewModelBase
{
    private readonly IUpdateInstaller _installer;
    private readonly IDialogService _dialogs;
    private readonly Func<Task<bool>> _prepareToLeave;
    private readonly Action _exit;
    private CancellationTokenSource? _download;
    private UpdateDownload? _downloaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsDownloading), nameof(IsDownloaded), nameof(IsBusy), nameof(ShowsDownload), nameof(ShowsReleaseLink))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(RestartCommand))]
    private UpdateStage _stage;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isProgressKnown;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <summary>Creates the dialog's model.</summary>
    public UpdateDialogViewModel(UpdateCheckResult result, ReleaseVersion current, IUpdateInstaller installer, IDialogService dialogs, Func<Task<bool>> prepareToLeave, Action exit)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(current);
        Release = result.Latest ?? throw new ArgumentException("No newer release.", nameof(result));
        _installer = installer;
        _dialogs = dialogs;
        _prepareToLeave = prepareToLeave;
        _exit = exit;
        Current = current;
        Sections = [.. result.Newer.Select(ReleaseNotesSection.From)];
        var size = Release.Asset(installer.Support.RuntimeId is { } runtime ? ReleaseInfo.PackageName(Release.Version, runtime) : string.Empty)?.Size;
        Title = Loc.Format("Update_Title", Release.Version);
        Subtitle = size is > 0
            ? Loc.Format("Update_SubtitleSize", current, FormatSize(size.Value))
            : Loc.Format("Update_Subtitle", current);
    }

    /// <summary>The newest release.</summary>
    public ReleaseInfo Release { get; }

    /// <summary>The running version.</summary>
    public ReleaseVersion Current { get; }

    /// <summary>"JetApi 0.4.0 is available".</summary>
    public string Title { get; }

    /// <summary>"You have 0.3.0 · 48 MB download".</summary>
    public string Subtitle { get; }

    /// <summary>Notes of every version newer than the running one, newest first.</summary>
    public IReadOnlyList<ReleaseNotesSection> Sections { get; }

    /// <summary>This copy installs updates itself.</summary>
    public bool CanInstall => _installer.Support.CanInstall;

    /// <summary>Why this copy does not install updates itself.</summary>
    public string ManualReason => _installer.Support.Reason ?? string.Empty;

    /// <summary>The notes are written in Turkish; English users are told so.</summary>
    public bool ShowsLanguageNote => !Loc.Instance.IsTurkish;

    /// <summary>Nothing started yet.</summary>
    public bool IsReady => Stage == UpdateStage.Ready;

    /// <summary>Downloading.</summary>
    public bool IsDownloading => Stage == UpdateStage.Downloading;

    /// <summary>Downloaded, waiting for a restart.</summary>
    public bool IsDownloaded => Stage == UpdateStage.Downloaded;

    /// <summary>Downloading or installing.</summary>
    public bool IsBusy => Stage is UpdateStage.Downloading or UpdateStage.Installing;

    /// <summary>The download button shows (this copy installs updates and nothing started yet).</summary>
    public bool ShowsDownload => IsReady && CanInstall;

    /// <summary>The release page link shows in the footer (the main button opens it when this copy cannot install).</summary>
    public bool ShowsReleaseLink => CanInstall && !IsBusy;

    /// <summary>The last step failed.</summary>
    public bool HasError => Error is not null;

    /// <summary>Downloads and verifies the package.</summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        Error = null;
        Stage = UpdateStage.Downloading;
        IsProgressKnown = false;
        Progress = 0;
        ProgressText = Loc.Get("Update_Starting");
        using var download = new CancellationTokenSource();
        _download = download;
        var progress = new Progress<DownloadProgress>(Report);
        try
        {
            var result = await _installer.DownloadAsync(Release, progress, download.Token).ConfigureAwait(true);
            if (result.Succeeded)
            {
                _downloaded = result;
                Stage = UpdateStage.Downloaded;
            }
            else
            {
                Error = result.Error;
                Stage = UpdateStage.Ready;
            }
        }
        catch (OperationCanceledException)
        {
            Stage = UpdateStage.Ready;
        }
        finally
        {
            _download = null;
        }
    }

    /// <summary>Stops the download.</summary>
    [RelayCommand]
    private void CancelDownload() => _download?.Cancel();

    /// <summary>Asks about unsaved requests, installs the downloaded files and restarts.</summary>
    [RelayCommand(CanExecute = nameof(CanRestart))]
    private async Task RestartAsync()
    {
        if (_downloaded is null || !await _prepareToLeave().ConfigureAwait(true))
        {
            return;
        }

        Error = null;
        Stage = UpdateStage.Installing;
        if (_installer.Install(_downloaded) is { } failure)
        {
            Error = failure;
            Stage = UpdateStage.Downloaded;
            return;
        }

        try
        {
            _installer.StartNewVersion(Current);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // The files are already replaced; the next start runs the new version.
            Error = Loc.Format("Update_ErrorRestart", exception.Message);
            return;
        }

        _exit();
    }

    /// <summary>Opens the release on GitHub.</summary>
    [RelayCommand]
    private Task OpenReleasePageAsync() => _dialogs.OpenBrowserAsync(Release.PageUrl);

    /// <summary>"12.3 MB" in the interface language.</summary>
    public static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? (bytes / 1024d / 1024d).ToString("0.0", Loc.Instance.Culture) + " MB"
            : Math.Max(1, bytes / 1024).ToString(Loc.Instance.Culture) + " KB";

    private bool CanDownload() => Stage == UpdateStage.Ready && CanInstall;

    private bool CanRestart() => Stage == UpdateStage.Downloaded;

    private void Report(DownloadProgress value)
    {
        IsProgressKnown = value.Total is > 0;
        Progress = value.Total is > 0 ? Math.Clamp(value.Received * 100d / value.Total.Value, 0, 100) : 0;
        ProgressText = value.Total is > 0
            ? Loc.Format("Update_Progress", FormatSize(value.Received), FormatSize(value.Total.Value))
            : FormatSize(value.Received);
    }
}
