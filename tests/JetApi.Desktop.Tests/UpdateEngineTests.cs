using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using JetApi.Desktop.Services;
using Shouldly;

namespace JetApi.Desktop.Tests;

public sealed class UpdateEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jetapi-update-" + Guid.NewGuid().ToString("N"));

    public UpdateEngineTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Theory]
    [InlineData("0.3.0", "0.3.1")]
    [InlineData("0.3.9", "0.10.0")]
    [InlineData("0.4.0-beta.1", "0.4.0")]
    [InlineData("0.4.0-alpha", "0.4.0-alpha.1")]
    [InlineData("0.4.0-alpha.1", "0.4.0-beta")]
    [InlineData("0.4.0-beta.2", "0.4.0-beta.11")]
    [InlineData("0.4.0-rc.1", "1.0.0")]
    public void Versions_FollowSemVerPrecedence(string lower, string higher)
    {
        var low = ReleaseVersion.TryParse(lower)!;
        var high = ReleaseVersion.TryParse(higher)!;
        (low < high).ShouldBeTrue();
        (high > low).ShouldBeTrue();
        (low == high).ShouldBeFalse();
    }

    [Theory]
    [InlineData("v0.3.0", "0.3.0")]
    [InlineData("0.3.0+129a129abcdef", "0.3.0")]
    [InlineData(" 1.2.3-rc.1 ", "1.2.3-rc.1")]
    public void Versions_Parse(string text, string expected) => ReleaseVersion.TryParse(text)!.ToString().ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x.3")]
    [InlineData("1.2.3-")]
    [InlineData("latest")]
    public void Versions_RejectOtherText(string text) => ReleaseVersion.TryParse(text).ShouldBeNull();

    [Fact]
    public void AppInfo_ReadsVersionAndShortCommit()
    {
        var (version, commit) = AppInfo.ParseInformationalVersion("0.3.0+129a129ddeadbeefdeadbeefdeadbeefdeadbeef");
        version!.ToString().ShouldBe("0.3.0");
        commit.ShouldBe("129a129");
        AppInfo.ParseInformationalVersion("0.4.0-beta.1").Commit.ShouldBeEmpty();
        AppInfo.Current.Version.ShouldBeGreaterThanOrEqualTo(ReleaseVersion.TryParse("0.3.0")!);
        AppInfo.Current.ToReport().ShouldContain("JetApi " + AppInfo.Current.DisplayVersion);
    }

    [Fact]
    public void Changelog_SplitsVersions_AndSkipsEmptyUnreleased()
    {
        var notes = Changelog.Parse("""
            # Changelog

            Intro text.

            ## [Unreleased]

            ## [0.3.0] - 2026-11-09

            Summary line.

            ### Added

            - One
            - Two

            ## [0.2.0] - 2026-11-08

            ### Fixed

            - Bug

            [0.3.0]: https://github.com/x/y/releases/tag/v0.3.0
            """);
        notes.Count.ShouldBe(2);
        notes[0].Version!.ToString().ShouldBe("0.3.0");
        notes[0].Date.ShouldBe(new DateOnly(2026, 11, 9));
        notes[0].Markdown.ShouldStartWith("Summary line.");
        notes[0].Markdown.ShouldEndWith("- Two");
        notes[1].Markdown.ShouldNotContain("releases/tag");
        notes[0].LinkBase.ToString().ShouldBe("https://github.com/mehmedozdemir/JetApi/blob/v0.3.0/");
    }

    [Fact]
    public void Changelog_ShipsWithTheApp()
    {
        // The bundled copy is the repository's CHANGELOG.md.
        Changelog.Current.ShouldNotBeEmpty();
        Changelog.Current.ShouldContain(n => n.Version != null && n.Version.ToString() == "0.3.0");
        Changelog.Find(Changelog.Current, ReleaseVersion.TryParse("0.2.0")!)!.Markdown.ShouldContain("OpenAPI");
    }

    [Fact]
    public void Markdown_ReadsHeadingsBulletsAndContinuations()
    {
        var blocks = MarkdownText.Parse("""
            Summary that
            wraps.

            ### Added

            - First item
              continues here.
              - Nested `code`
            - Second with **bold** and *note*

            ```
            jetapi run .
            ```
            """);
        blocks.Select(b => (b.Kind, b.Level)).ShouldBe(
        [
            (MarkdownBlockKind.Paragraph, 0),
            (MarkdownBlockKind.Heading, 3),
            (MarkdownBlockKind.Bullet, 0),
            (MarkdownBlockKind.Bullet, 1),
            (MarkdownBlockKind.Bullet, 0),
            (MarkdownBlockKind.Code, 0),
        ]);
        MarkdownText.PlainText(blocks[0]).ShouldBe("Summary that wraps.");
        MarkdownText.PlainText(blocks[2]).ShouldBe("First item continues here.");
        blocks[3].Inlines.ShouldContain(i => i.Kind == MarkdownInlineKind.Code && i.Text == "code");
        blocks[4].Inlines.Select(i => i.Kind).ShouldBe([MarkdownInlineKind.Text, MarkdownInlineKind.Strong, MarkdownInlineKind.Text, MarkdownInlineKind.Emphasis]);
        MarkdownText.PlainText(blocks[5]).ShouldBe("jetapi run .");
    }

    [Fact]
    public void Markdown_ResolvesRelativeLinks_AndDropsUnsafeOnes()
    {
        var linkBase = new Uri("https://github.com/o/r/blob/v0.3.0/");
        var inlines = MarkdownText.ParseInlines("See [CLI](docs/cli.md), [`ADR`](docs/adr/0010.md#x), [site](https://example.com) and [bad](javascript:alert(1)).", linkBase);
        var links = inlines.Where(i => i.Kind == MarkdownInlineKind.Link).ToList();
        links.Select(l => l.Text).ShouldBe(["CLI", "ADR", "site"]);
        links[0].Link!.ToString().ShouldBe("https://github.com/o/r/blob/v0.3.0/docs/cli.md");
        links[1].Link!.ToString().ShouldBe("https://github.com/o/r/blob/v0.3.0/docs/adr/0010.md#x");
        string.Concat(inlines.Select(i => i.Text)).ShouldContain("and bad");
        MarkdownText.ParseInlines("[x](docs/a.md)").ShouldHaveSingleItem().Kind.ShouldBe(MarkdownInlineKind.Text);
    }

    [Fact]
    public async Task GitHub_OffersNewerReleases_NewestFirst()
    {
        var source = new GitHubReleases(Http(_ => Json(ReleasesJson())), new Uri("https://api.test/releases"));
        var result = await source.CheckAsync(ReleaseVersion.TryParse("0.3.0")!, TestContext.Current.CancellationToken);
        result.Status.ShouldBe(UpdateCheckStatus.Available);
        result.Newer.Select(r => r.Version.ToString()).ShouldBe(["0.5.0", "0.4.0"]);
        result.Latest!.Notes.ShouldBe("### Added\n\n- Streaming");
        result.Latest.PublishedAt.ShouldBe(new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero));
        result.Latest.Asset("jetapi-0.5.0-win-x64.ZIP")!.Size.ShouldBe(50_000_000);
    }

    [Fact]
    public async Task GitHub_OffersPreReleasesOnlyToPreReleases_AndNeverDrafts()
    {
        var source = new GitHubReleases(Http(_ => Json(ReleasesJson())), new Uri("https://api.test/releases"));
        var fromBeta = await source.CheckAsync(ReleaseVersion.TryParse("0.5.0-beta.1")!, TestContext.Current.CancellationToken);
        fromBeta.Newer.Select(r => r.Version.ToString()).ShouldBe(["0.6.0-rc.1", "0.5.0"]);
        var latest = await source.CheckAsync(ReleaseVersion.TryParse("0.5.0")!, TestContext.Current.CancellationToken);
        latest.Status.ShouldBe(UpdateCheckStatus.UpToDate);
        latest.Newer.ShouldBeEmpty();
    }

    [Fact]
    public async Task GitHub_ReportsFailures_AsResults()
    {
        var version = ReleaseVersion.TryParse("0.3.0")!;
        var limited = await new GitHubReleases(Http(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))).CheckAsync(version, TestContext.Current.CancellationToken);
        limited.Status.ShouldBe(UpdateCheckStatus.Failed);
        limited.Error.ShouldBe("GitHub refused the check (too many requests). Try again later.");

        var broken = await new GitHubReleases(Http(_ => Json("{\"message\":\"x\"}"))).CheckAsync(version, TestContext.Current.CancellationToken);
        broken.Error.ShouldBe("GitHub's answer could not be read.");

        var offline = await new GitHubReleases(Http(_ => throw new HttpRequestException("No such host"))).CheckAsync(version, TestContext.Current.CancellationToken);
        offline.Error.ShouldBe("Could not reach GitHub: No such host");
    }

    [Fact]
    public async Task GitHub_SendsOnlyAPlainRequest()
    {
        HttpRequestMessage? sent = null;
        var source = new GitHubReleases(Http(request =>
        {
            sent = request;
            return Json("[]");
        }));
        await source.CheckAsync(ReleaseVersion.TryParse("0.3.0")!, TestContext.Current.CancellationToken);
        sent!.Method.ShouldBe(HttpMethod.Get);
        sent.RequestUri.ShouldBe(GitHubReleases.DefaultEndpoint);
        sent.Headers.UserAgent.ToString().ShouldBe("JetApi/0.3.0");
        sent.Headers.Authorization.ShouldBeNull();
        sent.Content.ShouldBeNull();
    }

    [Fact]
    public void Checksums_FindTheListedHash()
    {
        var hash = new string('a', 64);
        var sums = $"{new string('b', 64)}  jetapi-cli-0.4.0-win-x64.zip\n{hash} *JetApi-0.4.0-win-x64.zip\nnot a line\n";
        UpdateInstaller.ExpectedHash(sums, "JetApi-0.4.0-win-x64.zip").ShouldBe(hash);
        UpdateInstaller.ExpectedHash(sums, "JetApi-0.4.0-win-arm64.zip").ShouldBeNull();
    }

    [Fact]
    public void Detect_InstallsOnlyTheWindowsPackage()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var exe = Path.Combine(folder, "JetApi.exe");
        File.WriteAllText(exe, "exe");

        UpdateInstaller.Detect(isWindows: false, exe, "osx-arm64").CanInstall.ShouldBeFalse();
        UpdateInstaller.Detect(isWindows: true, Path.Combine(folder, "dotnet.exe"), "win-x64").Reason.ShouldBe(
            "This copy was not installed from a release package (for example, it runs from source), so it cannot update itself.");

        var support = UpdateInstaller.Detect(isWindows: true, exe, "win-x64");
        support.CanInstall.ShouldBeTrue();
        support.InstallDirectory.ShouldBe(folder);
        Directory.GetFiles(folder).ShouldBe([exe]);

        // "dotnet run" puts JetApi.dll next to the launcher.
        File.WriteAllText(Path.Combine(folder, "JetApi.dll"), "dll");
        UpdateInstaller.Detect(isWindows: true, exe, "win-x64").CanInstall.ShouldBeFalse();
    }

    [Fact]
    public async Task Download_VerifiesTheChecksum_AndUnpacks()
    {
        var zip = Package(("JetApi.exe", "new exe"), ("libSkiaSharp.dll", "new skia"));
        var installer = Installer(zip, Sha(zip));
        var progress = new List<DownloadProgress>();

        var download = await installer.DownloadAsync(Release(), new SyncProgress(progress.Add), TestContext.Current.CancellationToken);

        download.Error.ShouldBeNull();
        File.ReadAllText(Path.Combine(download.StagingDirectory!, "JetApi.exe")).ShouldBe("new exe");
        progress[^1].Received.ShouldBe(zip.Length);
        progress[^1].Total.ShouldBe(zip.Length);
    }

    [Fact]
    public async Task Download_RejectsAFileThatDoesNotMatch()
    {
        var zip = Package(("JetApi.exe", "new exe"));
        var installer = Installer(zip, new string('0', 64));
        var download = await installer.DownloadAsync(Release(), null, TestContext.Current.CancellationToken);
        download.Succeeded.ShouldBeFalse();
        download.Error.ShouldBe("The downloaded file does not match its SHA-256 checksum, so it was deleted. Try again.");
        Directory.EnumerateFiles(Path.Combine(_root, "work"), "*.zip", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task Download_RefusesWithoutAChecksumOrPackage()
    {
        var zip = Package(("JetApi.exe", "new exe"));
        var unlisted = await Installer(zip, Sha(zip), listedName: "JetApi-0.4.0-win-arm64.zip").DownloadAsync(Release(), null, TestContext.Current.CancellationToken);
        unlisted.Error.ShouldBe("SHA256SUMS.txt has no entry for JetApi-0.4.0-win-x64.zip. The download cannot be verified, so it is not installed.");

        var noPackage = await Installer(zip, Sha(zip)).DownloadAsync(Release() with { Assets = [] }, null, TestContext.Current.CancellationToken);
        noPackage.Error.ShouldBe("The release has no package named JetApi-0.4.0-win-x64.zip.");
    }

    [Fact]
    public void ReplaceFiles_KeepsTheOldFilesAside()
    {
        var (source, target) = Folders();
        File.WriteAllText(Path.Combine(source, "JetApi.exe"), "new exe");
        File.WriteAllText(Path.Combine(source, "added.dll"), "added");
        File.WriteAllText(Path.Combine(target, "JetApi.exe"), "old exe");
        File.WriteAllText(Path.Combine(target, "JetApi.exe" + UpdateInstaller.OldSuffix), "from an earlier update");

        UpdateInstaller.ReplaceFiles(source, target).ShouldBeNull();

        File.ReadAllText(Path.Combine(target, "JetApi.exe")).ShouldBe("new exe");
        File.ReadAllText(Path.Combine(target, "added.dll")).ShouldBe("added");
        File.ReadAllText(Path.Combine(target, "JetApi.exe" + UpdateInstaller.OldSuffix)).ShouldBe("old exe");

        UpdateInstaller.CleanUp(target, Path.Combine(_root, "no-work"));
        Directory.GetFiles(target).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["JetApi.exe", "added.dll"]);
    }

    [Fact]
    public void ReplaceFiles_RestoresEverything_WhenAFileCannotBeReplaced()
    {
        var (source, target) = Folders();
        foreach (var name in new[] { "a.dll", "b.dll", "c.dll" })
        {
            File.WriteAllText(Path.Combine(source, name), "new " + name);
        }

        File.WriteAllText(Path.Combine(target, "a.dll"), "old a.dll");
        File.WriteAllText(Path.Combine(target, "c.dll"), "old c.dll");

        // A folder where a file should go cannot be replaced.
        Directory.CreateDirectory(Path.Combine(target, "b.dll"));

        var error = UpdateInstaller.ReplaceFiles(source, target);

        error.ShouldNotBeNull();
        error.ShouldStartWith("The update could not be installed; the current version is unchanged.");
        File.ReadAllText(Path.Combine(target, "a.dll")).ShouldBe("old a.dll");
        File.ReadAllText(Path.Combine(target, "c.dll")).ShouldBe("old c.dll");
        Directory.GetFiles(target).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["a.dll", "c.dll"]);
    }

    [Fact]
    public void UpdatedFrom_ReadsTheStartOption()
    {
        UpdateInstaller.UpdatedFrom(["--updated-from", "0.3.0"])!.ToString().ShouldBe("0.3.0");
        UpdateInstaller.UpdatedFrom(["--measure", "x"]).ShouldBeNull();
        UpdateInstaller.UpdatedFrom(["--updated-from"]).ShouldBeNull();
    }

    private (string Source, string Target) Folders() =>
        (Directory.CreateDirectory(Path.Combine(_root, "source")).FullName, Directory.CreateDirectory(Path.Combine(_root, "target")).FullName);

    private UpdateInstaller Installer(byte[] zip, string hash, string listedName = "JetApi-0.4.0-win-x64.zip")
    {
        var sums = $"{hash}  {listedName}\n";
        var http = Http(request => request.RequestUri!.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sums) });
        return new UpdateInstaller(http, new InstallSupport(true, null, Path.Combine(_root, "app"), "win-x64"), Path.Combine(_root, "work"));
    }

    private static ReleaseInfo Release() => new(
        ReleaseVersion.TryParse("0.4.0")!,
        "JetApi 0.4.0",
        "notes",
        null,
        new Uri("https://github.test/releases/v0.4.0"),
        [
            new ReleaseAsset("JetApi-0.4.0-win-x64.zip", new Uri("https://dl.test/JetApi-0.4.0-win-x64.zip"), 0),
            new ReleaseAsset("SHA256SUMS.txt", new Uri("https://dl.test/SHA256SUMS.txt"), 0),
        ]);

    private static byte[] Package(params (string Name, string Content)[] files)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return memory.ToArray();
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpClient Http(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new Handler(respond));

    private static string ReleasesJson() => """
        [
          { "tag_name": "v0.7.0", "draft": true, "prerelease": false, "name": "draft", "body": "", "assets": [] },
          { "tag_name": "v0.6.0-rc.1", "draft": false, "prerelease": true, "name": "JetApi 0.6.0-rc.1", "body": "rc", "assets": [] },
          { "tag_name": "v0.5.0", "draft": false, "prerelease": false, "name": "JetApi 0.5.0", "body": "### Added\n\n- Streaming",
            "published_at": "2026-12-01T10:00:00Z", "html_url": "https://github.com/mehmedozdemir/JetApi/releases/tag/v0.5.0",
            "assets": [ { "name": "JetApi-0.5.0-win-x64.zip", "browser_download_url": "https://dl.test/JetApi-0.5.0-win-x64.zip", "size": 50000000 } ] },
          { "tag_name": "v0.4.0", "draft": false, "prerelease": false, "name": "", "body": null, "assets": [] },
          { "tag_name": "nightly", "draft": false, "prerelease": false, "assets": [] },
          { "tag_name": "v0.3.0", "draft": false, "prerelease": false, "assets": [] }
        ]
        """;

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    // Progress<T> posts to a context; tests want the reports at once.
    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
