using Avalonia;
using Avalonia.Media;

namespace JetApi.Desktop.Services;

/// <summary>
/// Applies appearance settings to the running application through resources, so every open view follows
/// immediately: <c>App.UiFont</c>, <c>App.MonoFont</c>, <c>App.EditorFontSize</c> and <c>App.WrapResponses</c>.
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

    /// <summary>Applies fonts, editor size and wrapping from <paramref name="settings"/>.</summary>
    public static void Apply(Application application, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(settings);
        var resources = application.Resources;
        resources["App.UiFont"] = new FontFamily(string.IsNullOrWhiteSpace(settings.UiFontFamily) ? BuiltInUiFont : settings.UiFontFamily + ", " + BuiltInUiFont);
        resources["App.MonoFont"] = new FontFamily(string.IsNullOrWhiteSpace(settings.EditorFontFamily) ? DefaultMonoFonts : settings.EditorFontFamily + ", " + DefaultMonoFonts);
        resources["App.EditorFontSize"] = Math.Clamp(settings.EditorFontSize, 8, 32);
        resources["App.WrapResponses"] = settings.WrapResponseLines;
    }
}
