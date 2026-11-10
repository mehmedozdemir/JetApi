using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace JetApi.Desktop.Services;

/// <summary>
/// Applies appearance settings to the running application through resources, so every open view follows
/// immediately: <c>App.UiFont</c>, the <c>App.FontSize.*</c> type scale, <c>App.MonoFont</c>, <c>App.EditorFontSize</c>
/// and <c>App.WrapResponses</c>, and the color palettes of the light and dark theme (<see cref="ThemePalettes"/>).
/// </summary>
public static class Appearance
{
    /// <summary>The interface font that ships with the app.</summary>
    public const string BuiltInUiFont = "fonts:Inter#Inter, $Default";

    /// <summary>Monospace fonts tried in order when no editor font is chosen.</summary>
    public const string DefaultMonoFonts = "Cascadia Mono, JetBrains Mono, SF Mono, Menlo, Consolas, DejaVu Sans Mono, monospace";

    private static readonly string[] KnownMonoFonts =
    [
        "Cascadia Mono", "Cascadia Code", "JetBrains Mono", "Fira Code", "Fira Mono", "Source Code Pro", "IBM Plex Mono",
        "SF Mono", "Menlo", "Monaco", "Consolas", "Lucida Console", "DejaVu Sans Mono", "Ubuntu Mono", "Liberation Mono", "Courier New",
    ];

