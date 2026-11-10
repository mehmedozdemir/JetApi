using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;
using JetApi.Desktop.Views.Dialogs;
using Shouldly;

namespace JetApi.Desktop.Tests;

public sealed class UpdatesTests
{
    private static readonly ReleaseVersion Current = ReleaseVersion.TryParse("0.3.0")!;

    [Fact]
    public async Task Check_OpensTheDialog_OnlyWhenAsked()
    {
        var dialogs = new FakeDialogs();
        var updates = Updates(new FakeSource(Available()), new FakeInstaller(), dialogs);

        await updates.CheckQuietlyAsync(TestContext.Current.CancellationToken);
        updates.IsAvailable.ShouldBeTrue();
        updates.LatestVersion.ShouldBe("0.5.0");
        updates.StatusText.ShouldBe("JetApi 0.5.0 is available.");
        updates.AvailableTip.ShouldBe("New version: JetApi 0.5.0");
        dialogs.UpdatesShown.ShouldBeEmpty();

        await updates.CheckCommand.ExecuteAsync(null);
        var dialog = dialogs.UpdatesShown.ShouldHaveSingleItem();
        dialog.Title.ShouldBe("JetApi 0.5.0 is available");
        dialog.Subtitle.ShouldBe("You have 0.3.0 · 47.7 MB download");
        dialog.Sections.Select(s => s.Heading).ShouldBe(["0.5.0", "0.4.0"]);
        dialog.Sections[1].Blocks.ShouldHaveSingleItem().Inlines.ShouldHaveSingleItem().Text.ShouldBe("No notes for this release.");
    }

