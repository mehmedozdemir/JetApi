using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Desktop.ViewModels;
using TextMateSharp.Grammars;

namespace JetApi.Desktop.Tests;

/// <summary>Color palettes of the light and dark theme: complete, readable (WCAG AA) and applied live.</summary>
public sealed class ThemePaletteTests
{
    private static readonly string[] Methods = ["Method.GET", "Method.POST", "Method.PUT", "Method.PATCH", "Method.DELETE", "Method.gRPC", "Method.Other"];

    // Text sizes in JetApi are small, so text needs 4.5:1; a few dense spots (faint text and badges on a hover or
    // selection) accept a little less, as the default palettes do.
    private static readonly (string[] Foregrounds, string[] Backgrounds, double Minimum)[] Rules =
    [
        (["App.Text", "App.TextMuted"], ["App.Background", "App.Surface", "App.Sidebar", "App.Selection", "App.Hover", "App.Subtle", "App.Code"], 4.5),
        (["App.TextFaint"], ["App.Background", "App.Surface", "App.Sidebar"], 4.5),
        (["App.TextFaint"], ["App.Selection", "App.Hover", "App.Subtle"], 3.8),
        (["App.Accent", "App.Danger"], ["App.Surface", "App.Background"], 4.5),
        (["App.AccentForeground"], ["App.Accent", "App.AccentHover"], 4.5),
        (["App.CheckForeground"], ["App.Check"], 4.5),
        (["Tone.Neutral.Fg"], ["Tone.Neutral.Bg"], 4.5),
        (["Tone.Success.Fg"], ["Tone.Success.Bg"], 4.5),
        (["Tone.Info.Fg"], ["Tone.Info.Bg"], 4.5),
        (["Tone.Warning.Fg"], ["Tone.Warning.Bg"], 4.5),
        (["Tone.Danger.Fg"], ["Tone.Danger.Bg"], 4.5),
        (Methods, ["App.Surface", "App.Sidebar", "App.Background"], 4.5),
        (Methods, ["App.Selection", "App.Hover"], 4.0),
    ];

    [Fact]
    public void Normalize_FallsBackToTheThemesDefault()
    {
        ThemePalettes.Normalize("nord", dark: true).ShouldBe("nord");
        ThemePalettes.Normalize("nord", dark: false).ShouldBe(ThemePalettes.DefaultLight);
        ThemePalettes.Normalize(null, dark: true).ShouldBe(ThemePalettes.DefaultDark);
        ThemePalettes.Normalize("gone", dark: false).ShouldBe(ThemePalettes.DefaultLight);
        ThemePalettes.Of(dark: false).First().Id.ShouldBe(ThemePalettes.DefaultLight);
        ThemePalettes.All.Select(p => p.Id).ShouldBeUnique();
    }

    [Fact]
    public void Settings_ChooseAPalettePerTheme()
    {
        var host = new Host(new AppSettings { DarkPalette = "removed-palette" });
        var vm = new SettingsViewModel(host, []);

        vm.DarkPalette!.Value.ShouldBe(ThemePalettes.DefaultDark);
        vm.LightPaletteOptions.Select(o => o.Value).ShouldBe(ThemePalettes.Of(dark: false).Select(p => p.Id));
        vm.DarkPalette = vm.DarkPaletteOptions.Single(o => o.Value == "dracula");
        vm.LightPalette = vm.LightPaletteOptions.Single(o => o.Value == "solarized-light");

        host.Settings.DarkPalette.ShouldBe("dracula");
        host.Settings.LightPalette.ShouldBe("solarized-light");
        host.Settings.WithDefaultPreferences().DarkPalette.ShouldBe(ThemePalettes.DefaultDark);
    }

