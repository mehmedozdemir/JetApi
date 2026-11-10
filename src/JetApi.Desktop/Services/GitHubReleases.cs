using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using JetApi.Desktop.Localization;

namespace JetApi.Desktop.Services;

/// <summary>A file attached to a release.</summary>
/// <param name="Name">File name, e.g. <c>JetApi-0.4.0-win-x64.zip</c>.</param>
/// <param name="DownloadUrl">Direct download address.</param>
/// <param name="Size">Size in bytes.</param>
public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size);

/// <summary>A published release.</summary>
/// <param name="Version">Version from the tag.</param>
/// <param name="Title">Release title.</param>
/// <param name="Notes">Release notes (Markdown; the changelog section of that version).</param>
/// <param name="PublishedAt">When it was published.</param>
/// <param name="PageUrl">The release page.</param>
/// <param name="Assets">Attached files.</param>
public sealed record ReleaseInfo(ReleaseVersion Version, string Title, string Notes, DateTimeOffset? PublishedAt, Uri PageUrl, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>The checksum file every release carries (<c>sha256sum</c> format).</summary>
    public const string ChecksumsName = "SHA256SUMS.txt";

    /// <summary>The desktop package for <paramref name="runtimeId"/>, e.g. <c>JetApi-0.4.0-win-x64.zip</c> (docs/packaging.md).</summary>
    public static string PackageName(ReleaseVersion version, string runtimeId) => $"JetApi-{version}-{runtimeId}.zip";

    /// <summary>The asset named <paramref name="name"/>, or <c>null</c>.</summary>
    public ReleaseAsset? Asset(string name) => Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>What an update check found.</summary>
public enum UpdateCheckStatus
{
    /// <summary>This is the newest version.</summary>
    UpToDate,

    /// <summary>A newer version is published.</summary>
    Available,

    /// <summary>The check did not complete (offline, rate limit, unexpected answer).</summary>
    Failed,
}

/// <summary>Result of an update check.</summary>
/// <param name="Status">What was found.</param>
/// <param name="Newer">Releases newer than the running one, newest first (empty unless <see cref="UpdateCheckStatus.Available"/>).</param>
/// <param name="Error">Why the check failed, for the user.</param>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, IReadOnlyList<ReleaseInfo> Newer, string? Error = null)
{
    /// <summary>The newest release, when one is available.</summary>
    public ReleaseInfo? Latest => Newer.Count > 0 ? Newer[0] : null;
}

/// <summary>Finds newer releases.</summary>
public interface IUpdateSource
{
    /// <summary>Compares published releases with <paramref name="current"/>.</summary>
    Task<UpdateCheckResult> CheckAsync(ReleaseVersion current, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads published releases from the GitHub REST API (no sign-in; drafts are not visible). Pre-releases are offered only to
/// someone already on a pre-release. The only data sent is the request itself, with <c>JetApi/&lt;version&gt;</c> as user agent.
/// </summary>
public sealed class GitHubReleases : IUpdateSource
{
    /// <summary>The releases endpoint of the JetApi repository.</summary>
    public static Uri DefaultEndpoint { get; } = new("https://api.github.com/repos/mehmedozdemir/JetApi/releases?per_page=30");

    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    /// <summary>Creates the source.</summary>
    public GitHubReleases(HttpClient http, Uri? endpoint = null)
    {
        _http = http;
        _endpoint = endpoint ?? DefaultEndpoint;
    }

    /// <summary>The package runtime of this process (<c>win-x64</c>, <c>osx-arm64</c>…), or <c>null</c> when no package exists for it.</summary>
    public static string? CurrentRuntimeId()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsLinux() ? "linux" : null;
        return architecture is null || os is null ? null : $"{os}-{architecture}";
    }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(ReleaseVersion current, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("JetApi", current.ToString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var limited = response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests;
                return Failed(limited ? Loc.Get("Update_ErrorRateLimit") : Loc.Format("Update_ErrorStatus", (int)response.StatusCode));
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var releases = ParseReleases(json);
            var newer = releases
                .Where(r => r.Version > current && (current.IsPreRelease || !r.Version.IsPreRelease))
                .OrderByDescending(r => r.Version)
                .ToList();
            return new UpdateCheckResult(newer.Count > 0 ? UpdateCheckStatus.Available : UpdateCheckStatus.UpToDate, newer);
        }
        catch (HttpRequestException exception)
        {
            return Failed(Loc.Format("Update_ErrorNetwork", exception.Message));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(Loc.Get("Update_ErrorTimeout"));
        }
        catch (JsonException)
        {
            return Failed(Loc.Get("Update_ErrorResponse"));
        }
    }

    /// <summary>Reads the GitHub release list; drafts and tags that are not versions are skipped.</summary>
    public static IReadOnlyList<ReleaseInfo> ParseReleases(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Expected an array of releases.");
        }

        var releases = new List<ReleaseInfo>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (Bool(item, "draft") || ReleaseVersion.TryParse(String(item, "tag_name")) is not { } version)
            {
                continue;
            }

            // A tag marked pre-release on GitHub counts as one even when its name has no "-label".
            if (Bool(item, "prerelease") && !version.IsPreRelease)
            {
                version = ReleaseVersion.TryParse($"{version}-pre") ?? version;
            }

            var assets = new List<ReleaseAsset>();
            if (item.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in list.EnumerateArray())
                {
                    if (String(asset, "name") is { Length: > 0 } name && Uri.TryCreate(String(asset, "browser_download_url"), UriKind.Absolute, out var url))
                    {
                        assets.Add(new ReleaseAsset(name, url, asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0));
                    }
                }
            }

            releases.Add(new ReleaseInfo(
                version,
                String(item, "name") is { Length: > 0 } title ? title : "JetApi " + version,
                String(item, "body") ?? string.Empty,
                item.TryGetProperty("published_at", out var published) && published.ValueKind == JsonValueKind.String && published.TryGetDateTimeOffset(out var at) ? at : null,
                Uri.TryCreate(String(item, "html_url"), UriKind.Absolute, out var page) ? page : AppInfo.ReleasesUrl,
                assets));
        }

        return releases;
    }

    private static UpdateCheckResult Failed(string message) => new(UpdateCheckStatus.Failed, [], message);

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
