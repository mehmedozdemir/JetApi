using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace JetApi.Desktop.Services;

/// <summary>What this copy of JetApi is: version, commit and the platform it runs on (for the About section and bug reports).</summary>
/// <param name="Version">The product version.</param>
/// <param name="Commit">Short commit the build came from, empty when unknown.</param>
/// <param name="Runtime">.NET runtime description.</param>
/// <param name="Os">Operating system description.</param>
/// <param name="Architecture">Process architecture (<c>X64</c>, <c>Arm64</c>).</param>
/// <param name="AvaloniaVersion">Avalonia version.</param>
public sealed record AppInfo(ReleaseVersion Version, string Commit, string Runtime, string Os, string Architecture, string AvaloniaVersion)
{
    /// <summary>The project's GitHub page.</summary>
    public static Uri RepositoryUrl { get; } = new("https://github.com/mehmedozdemir/JetApi");

    /// <summary>The license text on GitHub.</summary>
    public static Uri LicenseUrl { get; } = new("https://github.com/mehmedozdemir/JetApi/blob/HEAD/LICENSE");

    /// <summary>The releases page.</summary>
    public static Uri ReleasesUrl { get; } = new("https://github.com/mehmedozdemir/JetApi/releases");

    /// <summary>Where to report a problem.</summary>
    public static Uri IssuesUrl { get; } = new("https://github.com/mehmedozdemir/JetApi/issues");

    /// <summary>The running app.</summary>
    public static AppInfo Current { get; } = FromAssembly(typeof(AppInfo).Assembly);

    /// <summary>Version as shown to users: <c>0.3.0 (129a129)</c>.</summary>
    public string DisplayVersion => Commit.Length > 0 ? $"{Version} ({Commit})" : Version.ToString();

    /// <summary>Reads the version and commit from <paramref name="assembly"/>'s informational version (<c>0.3.0+129a129…</c>).</summary>
    public static AppInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        var (version, commit) = ParseInformationalVersion(informational);
        var fallback = assembly.GetName().Version;
        return new AppInfo(
            version ?? ReleaseVersion.TryParse(fallback is null ? null : $"{fallback.Major}.{fallback.Minor}.{Math.Max(fallback.Build, 0)}") ?? ReleaseVersion.TryParse("0.0.0")!,
            commit,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            typeof(Avalonia.Application).Assembly.GetName().Version?.ToString(3) ?? string.Empty);
    }

    /// <summary>Splits <c>0.3.0+129a129abcdef</c> into the version and a 7-character commit.</summary>
    public static (ReleaseVersion? Version, string Commit) ParseInformationalVersion(string informational)
    {
        ArgumentNullException.ThrowIfNull(informational);
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        var commit = plus >= 0 ? informational[(plus + 1)..] : string.Empty;
        if (commit.Length > 7 && commit.All(char.IsAsciiHexDigit))
        {
            commit = commit[..7];
        }

        return (ReleaseVersion.TryParse(plus >= 0 ? informational[..plus] : informational), commit);
    }

    /// <summary>Plain text for bug reports.</summary>
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append("JetApi ").AppendLine(DisplayVersion);
        text.Append(".NET: ").AppendLine(Runtime);
        text.Append("Avalonia: ").AppendLine(AvaloniaVersion);
        text.Append("OS: ").Append(Os).Append(" (").Append(Architecture).AppendLine(")");
        return text.ToString();
    }
}
