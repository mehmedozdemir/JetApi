using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;

namespace JetApi.Desktop.Tests;

public sealed class SettingsTests
{
    private sealed class Host : ISettingsHost
    {
        public AppSettings Settings { get; private set; } = new AppSettings().WithRecent(Path.GetTempPath(), "W", DateTimeOffset.UnixEpoch) with { Language = "en" };

        public int Applied { get; private set; }

        public void ApplySettings(AppSettings settings)
        {
            Settings = settings;
            Applied++;
        }
    }

    [Fact]
    public void EveryChange_IsAppliedAtOnce()
    {
        var host = new Host();
        var vm = new SettingsViewModel(host, ["Arial", "Cascadia Mono", "Consolas", "Segoe UI"]);

        vm.EditorFontOptions.Select(o => o.Value).ShouldBe([null, "Cascadia Mono", "Consolas"]);
        vm.UiFontOptions.Count.ShouldBe(5);
        host.Applied.ShouldBe(0);

        vm.Theme = vm.ThemeOptions.Single(o => o.Value == AppTheme.Dark);
        vm.UiFont = vm.UiFontOptions.Single(o => o.Value == "Segoe UI");
        vm.UiScale = vm.UiScaleOptions.Single(o => o.Value == 1.25);
        vm.EditorFont = vm.EditorFontOptions.Single(o => o.Value == "Consolas");
        vm.EditorFontSize = 15;
        vm.WrapResponseLines = true;
        vm.RequestTimeoutSeconds = 90;
        vm.ConfirmUndefinedVariables = false;
        vm.SaveHistory = false;

        host.Settings.Theme.ShouldBe(AppTheme.Dark);
        host.Settings.UiFontFamily.ShouldBe("Segoe UI");
        host.Settings.UiScale.ShouldBe(1.25);
        host.Settings.EditorFontFamily.ShouldBe("Consolas");
        host.Settings.EditorFontSize.ShouldBe(15);
        host.Settings.WrapResponseLines.ShouldBeTrue();
        host.Settings.RequestTimeoutSeconds.ShouldBe(90);
        host.Settings.ConfirmUndefinedVariables.ShouldBeFalse();
        host.Settings.SaveHistory.ShouldBeFalse();
        host.Applied.ShouldBe(9);
    }

    [Fact]
    public void Rendering_NeedsARestart_OnlyWhenItDiffersFromThisRun()
    {
        var vm = new SettingsViewModel(new Host(), []);

        vm.RenderingNeedsRestart.ShouldBeFalse();
        vm.Rendering = vm.RenderingOptions.Single(o => o.Value == RenderingPreference.Gpu);
        vm.RenderingNeedsRestart.ShouldBeTrue();
    }

    [Fact]
    public void Reset_RestoresDefaults_ButKeepsRecentWorkspacesAndLanguage()
    {
        var host = new Host();
        var vm = new SettingsViewModel(host, []);
        vm.EditorFontSize = 20;
        vm.UiScale = vm.UiScaleOptions[^1];

        vm.ResetCommand.Execute(null);

        host.Settings.EditorFontSize.ShouldBe(AppSettings.DefaultEditorFontSize);
        host.Settings.UiScale.ShouldBe(1);
        host.Settings.RecentWorkspaces.ShouldHaveSingleItem();
        host.Settings.Language.ShouldBe("en");
        vm.EditorFontSize.ShouldBe((decimal)AppSettings.DefaultEditorFontSize);
    }

    // The main view model sets the UI language, which views from other tests listen to: run on the UI thread.
    [Fact]
    public Task MainWindow_AppliesTimeoutAndScale_AndOpensTheSettingsWindow() =>
        HeadlessApp.Session.Dispatch(MainWindowAppliesSettings, TestContext.Current.CancellationToken);

    private static Task<bool> MainWindowAppliesSettings()
    {
        var file = Path.Combine(Path.GetTempPath(), "jetapi-settings-" + Guid.NewGuid().ToString("N"), "settings.json");
        var dialogs = new FakeDialogs();
        AppSettings? appearance = null;
        using var vm = new MainWindowViewModel(new AppSettingsStore(file), dialogs, _ => { }, action => action(), s => appearance = s);

        vm.ApplySettings(vm.Settings with { UiScale = 1.25, RequestTimeoutSeconds = 5, EditorFontSize = 14 });

        vm.UiScale.ShouldBe(1.25);
        appearance!.EditorFontSize.ShouldBe(14);
        new AppSettingsStore(file).Load().RequestTimeoutSeconds.ShouldBe(5);
        vm.OpenSettings();
        dialogs.SettingsShown.ShouldHaveSingleItem().RequestTimeoutSeconds.ShouldBe(5);
        return Task.FromResult(true);
    }
}