    [Fact]
    public void Settings_OfferOnlyTheActiveThemesPalettes()
    {
        var host = new Host(new AppSettings { Theme = AppTheme.System });
        var applied = 0;
        var light = new SettingsViewModel(host, [], systemIsDark: false);
        light.ShowsLightPalette.ShouldBeTrue();
        light.ShowsDarkPalette.ShouldBeFalse();
        light.FollowsSystem.ShouldBeTrue();
        new SettingsViewModel(host, [], systemIsDark: true).ShowsDarkPalette.ShouldBeTrue();

        light.PropertyChanged += (_, e) => applied += e.PropertyName == nameof(SettingsViewModel.ShowsDarkPalette) ? 1 : 0;
        light.Theme = light.ThemeOptions.Single(o => o.Value == AppTheme.Dark);
        applied.ShouldBe(1);
        light.ShowsDarkPalette.ShouldBeTrue();
        light.FollowsSystem.ShouldBeFalse();
        host.Settings.Theme.ShouldBe(AppTheme.Dark);
    }

    [Fact]
    public Task EveryPalette_HasEveryColor_AndReadableText() => HeadlessApp.Session.Dispatch(() =>
    {
        var tokens = Application.Current!.Resources.MergedDictionaries.OfType<IResourceProvider>().First();
        var problems = new List<string>();
        foreach (var palette in ThemePalettes.All)
        {
            var variant = palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
            var defaults = (ResourceDictionary)((ResourceDictionary)tokens).ThemeDictionaries[variant];
            IResourceProvider source = ThemePalettes.IsDefault(palette.Id) ? defaults : (ResourceDictionary)Appearance.Palettes[palette.Id]!;

            // Every color token of the defaults, except the Fluent check box keys a palette derives from App.Check.
            foreach (var key in defaults.Keys.OfType<string>().Where(k => !k.StartsWith("CheckBox", StringComparison.Ordinal)))
            {
                if (!source.TryGetResource(key, variant, out _))
                {
                    problems.Add($"{palette.Id}: {key} is missing");
                }
            }

            source.TryGetResource("App.SyntaxTheme", variant, out var syntax);
            if (!Enum.TryParse<ThemeName>(syntax as string, out _))
            {
                problems.Add($"{palette.Id}: App.SyntaxTheme '{syntax}' is not a TextMate theme");
            }

            Color ColorOf(string key) => source.TryGetResource(key, variant, out var value) && value is ISolidColorBrush brush ? brush.Color : Colors.Transparent;
            foreach (var (foregrounds, backgrounds, minimum) in Rules)
            {
                foreach (var foreground in foregrounds)
                {
                    foreach (var background in backgrounds)
                    {
                        var ratio = Contrast(ColorOf(foreground), ColorOf(background));
                        if (ratio < minimum)
                        {
                            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{palette.Id}: {foreground} on {background} is {ratio:0.00}:1, needs {minimum}"));
                        }
                    }
                }
            }
        }

        problems.ShouldBeEmpty();
        return Task.FromResult(true);
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task Palettes_ApplyLive_AndShowInTheSettings() => HeadlessApp.Session.Dispatch(WalkAsync, TestContext.Current.CancellationToken);

    private static async Task<bool> WalkAsync()
    {
        HeadlessApp.Log.Clear();
        var app = (App)Application.Current!;
        using var workspace = await ScreenTests.CreateSampleWorkspaceAsync();
        var settingsFile = Path.Combine(workspace.Root, ".jetapi", "settings.json");
        using var vm = new MainWindowViewModel(new AppSettingsStore(settingsFile), new FakeDialogs(), app.ApplyTheme, action => Dispatcher.UIThread.Post(action), app.ApplyAppearance);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await vm.OpenWorkspaceAsync(workspace.Root, offerCreate: false);
            var ws = vm.Workspace!;
            ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "Create user"));
            Settle();
            await ((HttpRequestTabViewModel)ws.SelectedTab!).SendAsync();
            Settle();

            // A palette switches every open view at once, without a restart.
            var url = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("field") && b.IsEffectivelyVisible);
            vm.ApplySettings(vm.Settings with { Theme = AppTheme.Dark, DarkPalette = "nord" });
            Settle();
            ((ISolidColorBrush)url.Background!).Color.ShouldBe(Color.Parse("#2E3440"));

