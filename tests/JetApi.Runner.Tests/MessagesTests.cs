using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace JetApi.Runner.Tests;

// Assertions compare English messages, so tests must not depend on the machine's UI language.
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }
}

public sealed partial class MessagesTests
{
    private static string SourceDirectory =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "JetApi.Runner");

    private static HashSet<string> Keys(string file) =>
        [.. XDocument.Load(Path.Combine(SourceDirectory, file)).Root!.Elements("data").Select(e => (string)e.Attribute("name")!)];

    [Fact]
    public void EveryMessageExistsInBothLanguages()
    {
        var english = Keys("Messages.resx");
        var turkish = Keys("Messages.tr.resx");

        english.Except(turkish).ShouldBeEmpty("missing in Messages.tr.resx");
        turkish.Except(english).ShouldBeEmpty("missing in Messages.resx");
    }

    [Fact]
    public void EveryKeyUsedInCodeExists()
    {
        var keys = Keys("Messages.resx");
        var used = Directory.EnumerateFiles(SourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => UsedKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        used.ShouldNotBeEmpty();
        used.Except(keys).ShouldBeEmpty();
    }

    [Fact]
    public void Messages_FollowTheUiLanguage()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");

            DataFile.ParseCsv(string.Empty).Error.ShouldBe("Veri dosyasında satır yok.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [GeneratedRegex(@"Messages\.(?:Get|Format)\(\s*""([A-Za-z]+_[A-Za-z0-9_]+)""")]
    private static partial Regex UsedKey();
}
