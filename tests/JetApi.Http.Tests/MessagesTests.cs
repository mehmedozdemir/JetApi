using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using JetApi.Core.Model;
using JetApi.Core.Variables;

namespace JetApi.Http.Tests;

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
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "JetApi.Http");

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
    public void PreparationErrors_FollowTheUiLanguage()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var request = new HttpRequestDefinition { Key = "r", Name = "r", Method = "BAD METHOD", Url = " " };

            var prepared = HttpRequestPreparer.Prepare(request, AuthConfig.None, VariableResolver.Empty, Path.GetTempPath());

            prepared.Errors.ShouldBe(["'BAD METHOD' geçerli bir HTTP metodu değil.", "URL boş."]);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [GeneratedRegex(@"Messages\.(?:Get|Format)\(""([A-Za-z]+_[A-Za-z0-9_]+)""")]
    private static partial Regex UsedKey();
}
