using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using JetApi.Desktop.Localization;

namespace JetApi.Desktop.Services;

/// <summary>Whether this copy can update itself, and if not, why.</summary>
/// <param name="CanInstall">Updates install in place.</param>
/// <param name="Reason">Why not, for the user (the release page is offered instead).</param>
/// <param name="InstallDirectory">The folder JetApi runs from.</param>
/// <param name="RuntimeId">The package runtime (<c>win-x64</c>).</param>
public sealed record InstallSupport(bool CanInstall, string? Reason, string? InstallDirectory, string? RuntimeId)
{
    /// <summary>Not installable, with a reason.</summary>
    public static InstallSupport No(string reason) => new(false, reason, null, null);
}

/// <summary>Download progress.</summary>
/// <param name="Received">Bytes received.</param>
/// <param name="Total">Total bytes, when known.</param>
public readonly record struct DownloadProgress(long Received, long? Total);

/// <summary>Result of a download: the verified, unpacked files, or why it failed.</summary>
/// <param name="StagingDirectory">Where the new files wait.</param>
/// <param name="Error">What went wrong, for the user.</param>
public sealed record UpdateDownload(string? StagingDirectory, string? Error)
{
    /// <summary>The files are ready to install.</summary>
    public bool Succeeded => StagingDirectory is not null;
}

/// <summary>Downloads, verifies and installs a release.</summary>
public interface IUpdateInstaller
{
    /// <summary>Whether this copy can update itself.</summary>
    InstallSupport Support { get; }

    /// <summary>Downloads the package for this platform, checks it against <c>SHA256SUMS.txt</c> and unpacks it.</summary>
    Task<UpdateDownload> DownloadAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Replaces the installed files with the downloaded ones; returns an error message, or <c>null</c> on success (nothing changes on failure).</summary>
    string? Install(UpdateDownload download);

    /// <summary>Starts the newly installed app; it shows what changed since <paramref name="previous"/>.</summary>
    void StartNewVersion(ReleaseVersion previous);
}

/// <summary>
/// Self-update for the Windows package (a folder holding the single-file <c>JetApi.exe</c>). A running executable cannot be
/// overwritten on Windows but can be renamed, so each file is renamed to <c>*.jetapi-old</c> and the new one copied in; if any
/// step fails, the files already swapped are put back. The next start deletes the old files. See ADR-0011.
/// </summary>
public sealed class UpdateInstaller : IUpdateInstaller
{
    /// <summary>Suffix of replaced files waiting to be deleted.</summary>
    public const string OldSuffix = ".jetapi-old";

    /// <summary>Command-line option the new version is started with.</summary>
    public const string UpdatedFromOption = "--updated-from";

    private const string ExecutableName = "JetApi.exe";

    private readonly HttpClient _http;
    private readonly string _workRoot;
    private readonly Lazy<InstallSupport> _support;

    /// <summary>Creates the installer.</summary>
    /// <param name="http">For downloads.</param>
    /// <param name="support">Whether and where to install; detected on first use when <c>null</c> (<see cref="Detect()"/>).</param>
    /// <param name="workRoot">Where downloads are kept until installed.</param>
    public UpdateInstaller(HttpClient http, InstallSupport? support = null, string? workRoot = null)
    {
        _http = http;
        _support = support is null ? new Lazy<InstallSupport>(Detect) : new Lazy<InstallSupport>(support);
        _workRoot = workRoot ?? DefaultWorkRoot;
    }

    /// <summary>Downloads live here: <c>%LOCALAPPDATA%\JetApi\updates</c>.</summary>
    public static string DefaultWorkRoot { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JetApi", "updates");

    /// <inheritdoc />
    public InstallSupport Support => _support.Value;

    /// <summary>Decides whether this process is an installed Windows package that may replace its own files.</summary>
    public static InstallSupport Detect() => Detect(OperatingSystem.IsWindows(), Environment.ProcessPath, GitHubReleases.CurrentRuntimeId());

    /// <summary>Testable core of <see cref="Detect()"/>.</summary>
    public static InstallSupport Detect(bool isWindows, string? processPath, string? runtimeId)
    {
        if (!isWindows)
        {
            return InstallSupport.No(Loc.Get("Update_ManualPlatform"));
        }

        // "dotnet run" and test hosts run JetApi.dll next to a small launcher; only the package is a single JetApi.exe.
        var directory = Path.GetDirectoryName(processPath);
        if (processPath is null || directory is null || runtimeId is null
            || !string.Equals(Path.GetFileName(processPath), ExecutableName, StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(directory, "JetApi.dll")))
        {
            return InstallSupport.No(Loc.Get("Update_ManualDevelopment"));
        }

        if (!CanWrite(directory))
        {
            return InstallSupport.No(Loc.Format("Update_ManualReadOnly", directory));
        }

        return new InstallSupport(true, null, directory, runtimeId);
    }

    /// <inheritdoc />
    public async Task<UpdateDownload> DownloadAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!Support.CanInstall || Support.RuntimeId is null)
        {
            return Fail(Support.Reason ?? Loc.Get("Update_ManualDevelopment"));
        }

        var packageName = ReleaseInfo.PackageName(release.Version, Support.RuntimeId);
        if (release.Asset(packageName) is not { } package || release.Asset(ReleaseInfo.ChecksumsName) is not { } checksums)
        {
            return Fail(Loc.Format("Update_ErrorNoPackage", packageName));
        }

