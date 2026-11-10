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

    /// <summary>Imports Postman exports into the open workspace; does nothing when none is open.</summary>
    Task ImportIntoWorkspaceAsync(IReadOnlyList<string> files);

    /// <summary>Opens the settings window.</summary>
    void OpenSettings();
}

/// <summary>The main window: either the welcome screen or an open workspace.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase, IMainHost, IWorkspaceHost, ISettingsHost, IDisposable
{
    private readonly AppSettingsStore _settingsStore;
    private readonly RequestRunner _runner;
    private readonly Action<AppTheme> _applyTheme;
    private readonly Action<Action> _post;

    [ObservableProperty]
    private ViewModelBase _content;

    [ObservableProperty]
    private double _uiScale = 1;

    private readonly Action<AppSettings>? _applyAppearance;

    /// <summary>Creates the main view model.</summary>
    /// <param name="settingsStore">Where settings live.</param>
    /// <param name="dialogs">Dialogs.</param>
    /// <param name="applyTheme">Applies a theme to the application.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    /// <param name="applyAppearance">Applies fonts and sizes (the app passes <c>App.ApplyAppearance</c>).</param>
    public MainWindowViewModel(AppSettingsStore settingsStore, IDialogService dialogs, Action<AppTheme> applyTheme, Action<Action> post, Action<AppSettings>? applyAppearance = null)
    {
        _settingsStore = settingsStore;
        Dialogs = dialogs;
        _applyTheme = applyTheme;
        _post = post;
        _applyAppearance = applyAppearance;
        _runner = new RequestRunner(new ProtoSchemaCache());
        Settings = settingsStore.Load();
        Loc.Instance.SetLanguage(Settings.Language);
        _content = new WelcomeViewModel(this);
        Apply(Settings);
    }

    /// <inheritdoc />
    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var languageChanged = settings.Language != Settings.Language;
        UpdateSettings(settings);
        if (languageChanged)
        {
            Loc.Instance.SetLanguage(settings.Language);
        }

        Apply(settings);
    }

    /// <inheritdoc cref="IMainHost.OpenSettings" />
    public void OpenSettings() => _ = Dialogs.ShowSettingsAsync(new SettingsViewModel(this, Appearance.InstalledFonts(), Appearance.IsSystemDark()));

    // Theme, fonts, interface size and request defaults; all apply to the running app at once.
    private void Apply(AppSettings settings)
    {
        _applyTheme(settings.Theme);
        _applyAppearance?.Invoke(settings);
        UiScale = Math.Clamp(settings.UiScale, 0.75, 2);
        _runner.DefaultHttpTimeout = TimeSpan.FromSeconds(Math.Clamp(settings.RequestTimeoutSeconds, 1, 3600));
        _runner.UseCookieJar = settings.UseCookieJar;
        Workspace?.OnLayoutChanged();
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
    public async Task ImportIntoWorkspaceAsync(IReadOnlyList<string> files)
    {
        if (Workspace is { } workspace)
        {
            await workspace.ImportFilesAsync(files).ConfigureAwait(true);
        }
    }

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
    public void ToggleResponsePane() => ApplySettings(Settings with
    {
        ResponsePane = Settings.ResponsePane == ResponsePanePosition.Right ? ResponsePanePosition.Bottom : ResponsePanePosition.Right,
    });

    /// <summary>Remembers where the user dragged the divider between request and response.</summary>
    public void SetRequestShare(bool stacked, double share)
    {
        var clamped = Math.Clamp(share, 0.1, 0.9);
        ApplySettings(stacked ? Settings with { RequestShareBottom = clamped } : Settings with { RequestShareRight = clamped });
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
        workspace.RestoreTabs();
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
