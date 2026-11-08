using System.Xml.Linq;
using JetApi.Desktop.Localization;

namespace JetApi.Desktop.Tests;

public sealed class LocalizationTests
{
    private static string LocalizationDirectory =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "JetApi.Desktop", "Localization");

    private static HashSet<string> Keys(string file) =>
        [.. XDocument.Load(Path.Combine(LocalizationDirectory, file)).Root!.Elements("data").Select(e => (string)e.Attribute("name")!)];

    [Fact]
    public void EveryStringExistsInBothLanguages()
    {
        var english = Keys("Strings.resx");
        var turkish = Keys("Strings.tr.resx");

        english.Except(turkish).ShouldBeEmpty("missing in Strings.tr.resx");
        turkish.Except(english).ShouldBeEmpty("missing in Strings.resx");
    }

    [Fact]
    public void EveryKeyUsedInCodeAndXamlExists()
    {
        var keys = Keys("Strings.resx");
        var source = Path.GetFullPath(Path.Combine(LocalizationDirectory, ".."));
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".axaml", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"(?:Loc\.(?:Get|Format)\(""|l:Tr |\[)([A-Z][A-Za-z]+_[A-Za-z0-9_]+)"))
            {
                used.Add(match.Groups[1].Value);
            }

            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"new\((?:[A-Za-z]+\.)+[A-Za-z]+, ""([A-Z][A-Za-z]+_[A-Za-z0-9_]+)""\)"))
            {
                used.Add(match.Groups[1].Value);
            }
        }

        used.Except(keys).ShouldBeEmpty();
    }

    // Switching the language notifies view models that earlier UI tests bound to controls, so it must run on
    // the UI thread; on a test thread it fails, depending on test order, with "a different thread owns it".
    [Fact]
    public Task SwitchingLanguage_ChangesStrings() =>
        HeadlessApp.Session.Dispatch(SwitchLanguage, TestContext.Current.CancellationToken);

    private static Task<bool> SwitchLanguage()
    {
        try
        {
            Loc.Instance.SetLanguage("tr");
            Loc.Get("Btn_Send").ShouldBe("Gönder");
            Loc.Format("Status_Unsaved", 2).ShouldBe("Kaydedilmemiş: 2");
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture!.Name.ShouldBe("tr-TR");

            Loc.Instance.SetLanguage("en");
            Loc.Get("Btn_Send").ShouldBe("Send");
            Loc.Get("Does_Not_Exist").ShouldBe("[Does_Not_Exist]");
        }
        finally
        {
            Loc.Instance.SetLanguage("en");
        }

        return Task.FromResult(true);
    }
}
