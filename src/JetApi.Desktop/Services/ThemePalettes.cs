namespace JetApi.Desktop.Services;

/// <summary>A color palette for the light or the dark theme.</summary>
/// <param name="Id">Stored in the settings; also the key in <c>Themes/Palettes.axaml</c>.</param>
/// <param name="IsDark">For the dark theme.</param>
/// <param name="NameKey">Localized display name.</param>
public sealed record ThemePalette(string Id, bool IsDark, string NameKey);

/// <summary>
/// The color palettes. The light and the dark theme each have their own, so "follow the system" switches between the two
/// chosen ones. The defaults live in <c>Themes/Tokens.axaml</c>; the others in <c>Themes/Palettes.axaml</c>, each paired with
/// the matching syntax theme of the code editor.
/// </summary>
public static class ThemePalettes
{
    /// <summary>The default light palette.</summary>
    public const string DefaultLight = "jetapi-light";

    /// <summary>The default dark palette.</summary>
    public const string DefaultDark = "jetapi-dark";

    /// <summary>Every palette, light ones first, each group starting with the default.</summary>
    public static IReadOnlyList<ThemePalette> All { get; } =
    [
        new(DefaultLight, false, "Palette_JetApi"),
        new("one-light", false, "Palette_OneLight"),
        new("solarized-light", false, "Palette_SolarizedLight"),
        new("quiet-light", false, "Palette_QuietLight"),
        new("high-contrast-light", false, "Palette_HighContrast"),
        new(DefaultDark, true, "Palette_JetApi"),
        new("one-dark", true, "Palette_OneDark"),
        new("nord", true, "Palette_Nord"),
        new("dracula", true, "Palette_Dracula"),
        new("monokai", true, "Palette_Monokai"),
        new("solarized-dark", true, "Palette_SolarizedDark"),
        new("high-contrast-dark", true, "Palette_HighContrast"),
    ];

    /// <summary>The palettes of the light (<paramref name="dark"/> false) or the dark theme.</summary>
    public static IEnumerable<ThemePalette> Of(bool dark) => All.Where(p => p.IsDark == dark);

    /// <summary><paramref name="id"/> when it is a palette of that theme, otherwise the theme's default (unknown or old settings).</summary>
    public static string Normalize(string? id, bool dark) =>
        All.Any(p => p.Id == id && p.IsDark == dark) ? id! : dark ? DefaultDark : DefaultLight;

    /// <summary>Whether <paramref name="id"/> is one of the two default palettes (nothing to load).</summary>
    public static bool IsDefault(string id) => id is DefaultLight or DefaultDark;
}