        var work = Path.Combine(_workRoot, release.Version.ToString());
        try
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }

            Directory.CreateDirectory(work);
            var sums = await _http.GetStringAsync(checksums.DownloadUrl, cancellationToken).ConfigureAwait(false);
            if (ExpectedHash(sums, packageName) is not { } expected)
            {
                return Fail(Loc.Format("Update_ErrorNoChecksum", packageName));
            }

            var zip = Path.Combine(work, packageName);
            var actual = await DownloadFileAsync(package, zip, progress, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(zip);
                return Fail(Loc.Get("Update_ErrorChecksum"));
            }

            var staging = Path.Combine(work, "files");
            ZipFile.ExtractToDirectory(zip, staging);
            File.Delete(zip);
            return File.Exists(Path.Combine(staging, ExecutableName))
                ? new UpdateDownload(staging, null)
                : Fail(Loc.Format("Update_ErrorNoPackage", packageName));
        }
        catch (HttpRequestException exception)
        {
            return Fail(Loc.Format("Update_ErrorNetwork", exception.Message));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Fail(Loc.Format("Update_ErrorFiles", exception.Message));
        }
    }

    /// <inheritdoc />
    public string? Install(UpdateDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);
        if (download.StagingDirectory is null || Support.InstallDirectory is null)
        {
            return download.Error ?? Support.Reason;
        }

        return ReplaceFiles(download.StagingDirectory, Support.InstallDirectory);
    }

    /// <inheritdoc />
    public void StartNewVersion(ReleaseVersion previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (Support.InstallDirectory is null)
        {
            return;
        }

        var start = new ProcessStartInfo(Path.Combine(Support.InstallDirectory, ExecutableName)) { UseShellExecute = false, WorkingDirectory = Support.InstallDirectory };
        start.ArgumentList.Add(UpdatedFromOption);
        start.ArgumentList.Add(previous.ToString());
        Process.Start(start)?.Dispose();
    }

    /// <summary>
    /// Copies every file under <paramref name="source"/> into <paramref name="target"/>, renaming files it replaces to
    /// <c>*.jetapi-old</c>. All or nothing: on failure, the swapped files are restored. Returns an error message or <c>null</c>.
    /// </summary>
    public static string? ReplaceFiles(string source, string target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var done = new List<(string Target, string? Old)>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string? old = null;
                if (File.Exists(destination))
                {
                    old = FreeOldName(destination);
                    File.Move(destination, old);
                }

                done.Add((destination, old));
                File.Copy(file, destination);
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Restore(done);
            return Loc.Format("Update_ErrorInstall", exception.Message);
        }
    }

    /// <summary>
    /// Deletes the downloads and, given the install folder, the files an update replaced; anything still in use is left for
    /// next time.
    /// </summary>
    public static void CleanUp(string? installDirectory, string workRoot)
    {
        ArgumentNullException.ThrowIfNull(workRoot);
        if (installDirectory is not null && Directory.Exists(installDirectory))
        {
            try
            {
                foreach (var old in Directory.EnumerateFiles(installDirectory, "*" + OldSuffix + "*", SearchOption.AllDirectories))
                {
                    TryDelete(() => File.Delete(old));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best effort: the folder may be unreadable; nothing depends on the old files being gone.
            }
        }

        if (Directory.Exists(workRoot))
        {
            TryDelete(() => Directory.Delete(workRoot, recursive: true));
        }
    }

    /// <summary>The version a just-updated app was started from (<c>--updated-from 0.3.0</c>), or <c>null</c>.</summary>
    public static ReleaseVersion? UpdatedFrom(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == UpdatedFromOption)
            {
                return ReleaseVersion.TryParse(args[i + 1]);
            }
        }

        return null;
    }

    /// <summary>The hash listed for <paramref name="fileName"/> in a <c>sha256sum</c> file (<c>&lt;hash&gt;  name</c> or <c>&lt;hash&gt; *name</c>).</summary>
    public static string? ExpectedHash(string checksums, string fileName)
    {
        ArgumentNullException.ThrowIfNull(checksums);
        foreach (var line in checksums.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space != 64 || !line[..64].All(char.IsAsciiHexDigit))
            {
                continue;
            }

            var name = line[(space + 1)..].TrimStart(' ', '*');
            if (string.Equals(name, fileName, StringComparison.Ordinal))
            {
                return line[..64];
            }
        }

        return null;
    }

    private async Task<string> DownloadFileAsync(ReleaseAsset asset, string path, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : null);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (input.ConfigureAwait(false))
        {
            var output = File.Create(path);
            await using (output.ConfigureAwait(false))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    progress?.Report(new DownloadProgress(received, total));
                }
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string FreeOldName(string path)
    {
        var old = path + OldSuffix;
        if (!File.Exists(old))
        {
            return old;
        }

        try
        {
            File.Delete(old);
            return old;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left over from an update whose process is still running; pick another name.
            return path + OldSuffix + "-" + Guid.NewGuid().ToString("N")[..8];
        }
    }

    private static void Restore(List<(string Target, string? Old)> done)
    {
        for (var i = done.Count - 1; i >= 0; i--)
        {
            var (target, old) = done[i];
            TryDelete(() => File.Delete(target));
            if (old is not null)
            {
                TryDelete(() => File.Move(old, target, overwrite: true));
            }
        }
    }

    private static bool CanWrite(string directory)
    {
        var probe = Path.Combine(directory, ".jetapi-write-test-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private static UpdateDownload Fail(string message) => new(null, message);
}