    /// <summary>Font families installed on this machine, sorted by name.</summary>
    public static IReadOnlyList<string> InstalledFonts()
    {
        try
        {
            return [.. FontManager.Current.SystemFonts.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (InvalidOperationException)
        {
            // No platform font manager (e.g. some test hosts).
            return [];
        }
    }

    /// <summary>Installed monospace fonts: the well-known ones, plus any whose name says mono or code.</summary>
    public static IReadOnlyList<string> InstalledMonoFonts(IReadOnlyList<string> installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        return [.. installed.Where(name => KnownMonoFonts.Contains(name, StringComparer.OrdinalIgnoreCase)
            || name.Contains("Mono", StringComparison.OrdinalIgnoreCase) || name.Contains(" Code", StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// The interface type scale for a body size: every <c>App.FontSize.*</c> token keeps its distance from the body
    /// size (as in Tokens.axaml at 13), so headings and small text grow and shrink together.
    /// </summary>
    public static IReadOnlyList<(string Key, double Size)> TypeScale(double body)
    {
        var b = Math.Clamp(body, MinUiFontSize, MaxUiFontSize);
        return
        [
            ("ControlContentThemeFontSize", b),
            ("App.FontSize.Display", b + 11),
            ("App.FontSize.Title", b + 5),
            ("App.FontSize.Heading", b + 1),
            ("App.FontSize.Body", b),
            ("App.FontSize.Control", b - 1),
            ("App.FontSize.Mono", b - 1),
            ("App.FontSize.Small", b - 1.5),
            ("App.FontSize.Label", b - 2),
            ("App.FontSize.Caption", b - 2.5),
        ];
    }

    /// <summary>Whether the operating system currently uses a dark theme.</summary>
    public static bool IsSystemDark() =>
        Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark;

    /// <summary>Smallest interface font size offered.</summary>
    public const double MinUiFontSize = 11;

    /// <summary>Largest interface font size offered.</summary>
    public const double MaxUiFontSize = 18;

    /// <summary>Applies fonts, editor size and wrapping from <paramref name="settings"/>.</summary>
    public static void Apply(Application application, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(settings);
        var resources = application.Resources;
        resources["App.UiFont"] = new FontFamily(string.IsNullOrWhiteSpace(settings.UiFontFamily) ? BuiltInUiFont : settings.UiFontFamily + ", " + BuiltInUiFont);
        resources["App.MonoFont"] = new FontFamily(string.IsNullOrWhiteSpace(settings.EditorFontFamily) ? DefaultMonoFonts : settings.EditorFontFamily + ", " + DefaultMonoFonts);
        resources["App.EditorFontSize"] = Math.Clamp(settings.EditorFontSize, 8, 32);
        foreach (var (key, size) in TypeScale(settings.UiFontSize))
        {
            resources[key] = size;
        }

        ApplyPalette(application, ThemeVariant.Light, settings.LightPalette);
        ApplyPalette(application, ThemeVariant.Dark, settings.DarkPalette);
        resources["App.WrapResponses"] = settings.WrapResponseLines;
        resources["App.ResponseBelow"] = settings.ResponsePane == ResponsePanePosition.Bottom;
        resources["App.RequestShareRight"] = Math.Clamp(settings.RequestShareRight, 0.1, 0.9);
        resources["App.RequestShareBottom"] = Math.Clamp(settings.RequestShareBottom, 0.1, 0.9);
    }

    // Fluent's own brushes read their colors once, so a palette also sets the ones the app shows, from its tokens.
    private static readonly (string Token, string[] Keys)[] FluentBrushes =
    [
        ("App.Surface", ["TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "ComboBoxBackground",
            "ComboBoxBackgroundUnfocused", "ComboBoxDropDownBackground", "MenuFlyoutPresenterBackground", "FlyoutPresenterBackground", "ToolTipBackground",
            "CheckBoxCheckBackgroundFillUnchecked", "CheckBoxCheckBackgroundFillUncheckedPointerOver", "RadioButtonOuterEllipseFill",
            "RadioButtonOuterEllipseFillPointerOver"]),
        ("App.Subtle", ["TextControlBackgroundDisabled", "ComboBoxBackgroundDisabled", "ComboBoxBackgroundPressed", "ComboBoxItemBackgroundPressed",
            "MenuFlyoutItemBackgroundPressed", "TextControlButtonBackgroundPressed", "CheckBoxCheckBackgroundFillUncheckedPressed"]),
        ("App.Hover", ["ComboBoxBackgroundPointerOver", "ComboBoxItemBackgroundPointerOver", "MenuFlyoutItemBackgroundPointerOver",
            "TextControlButtonBackgroundPointerOver"]),
        ("App.Selection", ["ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPointerOver", "ComboBoxItemBackgroundSelectedPressed"]),
        ("App.Border", ["ComboBoxDropDownBorderBrush", "MenuFlyoutPresenterBorderBrush", "ToolTipBorderBrush", "FlyoutBorderThemeBrush",
            "TextControlBorderBrushDisabled", "ComboBoxBorderBrushDisabled"]),
        ("App.BorderStrong", ["TextControlBorderBrush", "ComboBoxBorderBrush"]),
        ("App.TextFaint", ["TextControlBorderBrushPointerOver", "ComboBoxBorderBrushPointerOver", "TextControlPlaceholderForeground",
            "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused", "TextControlPlaceholderForegroundDisabled",
            "ComboBoxPlaceHolderForeground", "TextControlForegroundDisabled", "ComboBoxForegroundDisabled", "MenuFlyoutItemForegroundDisabled",
            "MenuFlyoutItemKeyboardAcceleratorTextForeground", "MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver",
            "MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed", "CheckBoxCheckBackgroundStrokeUnchecked",
            "CheckBoxCheckBackgroundStrokeUncheckedPointerOver", "RadioButtonOuterEllipseStroke", "RadioButtonOuterEllipseStrokePointerOver",
            "ScrollBarPanningThumbBackground", "ScrollBarButtonArrowForeground"]),
        ("App.TextMuted", ["ComboBoxDropDownGlyphForeground", "TextControlButtonForeground", "TextControlButtonForegroundPointerOver",
            "ScrollBarThumbFillPointerOver", "ScrollBarButtonArrowForegroundPointerOver"]),
        ("App.Text", ["TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused", "ComboBoxForeground",
            "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed", "ComboBoxItemForeground", "ComboBoxItemForegroundPointerOver",
            "ComboBoxItemForegroundPressed", "ComboBoxItemForegroundSelected", "ComboBoxItemForegroundSelectedPointerOver",
            "ComboBoxItemForegroundSelectedPressed", "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver", "MenuFlyoutItemForegroundPressed",
            "ToolTipForeground", "CheckBoxForegroundUnchecked", "CheckBoxForegroundChecked", "CheckBoxForegroundUncheckedPointerOver",
            "CheckBoxForegroundCheckedPointerOver", "CheckBoxForegroundIndeterminate", "RadioButtonForeground", "RadioButtonForegroundPointerOver",
            "RadioButtonForegroundPressed"]),
        ("App.Accent", ["TextControlBorderBrushFocused", "ComboBoxBackgroundBorderBrushFocused", "SystemControlHighlightAccentBrush",
            "SystemControlForegroundAccentBrush", "SystemControlBackgroundAccentBrush", "RadioButtonOuterEllipseCheckedFill",
            "RadioButtonOuterEllipseCheckedFillPointerOver", "RadioButtonOuterEllipseCheckedStroke", "RadioButtonOuterEllipseCheckedStrokePointerOver"]),
        ("App.AccentForeground", ["RadioButtonCheckGlyphFill", "RadioButtonCheckGlyphFillPointerOver", "RadioButtonCheckGlyphStroke"]),
        ("App.Check", ["CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundFillIndeterminate",
            "CheckBoxCheckBackgroundStrokeIndeterminate", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPointerOver",
            "CheckBoxCheckBackgroundFillCheckedPressed", "CheckBoxCheckBackgroundStrokeCheckedPressed"]),
        ("App.CheckForeground", ["CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver",
            "CheckBoxCheckGlyphForegroundCheckedPressed", "CheckBoxCheckGlyphForegroundIndeterminate"]),
    ];

    private static readonly ConditionalWeakTable<Application, Dictionary<ThemeVariant, string>> AppliedPalettes = [];
    private static ResourceDictionary? _palettes;

    /// <summary>The palettes besides the defaults, loaded on first use (nothing is loaded while the defaults are used).</summary>
    internal static ResourceDictionary Palettes =>
        _palettes ??= (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://JetApi/Themes/Palettes.axaml"));

    /// <summary>
    /// Uses palette <paramref name="id"/> for <paramref name="variant"/>. The defaults come from Tokens.axaml; any other is put
    /// in the application's theme dictionary, which is looked up before Tokens.axaml, so every view follows at once.
    /// </summary>
    public static void ApplyPalette(Application application, ThemeVariant variant, string? id)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(variant);
        var palette = ThemePalettes.Normalize(id, variant == ThemeVariant.Dark);
        var applied = AppliedPalettes.GetOrCreateValue(application);
        if (applied.TryGetValue(variant, out var current) ? current == palette : ThemePalettes.IsDefault(palette))
        {
            return;
        }

        applied[variant] = palette;
        if (ThemePalettes.IsDefault(palette))
        {
            application.Resources.ThemeDictionaries.Remove(variant);
            return;
        }

        var source = (ResourceDictionary)Palettes[palette]!;
        var dictionary = new ResourceDictionary();
        foreach (var (key, value) in source)
        {
            dictionary[key] = value;
        }

        foreach (var (token, keys) in FluentBrushes)
        {
            foreach (var key in keys)
            {
                dictionary[key] = source[token];
            }
        }

        application.Resources.ThemeDictionaries[variant] = dictionary;
    }

    /// <summary>A palette's colors for its preview in the settings.</summary>
    public static PaletteSwatch Swatch(string id)
    {
        var palette = ThemePalettes.All.FirstOrDefault(p => p.Id == id) ?? ThemePalettes.All[0];
        IResourceProvider? source = ThemePalettes.IsDefault(palette.Id)
            ? Application.Current?.Resources.MergedDictionaries.OfType<IResourceProvider>().FirstOrDefault()
            : (ResourceDictionary)Palettes[palette.Id]!;
        var variant = palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        IBrush? Find(string key) => source is not null && source.TryGetResource(key, variant, out var value) ? value as IBrush : null;
        return new PaletteSwatch(Find("App.Surface"), Find("App.BorderStrong"), Find("App.Text"), Find("App.Accent"), Find("Method.GET"), Find("Method.POST"));
    }
}

/// <summary>A palette's colors for its preview chip in the settings.</summary>
/// <param name="Surface">Panel background.</param>
/// <param name="Border">Border.</param>
/// <param name="Text">Text.</param>
/// <param name="Accent">Accent.</param>
/// <param name="First">A method color.</param>
/// <param name="Second">Another method color.</param>
public sealed record PaletteSwatch(IBrush? Surface, IBrush? Border, IBrush? Text, IBrush? Accent, IBrush? First, IBrush? Second);
