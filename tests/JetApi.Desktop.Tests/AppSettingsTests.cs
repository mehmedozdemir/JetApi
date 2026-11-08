using JetApi.Desktop.Services;

namespace JetApi.Desktop.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void WithRecent_MovesToTop_AndKeepsTheLimit()
    {
        var settings = new AppSettings();
        for (var i = 0; i < AppSettings.MaxRecentWorkspaces + 3; i++)
        {
            settings = settings.WithRecent($"/w/{i}", $"W{i}", DateTimeOffset.UnixEpoch.AddDays(i));
        }

        settings = settings.WithRecent("/w/5", "W5", DateTimeOffset.UnixEpoch.AddDays(100));

        settings.RecentWorkspaces.Count.ShouldBe(AppSettings.MaxRecentWorkspaces);
        settings.RecentWorkspaces[0].Name.ShouldBe("W5");
        settings.RecentWorkspaces.Count(r => r.Name == "W5").ShouldBe(1);
    }

    [Fact]
    public void Store_RoundTrips_AndToleratesCorruptFiles()
    {
        var file = Path.Combine(Path.GetTempPath(), "jetapi-settings-" + Guid.NewGuid().ToString("N"), "settings.json");
        var store = new AppSettingsStore(file);

        store.Load().ShouldBe(new AppSettings());
        store.Save(new AppSettings { Language = "tr", Theme = AppTheme.Dark });
        store.Load().Theme.ShouldBe(AppTheme.Dark);
        store.Load().Language.ShouldBe("tr");

        File.WriteAllText(file, "{ not json");
        store.Load().Theme.ShouldBe(AppTheme.System);
        Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
    }
}
