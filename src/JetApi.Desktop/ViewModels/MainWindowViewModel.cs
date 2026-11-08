using CommunityToolkit.Mvvm.ComponentModel;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Storage;

namespace JetApi.Desktop.ViewModels;

/// <summary>What the welcome screen needs from the main window.</summary>
public interface IMainHost
{
    /// <summary>Per-user settings.</summary>
    AppSettings Settings { get; }

    /// <summary>Dialogs.</summary>
    IDialogService Dialogs { get; }

    /// <summary>Opens a workspace; offers to create one when the folder is not a workspace.</summary>
    Task OpenWorkspaceAsync(string path, bool offerCreate);

    /// <summary>Creates a workspace in <paramref name="path"/> and opens it.</summary>
    Task CreateWorkspaceAsync(string path);

    /// <summary>Removes a path from the recent list.</summary>
    void RemoveRecent(string path);

    /// <summary>Sets the UI language.</summary>
    void SetLanguage(string? language);
}

/// <summary>The main window: either the welcome screen or an open workspace.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase, IMainHost, IWorkspaceHost, IDisposable
{
    private readonly AppSettingsStore _settingsStore;
    private readonly RequestRunner _runner;
    private readonly Action<AppTheme> _applyTheme;
    private readonly Action<Action> _post;

    [ObservableProperty]
    private ViewModelBase _content;

    /// <summary>Creates the main view model.</summary>
    /// <param name="settingsStore">Where settings live.</param>
    /// <param name="dialogs">Dialogs.</param>
    /// <param name="applyTheme">Applies a theme to the application.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    public MainWindowViewModel(AppSettingsStore settingsStore, IDialogService dialogs, Action<AppTheme> applyTheme, Action<Action> post)
    {
        _settingsStore = settingsStore;
        Dialogs = dialogs;
        _applyTheme = applyTheme;
        _post = post;
        _runner = new RequestRunner(new ProtoSchemaCache());
        Settings = settingsStore.Load();
        Loc.Instance.SetLanguage(Settings.Language);
        _applyTheme(Settings.Theme);
        _content = new WelcomeViewModel(this);
    }

    /// <inheritdoc cref="IMainHost.Settings" />
    public AppSettings Settings { get; private set; }

    /// <inheritdoc />
    public IDialogService Dialogs { get; }

    /// <summary>The open workspace, if any.</summary>
    public WorkspaceViewModel? Workspace => Content as WorkspaceViewModel;

    /// <summary>Window title.</summary>
    public string Title => Workspace is { } workspace ? $"{workspace.WorkspaceName} — JetApi" : "JetApi";

    /// <summary>Opens the most recent workspace if it still exists.</summary>
    public async Task OpenLastWorkspaceAsync()
    {
        if (Settings.RecentWorkspaces.Count > 0 && WorkspaceStore.IsWorkspace(Settings.RecentWorkspaces[0].Path))
        {
            await OpenWorkspaceAsync(Settings.RecentWorkspaces[0].Path, offerCreate: false).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task OpenWorkspaceAsync(string path, bool offerCreate)
    {
        if (!WorkspaceStore.IsWorkspace(path))
        {
            if (offerCreate && await Dialogs.ConfirmAsync(Loc.Get("Welcome_Open"), Loc.Get("Welcome_NotWorkspace"), Loc.Get("Btn_Create"), isDestructive: false).ConfigureAwait(true))
            {
                await CreateWorkspaceAsync(path).ConfigureAwait(true);
            }
            else if (!offerCreate)
            {
                await Dialogs.ShowErrorAsync(Loc.Format("Error_Open", path)).ConfigureAwait(true);
                RemoveRecent(path);
            }

            return;
        }

        try
        {
            var session = await WorkspaceSession.OpenAsync(path).ConfigureAwait(true);
            ShowWorkspace(session);
        }
        catch (Exception exception) when (exception is WorkspaceStorageException or IOException or UnauthorizedAccessException)
        {
            await Dialogs.ShowErrorAsync(Loc.Format("Error_Open", exception.Message)).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task CreateWorkspaceAsync(string path)
    {
        if (WorkspaceStore.IsWorkspace(path))
        {
            await OpenWorkspaceAsync(path, offerCreate: false).ConfigureAwait(true);
            return;
        }

        var name = await Dialogs.PromptTextAsync(Loc.Get("Welcome_Create"), Loc.Get("Welcome_WorkspaceName"), Path.GetFileName(Path.TrimEndingDirectorySeparator(path))).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var session = await WorkspaceSession.CreateAsync(path, name.Trim()).ConfigureAwait(true);
            ShowWorkspace(session);
        }
        catch (Exception exception) when (exception is WorkspaceStorageException or IOException or UnauthorizedAccessException)
        {
            await Dialogs.ShowErrorAsync(Loc.Format("Error_Open", exception.Message)).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public void RemoveRecent(string path) => UpdateSettings(Settings.WithoutRecent(path));

    /// <inheritdoc />
    public void SetLanguage(string? language)
    {
        UpdateSettings(Settings with { Language = language });
        Loc.Instance.SetLanguage(language);
    }

    /// <inheritdoc />
    public void CloseWorkspace()
    {
        Workspace?.Dispose();
        Content = new WelcomeViewModel(this);
        OnPropertyChanged(nameof(Workspace));
        OnPropertyChanged(nameof(Title));
    }

    /// <inheritdoc />
    public void ToggleTheme()
    {
        var next = Settings.Theme switch
        {
            AppTheme.System => AppTheme.Light,
            AppTheme.Light => AppTheme.Dark,
            _ => AppTheme.System,
        };
        UpdateSettings(Settings with { Theme = next });
        _applyTheme(next);
    }

    /// <inheritdoc />
    public void ToggleLanguage() => SetLanguage(Loc.Instance.IsTurkish ? "en" : "tr");

    /// <inheritdoc />
    public void Post(Action action) => _post(action);

    /// <inheritdoc />
    public void Dispose()
    {
        Workspace?.Dispose();
        _runner.Dispose();
    }

    private void ShowWorkspace(WorkspaceSession session)
    {
        Workspace?.Dispose();
        UpdateSettings(Settings.WithRecent(session.Root, session.Workspace.Name, DateTimeOffset.Now));
        var workspace = new WorkspaceViewModel(session, _runner, Dialogs, this);
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.WorkspaceName))
            {
                OnPropertyChanged(nameof(Title));
            }
        };
        Content = workspace;
        OnPropertyChanged(nameof(Workspace));
        OnPropertyChanged(nameof(Title));
    }

    private void UpdateSettings(AppSettings settings)
    {
        Settings = settings;
        try
        {
            _settingsStore.Save(settings);
        }
        catch (IOException)
        {
            // Settings are a convenience; the app keeps working with the in-memory copy.
        }
    }
}