            // A middle click on a tab closes it, also off its title.
            ws.OpenNode(ws.Collections[0].Children.Single(c => c.Name == "List users"));
            Settle();
            ws.Tabs.Count.ShouldBe(2);
            var tab = window.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.DataContext == ws.Tabs[1]);
            var point = tab.TranslatePoint(new Point(4, tab.Bounds.Height - 4), window)!.Value;
            window.MouseDown(point, Avalonia.Input.MouseButton.Middle);
            window.MouseUp(point, Avalonia.Input.MouseButton.Middle);
            Settle();
            ws.Tabs.ShouldHaveSingleItem().Name.ShouldBe("Create user");
            url = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("field") && b.IsEffectivelyVisible);
            ScreenTests.Capture(window, "ux-40-palette-nord");

            foreach (var (theme, palette) in new[] { (AppTheme.Dark, "one-dark"), (AppTheme.Dark, "dracula"), (AppTheme.Dark, "monokai"), (AppTheme.Dark, "solarized-dark"),
                (AppTheme.Dark, "high-contrast-dark"), (AppTheme.Light, "one-light"), (AppTheme.Light, "solarized-light"), (AppTheme.Light, "quiet-light"), (AppTheme.Light, "high-contrast-light") })
            {
                vm.ApplySettings(theme == AppTheme.Dark ? vm.Settings with { Theme = theme, DarkPalette = palette } : vm.Settings with { Theme = theme, LightPalette = palette });
                Settle();
                ScreenTests.Capture(window, "ux-41-palette-" + palette);
            }

            ((ISolidColorBrush)url.Background!).Color.ShouldBe(Colors.White);

            // Back to the default: Tokens.axaml again.
            vm.ApplySettings(vm.Settings with { LightPalette = ThemePalettes.DefaultLight });
            Settle();
            ((ISolidColorBrush)url.Background!).Color.ShouldBe(Colors.White);
            app.Resources.ThemeDictionaries.ContainsKey(ThemeVariant.Light).ShouldBeFalse();

            // The settings window previews each palette in its own colors, in Turkish too.
            Loc.Instance.SetLanguage("tr");
            vm.ApplySettings(vm.Settings with { Theme = AppTheme.Dark });
            var settings = new SettingsViewModel(vm, []);
            var settingsWindow = new Views.SettingsWindow { DataContext = settings, Width = 860, Height = 600 };
            settingsWindow.Show();
            Settle();
            settingsWindow.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "LightPaletteBox").IsVisible.ShouldBeFalse();
            var dark = settingsWindow.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "DarkPaletteBox");
            dark.IsDropDownOpen = true;
            Settle();
            ScreenTests.Capture(settingsWindow, "ux-42-settings-palettes-tr");
            dark.IsDropDownOpen = false;
            settings.Theme = settings.ThemeOptions.Single(o => o.Value == AppTheme.Light);
            Settle();
            dark.IsVisible.ShouldBeFalse();
            ScreenTests.Capture(settingsWindow, "ux-47-settings-light-palettes-tr");
            settingsWindow.Close();
        }
        finally
        {
            Loc.Instance.SetLanguage("en");
            vm.ApplySettings(vm.Settings with { Theme = AppTheme.Light, LightPalette = ThemePalettes.DefaultLight, DarkPalette = ThemePalettes.DefaultDark });
            window.Close();
        }

        HeadlessApp.Log.Messages.ShouldBeEmpty();
        return true;
    }

    // WCAG 2.1 contrast ratio.
    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color c) => (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Host(AppSettings settings) : ISettingsHost
    {
        public AppSettings Settings { get; private set; } = settings;

        public void ApplySettings(AppSettings settings) => Settings = settings;
    }
}