    [Fact]
    public async Task Check_SaysWhenUpToDate_OrWhyItFailed()
    {
        var upToDate = Updates(new FakeSource(new UpdateCheckResult(UpdateCheckStatus.UpToDate, [])), new FakeInstaller(), new FakeDialogs());
        await upToDate.CheckCommand.ExecuteAsync(null);
        upToDate.StatusText.ShouldBe("JetApi 0.3.0 is the latest version.");
        upToDate.IsAvailable.ShouldBeFalse();

        var failed = Updates(new FakeSource(new UpdateCheckResult(UpdateCheckStatus.Failed, [], "Could not reach GitHub: offline")), new FakeInstaller(), new FakeDialogs());
        await failed.CheckCommand.ExecuteAsync(null);
        failed.StatusText.ShouldBe("Could not reach GitHub: offline");
        failed.HasFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task Dialog_DownloadsThenRestartsAfterAsking()
    {
        var installer = new FakeInstaller();
        var asked = 0;
        var exited = 0;
        var leave = false;
        var dialog = new UpdateDialogViewModel(Available(), Current, installer, new FakeDialogs(), () =>
        {
            asked++;
            return Task.FromResult(leave);
        }, () => exited++);

        dialog.ShowsDownload.ShouldBeTrue();
        dialog.RestartCommand.CanExecute(null).ShouldBeFalse();
        await dialog.DownloadCommand.ExecuteAsync(null);
        dialog.Stage.ShouldBe(UpdateStage.Downloaded);
        dialog.IsDownloaded.ShouldBeTrue();
        dialog.ShowsDownload.ShouldBeFalse();

        // Unsaved requests: the user cancelled, so nothing is installed.
        await dialog.RestartCommand.ExecuteAsync(null);
        asked.ShouldBe(1);
        installer.Installed.ShouldBe(0);
        exited.ShouldBe(0);

        leave = true;
        await dialog.RestartCommand.ExecuteAsync(null);
        installer.Installed.ShouldBe(1);
        installer.StartedFrom.ShouldBe(Current);
        exited.ShouldBe(1);
    }

    [Fact]
    public async Task Dialog_KeepsRunning_WhenInstallFails()
    {
        var installer = new FakeInstaller { InstallError = "The update could not be installed; the current version is unchanged. Access denied." };
        var exited = 0;
        var dialog = new UpdateDialogViewModel(Available(), Current, installer, new FakeDialogs(), () => Task.FromResult(true), () => exited++);
        await dialog.DownloadCommand.ExecuteAsync(null);
        await dialog.RestartCommand.ExecuteAsync(null);
        dialog.Error.ShouldBe(installer.InstallError);
        dialog.Stage.ShouldBe(UpdateStage.Downloaded);
        exited.ShouldBe(0);
        installer.StartedFrom.ShouldBeNull();
    }

    [Fact]
    public async Task Dialog_ShowsDownloadErrors_AndCanRetry()
    {
        var installer = new FakeInstaller { DownloadError = "The downloaded file does not match its SHA-256 checksum, so it was deleted. Try again." };
        var dialog = new UpdateDialogViewModel(Available(), Current, installer, new FakeDialogs(), () => Task.FromResult(true), () => { });
        await dialog.DownloadCommand.ExecuteAsync(null);
        dialog.HasError.ShouldBeTrue();
        dialog.Stage.ShouldBe(UpdateStage.Ready);
        dialog.DownloadCommand.CanExecute(null).ShouldBeTrue();

        installer.DownloadError = null;
        await dialog.DownloadCommand.ExecuteAsync(null);
        dialog.HasError.ShouldBeFalse();
        dialog.IsDownloaded.ShouldBeTrue();
    }

    [Fact]
    public async Task Dialog_OffersTheReleasePage_WhenThisCopyCannotInstall()
    {
        var dialogs = new FakeDialogs();
        var installer = new FakeInstaller { Support = InstallSupport.No("On this platform JetApi does not install updates itself.") };
        var dialog = new UpdateDialogViewModel(Available(), Current, installer, dialogs, () => Task.FromResult(true), () => { });
        dialog.CanInstall.ShouldBeFalse();
        dialog.ShowsDownload.ShouldBeFalse();
        dialog.ShowsReleaseLink.ShouldBeFalse();
        dialog.DownloadCommand.CanExecute(null).ShouldBeFalse();
        dialog.ManualReason.ShouldBe("On this platform JetApi does not install updates itself.");
        dialog.Subtitle.ShouldBe("You have 0.3.0.");
        await dialog.OpenReleasePageCommand.ExecuteAsync(null);
        dialogs.OpenedInBrowser.ShouldHaveSingleItem().ShouldBe(new Uri("https://github.com/mehmedozdemir/JetApi/releases/tag/v0.5.0"));
    }

    [Fact]
    public void ReleaseNotes_AfterAnUpdate_ShowOnlyWhatChanged()
    {
        var notes = Changelog.Parse("""
            ## [Unreleased]

            - Coming

            ## [0.5.0] - 2026-12-01

            - Five

            ## [0.4.0] - 2026-11-20

            - Four

            ## [0.3.0] - 2026-11-09

            - Three
            """);
        var all = new ReleaseNotesViewModel(notes, ReleaseVersion.TryParse("0.5.0")!);
        all.Sections.Select(s => s.Heading).ShouldBe(["Unreleased", "0.5.0", "0.4.0", "0.3.0"]);
        all.Title.ShouldBe("Release notes");
        all.Subtitle.ShouldBe("You are using JetApi 0.5.0.");
        all.Sections[1].Date.ShouldBe("1 December 2026");

        var updated = new ReleaseNotesViewModel(notes, ReleaseVersion.TryParse("0.5.0")!, Current);
        updated.Sections.Select(s => s.Heading).ShouldBe(["0.5.0", "0.4.0"]);
        updated.Title.ShouldBe("JetApi is now 0.5.0");
        updated.Subtitle.ShouldBe("What changed since 0.3.0.");
        updated.ShowsLanguageNote.ShouldBeTrue();
    }

    [Fact]
    public async Task About_CopiesDetails_AndOpensLinks()
    {
        var dialogs = new FakeDialogs();
        var notes = 0;
        var about = new AboutViewModel(AppInfo.Current, dialogs, null, () =>
        {
            notes++;
            return Task.CompletedTask;
        });
        about.HasUpdates.ShouldBeFalse();
        about.VersionText.ShouldBe("Version " + AppInfo.Current.DisplayVersion);
        await about.CopyInfoCommand.ExecuteAsync(null);
        dialogs.Clipboard.ShouldBe(AppInfo.Current.ToReport());
        about.Copied.ShouldBeTrue();
        await about.ShowReleaseNotesCommand.ExecuteAsync(null);
        notes.ShouldBe(1);
        await about.OpenRepositoryCommand.ExecuteAsync(null);
        await about.OpenLicenseCommand.ExecuteAsync(null);
        await about.ReportIssueCommand.ExecuteAsync(null);
        dialogs.OpenedInBrowser.ShouldBe([AppInfo.RepositoryUrl, AppInfo.LicenseUrl, AppInfo.IssuesUrl]);
    }

    [Fact]
    public void Settings_RemembersTheStartUpCheck_OffByDefault()
    {
        new AppSettings().CheckForUpdates.ShouldBeFalse();
        var host = new Host(new AppSettings());
        var settings = new SettingsViewModel(host, []);
        settings.CheckForUpdates.ShouldBeFalse();
        settings.CheckForUpdates = true;
        host.Settings.CheckForUpdates.ShouldBeTrue();
        settings.ResetCommand.Execute(null);
        host.Settings.CheckForUpdates.ShouldBeFalse();
    }

    [Fact]
    public async Task Palette_OffersAboutNotesAndUpdates()
    {
        using var workspace = await TempWorkspace.CreateAsync();
        var dialogs = new FakeDialogs();
        var host = new FakeWorkspaceHost { Updates = Updates(new FakeSource(Available()), new FakeInstaller(), dialogs) };
        using var vm = workspace.CreateViewModel(dialogs, host: host);

        vm.OpenPalette();
        vm.Palette!.Query = ">check for updates";
        await vm.Palette.ExecuteSelectedCommand.ExecuteAsync(null);
        host.Updates.IsAvailable.ShouldBeTrue();
        dialogs.UpdatesShown.Count.ShouldBe(1);

        vm.OpenPalette();
        vm.Palette!.Query = ">install update";
        vm.Palette.Results.ShouldHaveSingleItem().Label.ShouldBe("Install update: JetApi 0.5.0");

        vm.OpenPalette();
        vm.Palette!.Query = ">release notes";
        await vm.Palette.ExecuteSelectedCommand.ExecuteAsync(null);
        host.ReleaseNotesOpened.ShouldBe(1);

        vm.OpenPalette();
        vm.Palette!.Query = ">about jetapi";
        await vm.Palette.ExecuteSelectedCommand.ExecuteAsync(null);
        host.LastSettingsSection.ShouldBe(SettingsSection.About);
    }

    [Fact]
    public Task Screens_AboutNotesAndUpdate() => HeadlessApp.Session.Dispatch(WalkAsync, TestContext.Current.CancellationToken);

    private static async Task<bool> WalkAsync()
    {
        HeadlessApp.Log.Clear();
        var app = (App)Application.Current!;
        using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
        var dialogs = new FakeDialogs();
        using var vm = new MainWindowViewModel(new AppSettingsStore(Path.Combine(workspace.Root, ".jetapi", "settings.json")), dialogs, app.ApplyTheme, action => Dispatcher.UIThread.Post(action), app.ApplyAppearance);
        vm.Updates = Updates(new FakeSource(Available()), new FakeInstaller(), dialogs);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        var opened = new List<Window>();
        try
        {
            // Welcome screen: version footer; no update button before a check.
            Settle();
            UpdateButton(window).IsEffectivelyVisible.ShouldBeFalse();
            ScreenTests.Capture(window, "ux-48-welcome-version");

            // A start-up check found a release: the top bar shows it.
            await vm.Updates.CheckQuietlyAsync();
            Settle();
            UpdateButton(window).IsEffectivelyVisible.ShouldBeTrue();
            await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
            Settle();
            UpdateButton(window).IsEffectivelyVisible.ShouldBeTrue();
            ScreenTests.Capture(window, "ux-49-update-button");

            // About, in English and light, then Turkish and dark.
            var settings = new SettingsViewModel(vm, []) { Section = SettingsSection.About, About = new AboutViewModel(AppInfo.Current, dialogs, vm.Updates, () => Task.CompletedTask) };
            opened.Add(Show(new Views.SettingsWindow { DataContext = settings, Width = 860, Height = 600 }));
            ScreenTests.Capture(opened[^1], "ux-50-about-en");
            Loc.Instance.SetLanguage("tr");
            vm.ApplySettings(vm.Settings with { Theme = AppTheme.Dark });
            settings = new SettingsViewModel(vm, []) { Section = SettingsSection.About, About = new AboutViewModel(AppInfo.Current, dialogs, vm.Updates, () => Task.CompletedTask) };
            opened.Add(Show(new Views.SettingsWindow { DataContext = settings, Width = 720, Height = 480 }));
            ScreenTests.Capture(opened[^1], "ux-51-about-tr-dark-min");

            // Release notes from the bundled changelog.
            opened.Add(Show(new ReleaseNotesDialog { DataContext = new ReleaseNotesViewModel(Changelog.Current, AppInfo.Current.Version), Width = 760, Height = 680 }));
            opened[^1].GetVisualDescendants().OfType<Controls.MarkdownView>().Count().ShouldBeGreaterThan(1);
            ScreenTests.Capture(opened[^1], "ux-52-release-notes-tr-dark");

            // The update dialog: ready, then downloaded.
            var update = new UpdateDialogViewModel(Available(), Current, new FakeInstaller(), dialogs, () => Task.FromResult(false), () => { });
            opened.Add(Show(new UpdateDialog { DataContext = update, Width = 720, Height = 640 }));
            ScreenTests.Capture(opened[^1], "ux-53-update-ready-tr-dark");
            await update.DownloadCommand.ExecuteAsync(null);
            Settle();
            ScreenTests.Capture(opened[^1], "ux-54-update-downloaded-tr-dark");

            Loc.Instance.SetLanguage("en");
            vm.ApplySettings(vm.Settings with { Theme = AppTheme.Light });
            var manual = new UpdateDialogViewModel(Available(), Current, new FakeInstaller { Support = InstallSupport.No(Loc.Get("Update_ManualDevelopment")) }, dialogs, () => Task.FromResult(true), () => { });
            opened.Add(Show(new UpdateDialog { DataContext = manual, Width = 540, Height = 420 }));
            ScreenTests.Capture(opened[^1], "ux-55-update-manual-en-min");
        }
        finally
        {
            foreach (var dialog in opened)
            {
                dialog.Close();
            }

            Loc.Instance.SetLanguage("en");
            vm.ApplySettings(vm.Settings with { Theme = AppTheme.Light });
            window.Close();
        }

        HeadlessApp.Log.Messages.ShouldBeEmpty();
        return true;
    }

    private static Button UpdateButton(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UpdatePill");

    private static Window Show(Window window)
    {
        window.Show();
        Settle();
        return window;
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static UpdatesViewModel Updates(IUpdateSource source, IUpdateInstaller installer, FakeDialogs dialogs) =>
        new(source, installer, dialogs, Current, () => Task.FromResult(true), () => { });

    private static UpdateCheckResult Available() => new(UpdateCheckStatus.Available,
    [
        new ReleaseInfo(ReleaseVersion.TryParse("0.5.0")!, "JetApi 0.5.0", """
            Advanced gRPC release (M4).

            ### Added

            - Server reflection: services are listed without `.proto` files ([docs](docs/grpc.md)).
            - Streaming calls: server, client and **bidirectional**.
              - Messages show live as they arrive.

            ### Fixed

            - Tabs kept the old path after a move.
            """, new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero), new Uri("https://github.com/mehmedozdemir/JetApi/releases/tag/v0.5.0"),
            [new ReleaseAsset("JetApi-0.5.0-win-x64.zip", new Uri("https://dl.test/a.zip"), 50_000_000), new ReleaseAsset("SHA256SUMS.txt", new Uri("https://dl.test/s"), 100)]),
        new ReleaseInfo(ReleaseVersion.TryParse("0.4.0")!, "JetApi 0.4.0", "", null, new Uri("https://github.com/mehmedozdemir/JetApi/releases/tag/v0.4.0"), []),
    ]);

    private sealed class FakeSource(UpdateCheckResult result) : IUpdateSource
    {
        public Task<UpdateCheckResult> CheckAsync(ReleaseVersion current, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FakeInstaller : IUpdateInstaller
    {
        public InstallSupport Support { get; set; } = new(true, null, "C:\\JetApi", "win-x64");

        public string? DownloadError { get; set; }

        public string? InstallError { get; set; }

        public int Installed { get; private set; }

        public ReleaseVersion? StartedFrom { get; private set; }

        public Task<UpdateDownload> DownloadAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(new DownloadProgress(25_000_000, 50_000_000));
            return Task.FromResult(DownloadError is null ? new UpdateDownload("C:\\staging", null) : new UpdateDownload(null, DownloadError));
        }

        public string? Install(UpdateDownload download)
        {
            if (InstallError is null)
            {
                Installed++;
            }

            return InstallError;
        }

        public void StartNewVersion(ReleaseVersion previous) => StartedFrom = previous;
    }

    private sealed class Host(AppSettings settings) : ISettingsHost
    {
        public AppSettings Settings { get; private set; } = settings;

        public void ApplySettings(AppSettings settings) => Settings = settings;
    }
}
