using System.Text.Json;
using System.Text.Json.Serialization;

namespace JetApi.Desktop.Services;

/// <summary>Color theme preference.</summary>
public enum AppTheme
{
    /// <summary>Follow the operating system.</summary>
    System,

    /// <summary>Always light.</summary>
    Light,

    /// <summary>Always dark.</summary>
    Dark,
}

/// <summary>How the window is drawn (Windows); see docs/performance.md.</summary>
public enum RenderingPreference
{
    /// <summary>CPU rendering: about 110 MB less idle memory.</summary>
    Software,

    /// <summary>GPU rendering (ANGLE/Direct3D): smoother on very large screens.</summary>
    Gpu,
}

/// <summary>A workspace opened before.</summary>
/// <param name="Path">Absolute path of the workspace directory.</param>
/// <param name="Name">Workspace name at the time it was last opened.</param>
/// <param name="LastOpened">When it was last opened.</param>
public sealed record RecentWorkspace(string Path, string Name, DateTimeOffset LastOpened);

/// <summary>Per-user application settings (not per workspace).</summary>
public sealed record AppSettings
{
    /// <summary>Maximum number of recent workspaces kept.</summary>
    public const int MaxRecentWorkspaces = 10;

    /// <summary>UI language (<c>tr</c> or <c>en</c>); <c>null</c> follows the operating system.</summary>
    public string? Language { get; init; }

    /// <summary>Color theme.</summary>
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>Recently opened workspaces, most recent first.</summary>
    public IReadOnlyList<RecentWorkspace> RecentWorkspaces { get; init; } = [];

    /// <summary>Ask before sending a request that has undefined variables.</summary>
    public bool ConfirmUndefinedVariables { get; init; } = true;

    /// <summary>Interface font family; <c>null</c> for the built-in Inter.</summary>
    public string? UiFontFamily { get; init; }

    /// <summary>Interface font size in points (body text); headings and small text follow it.</summary>
    public double UiFontSize { get; init; } = DefaultUiFontSize;

    /// <summary>Interface size: 1 is 100%; everything scales (text, spacing, icons).</summary>
    public double UiScale { get; init; } = 1;

    /// <summary>Code editor font family; <c>null</c> for the first installed of Cascadia Mono, JetBrains Mono, SF Mono, Menlo, Consolas.</summary>
    public string? EditorFontFamily { get; init; }

    /// <summary>Code editor font size in points.</summary>
    public double EditorFontSize { get; init; } = DefaultEditorFontSize;

    /// <summary>Wrap long lines in response bodies instead of scrolling sideways.</summary>
    public bool WrapResponseLines { get; init; }

    /// <summary>Timeout of requests that do not set their own, in seconds.</summary>
    public int RequestTimeoutSeconds { get; init; } = DefaultRequestTimeoutSeconds;

    /// <summary>Keep cookies that servers set and send them with later requests (per workspace, in memory).</summary>
    public bool UseCookieJar { get; init; }

    /// <summary>Record sent requests in the workspace's local history.</summary>
    public bool SaveHistory { get; init; } = true;

    /// <summary>How the window is drawn; takes effect after a restart.</summary>
    public RenderingPreference Rendering { get; init; } = RenderingPreference.Software;

    /// <summary>Default interface font size.</summary>
    public const double DefaultUiFontSize = 13;

    /// <summary>Default editor font size.</summary>
    public const double DefaultEditorFontSize = 12.5;

    /// <summary>Default request timeout.</summary>
    public const int DefaultRequestTimeoutSeconds = 30;

    /// <summary>Every preference back to its default; the recent list and language stay.</summary>
    public AppSettings WithDefaultPreferences() => new() { RecentWorkspaces = RecentWorkspaces, Language = Language };

    /// <summary>Returns settings with <paramref name="path"/> moved to the top of the recent list.</summary>
    public AppSettings WithRecent(string path, string name, DateTimeOffset now)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var recent = RecentWorkspaces
            .Where(r => !string.Equals(r.Path, fullPath, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            .Prepend(new RecentWorkspace(fullPath, name, now))
            .Take(MaxRecentWorkspaces)
            .ToList();
        return this with { RecentWorkspaces = recent };
    }

    /// <summary>Returns settings without <paramref name="path"/> in the recent list.</summary>
    public AppSettings WithoutRecent(string path) =>
        this with { RecentWorkspaces = [.. RecentWorkspaces.Where(r => r.Path != path)] };
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON in the user's application data directory.</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Creates a store for <paramref name="filePath"/>.</summary>
    public AppSettingsStore(string filePath)
    {
        FilePath = filePath;
    }

    /// <summary>The settings file, e.g. <c>%APPDATA%\JetApi\settings.json</c> or <c>~/.config/JetApi/settings.json</c>.</summary>
    public static string DefaultFilePath { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JetApi", "settings.json");

    /// <summary>The file this store reads and writes.</summary>
    public string FilePath { get; }

    /// <summary>Loads settings; a missing or corrupt file yields defaults rather than an error.</summary>
    public AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Saves settings atomically.</summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
