using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.ViewModels;

/// <summary>
/// Update state shared by the top bars, the About section and the command palette: checks for a newer release and opens
/// the update dialog. Checks only run when the user asks or has turned on checking at start-up (NFR-6).
/// </summary>
public sealed partial class UpdatesViewModel : ViewModelBase
{
    private readonly IUpdateSource _source;
    private readonly IUpdateInstaller _installer;
    private readonly IDialogService _dialogs;
    private readonly Func<Task<bool>> _prepareToLeave;
    private readonly Action _exit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsAvailable), nameof(LatestVersion), nameof(AvailableTip))]
    private UpdateCheckResult? _result;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    private bool _isChecking;

    /// <summary>Creates the model.</summary>
    /// <param name="source">Where releases are listed.</param>
    /// <param name="installer">Downloads and installs.</param>
    /// <param name="dialogs">Dialogs and the browser.</param>
    /// <param name="current">The running version.</param>
    /// <param name="prepareToLeave">Asks about unsaved requests before restarting; <c>false</c> cancels.</param>
    /// <param name="exit">Closes the app without asking again.</param>
    public UpdatesViewModel(IUpdateSource source, IUpdateInstaller installer, IDialogService dialogs, ReleaseVersion current, Func<Task<bool>> prepareToLeave, Action exit)
    {
        _source = source;
        _installer = installer;
        _dialogs = dialogs;
        Current = current;
        _prepareToLeave = prepareToLeave;
        _exit = exit;
    }

    /// <summary>The running version.</summary>
    public ReleaseVersion Current { get; }

    /// <summary>A newer release was found.</summary>
    public bool IsAvailable => Result?.Status == UpdateCheckStatus.Available;

    /// <summary>The newest release's version, or empty.</summary>
    public string LatestVersion => Result?.Latest?.Version.ToString() ?? string.Empty;

    /// <summary>Tooltip of the top bar button.</summary>
    public string AvailableTip => IsAvailable ? Loc.Format("Update_AvailableTip", LatestVersion) : string.Empty;

    /// <summary>One line for the About section.</summary>
    public string StatusText => IsChecking
        ? Loc.Get("Update_Checking")
        : Result?.Status switch
        {
            UpdateCheckStatus.UpToDate => Loc.Format("Update_UpToDate", Current),
            UpdateCheckStatus.Available => Loc.Format("Update_Available", LatestVersion),
            UpdateCheckStatus.Failed => Result.Error ?? Loc.Get("Update_ErrorResponse"),
            _ => string.Empty,
        };

    /// <summary>The last check failed.</summary>
    public bool HasFailed => Result?.Status == UpdateCheckStatus.Failed;

    /// <summary>Checks now; when a newer release exists the update dialog opens.</summary>
    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        await CheckQuietlyAsync().ConfigureAwait(true);
        if (IsAvailable)
        {
            await ShowAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Checks without opening anything; a newer release only lights up the top bar button.</summary>
    public async Task CheckQuietlyAsync(CancellationToken cancellationToken = default)
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        try
        {
            Result = await _source.CheckAsync(Current, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsChecking = false;
            OnPropertyChanged(nameof(HasFailed));
        }
    }

    /// <summary>Opens the update dialog for the release found.</summary>
    [RelayCommand]
    private Task ShowAsync() => Result is { Status: UpdateCheckStatus.Available } result
        ? _dialogs.ShowUpdateAsync(new UpdateDialogViewModel(result, Current, _installer, _dialogs, _prepareToLeave, _exit))
        : Task.CompletedTask;

    private bool CanCheck() => !IsChecking;
}
