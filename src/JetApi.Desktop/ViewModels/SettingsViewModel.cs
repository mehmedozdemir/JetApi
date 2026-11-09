using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.ViewModels;

/// <summary>Sections of the settings window.</summary>
public enum SettingsSection
{
    /// <summary>Theme, fonts, sizes.</summary>
    Appearance,

    /// <summary>Interface language.</summary>
    Language,

    /// <summary>Request defaults.</summary>
    Requests,

    /// <summary>Request history.</summary>
    History,

    /// <summary>Rendering and other rarely changed options.</summary>
    Advanced,
}

/// <summary>Who owns the settings: saves them and applies them to the running app.</summary>
public interface ISettingsHost
{
    /// <summary>Current settings.</summary>
    AppSettings Settings { get; }

    /// <summary>Saves and applies new settings.</summary>
    void ApplySettings(AppSettings settings);
}

/// <summary>A choice in a settings list.</summary>
/// <typeparam name="T">The stored value.</typeparam>
/// <param name="Value">The stored value.</param>
/// <param name="Label">What the user sees.</param>
public sealed record SettingsOption<T>(T Value, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>
/// The settings window. Every change is saved and applied at once (no Save button); "Reset" restores defaults.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private static readonly double[] Scales = [0.9, 1.0, 1.1, 1.25, 1.5];

    private readonly ISettingsHost _host;
    private bool _loading;

    [ObservableProperty]
    private SettingsSection _section;

    [ObservableProperty]
    private SettingsOption<AppTheme>? _theme;

    [ObservableProperty]
    private SettingsOption<ResponsePanePosition>? _responsePane;

    [ObservableProperty]
    private SettingsOption<string?>? _uiFont;

    [ObservableProperty]
    private SettingsOption<double>? _uiScale;

    [ObservableProperty]
    private SettingsOption<string?>? _editorFont;

    [ObservableProperty]
    private decimal _editorFontSize;

    [ObservableProperty]
    private decimal _uiFontSize;

    [ObservableProperty]
    private bool _wrapResponseLines;

    [ObservableProperty]
    private SettingsOption<string?>? _language;

    [ObservableProperty]
    private bool _confirmUndefinedVariables;

    [ObservableProperty]
    private decimal _requestTimeoutSeconds;

    [ObservableProperty]
    private bool _saveHistory;

    [ObservableProperty]
    private bool _useCookieJar;

    [ObservableProperty]
    private SettingsOption<RenderingPreference>? _rendering;

    /// <summary>Creates the view model; <paramref name="installedFonts"/> come from the platform (empty in tests is fine).</summary>
    public SettingsViewModel(ISettingsHost host, IReadOnlyList<string> installedFonts)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(installedFonts);
        _host = host;
        ThemeOptions = [new(AppTheme.System, Loc.Get("Settings_ThemeSystem")), new(AppTheme.Light, Loc.Get("Settings_ThemeLight")), new(AppTheme.Dark, Loc.Get("Settings_ThemeDark"))];
        ResponsePaneOptions = [new(ResponsePanePosition.Right, Loc.Get("Settings_ResponseRight")), new(ResponsePanePosition.Bottom, Loc.Get("Settings_ResponseBottom"))];
        UiFontOptions = [new(null, Loc.Get("Settings_FontBuiltIn")), .. installedFonts.Select(f => new SettingsOption<string?>(f, f))];
        UiScaleOptions = [.. Scales.Select(s => new SettingsOption<double>(s, s.ToString("P0", Loc.Instance.Culture)))];
        EditorFontOptions = [new(null, Loc.Get("Settings_FontDefaultMono")), .. Appearance.InstalledMonoFonts(installedFonts).Select(f => new SettingsOption<string?>(f, f))];
        LanguageOptions = [new(null, Loc.Get("Settings_LanguageSystem")), new("tr", "Türkçe"), new("en", "English")];
        RenderingOptions = [new(RenderingPreference.Software, Loc.Get("Settings_RenderingSoftware")), new(RenderingPreference.Gpu, Loc.Get("Settings_RenderingGpu"))];
        Load(host.Settings);
    }

    /// <summary>Response pane positions.</summary>
    public IReadOnlyList<SettingsOption<ResponsePanePosition>> ResponsePaneOptions { get; }

    /// <summary>Theme choices.</summary>
    public IReadOnlyList<SettingsOption<AppTheme>> ThemeOptions { get; }

    /// <summary>Interface fonts: built-in Inter, then installed fonts.</summary>
    public IReadOnlyList<SettingsOption<string?>> UiFontOptions { get; }

    /// <summary>Interface sizes.</summary>
    public IReadOnlyList<SettingsOption<double>> UiScaleOptions { get; }

    /// <summary>Editor fonts: the default monospace chain, then installed monospace fonts.</summary>
    public IReadOnlyList<SettingsOption<string?>> EditorFontOptions { get; }

    /// <summary>Languages.</summary>
    public IReadOnlyList<SettingsOption<string?>> LanguageOptions { get; }

    /// <summary>Rendering modes.</summary>
    public IReadOnlyList<SettingsOption<RenderingPreference>> RenderingOptions { get; }

    /// <summary>Sample shown under the editor font settings.</summary>
    public string EditorSample { get; } = "{\n  \"id\": 42,\n  \"name\": \"Ayşe Yılmaz\",\n  \"url\": \"{{baseUrl}}/users/42\"\n}";

    /// <summary>The rendering choice differs from what this run uses; a restart applies it.</summary>
    public bool RenderingNeedsRestart => Rendering?.Value != StartupRendering;

    /// <summary>The rendering mode this process started with.</summary>
    public static RenderingPreference StartupRendering { get; set; } = RenderingPreference.Software;

    /// <summary>Restores every preference (not the recent list or language) to its default.</summary>
    [RelayCommand]
    private void Reset()
    {
        _host.ApplySettings(_host.Settings.WithDefaultPreferences());
        Load(_host.Settings);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is nameof(Section) or nameof(RenderingNeedsRestart) or null or "")
        {
            return;
        }

        var current = _host.Settings;
        _host.ApplySettings(current with
        {
            Theme = Theme?.Value ?? current.Theme,
            ResponsePane = ResponsePane?.Value ?? current.ResponsePane,
            UiFontFamily = UiFont is null ? current.UiFontFamily : UiFont.Value,
            UiScale = UiScale?.Value ?? current.UiScale,
            EditorFontFamily = EditorFont is null ? current.EditorFontFamily : EditorFont.Value,
            EditorFontSize = (double)Math.Clamp(EditorFontSize, 8, 32),
            UiFontSize = (double)Math.Clamp(UiFontSize, (decimal)Appearance.MinUiFontSize, (decimal)Appearance.MaxUiFontSize),
            WrapResponseLines = WrapResponseLines,
            Language = Language is null ? current.Language : Language.Value,
            ConfirmUndefinedVariables = ConfirmUndefinedVariables,
            RequestTimeoutSeconds = (int)Math.Clamp(RequestTimeoutSeconds, 1, 3600),
            SaveHistory = SaveHistory,
            UseCookieJar = UseCookieJar,
            Rendering = Rendering?.Value ?? current.Rendering,
        });
        if (e.PropertyName == nameof(Rendering))
        {
            OnPropertyChanged(nameof(RenderingNeedsRestart));
        }
    }

    private void Load(AppSettings settings)
    {
        _loading = true;
        Theme = ThemeOptions.First(o => o.Value == settings.Theme);
        ResponsePane = ResponsePaneOptions.First(o => o.Value == settings.ResponsePane);
        UiFont = UiFontOptions.FirstOrDefault(o => o.Value == settings.UiFontFamily) ?? AddFont(UiFontOptions, settings.UiFontFamily);
        UiScale = UiScaleOptions.MinBy(o => Math.Abs(o.Value - settings.UiScale));
        EditorFont = EditorFontOptions.FirstOrDefault(o => o.Value == settings.EditorFontFamily) ?? AddFont(EditorFontOptions, settings.EditorFontFamily);
        EditorFontSize = (decimal)settings.EditorFontSize;
        UiFontSize = (decimal)settings.UiFontSize;
        WrapResponseLines = settings.WrapResponseLines;
        Language = LanguageOptions.FirstOrDefault(o => o.Value == settings.Language) ?? LanguageOptions[0];
        ConfirmUndefinedVariables = settings.ConfirmUndefinedVariables;
        RequestTimeoutSeconds = settings.RequestTimeoutSeconds;
        SaveHistory = settings.SaveHistory;
        UseCookieJar = settings.UseCookieJar;
        Rendering = RenderingOptions.First(o => o.Value == settings.Rendering);
        _loading = false;
        OnPropertyChanged(nameof(RenderingNeedsRestart));
    }

    // A font saved earlier but not installed any more still shows as the current choice.
    private static SettingsOption<string?> AddFont(IReadOnlyList<SettingsOption<string?>> options, string? font) =>
        font is null ? options[0] : new SettingsOption<string?>(font, font);
}
