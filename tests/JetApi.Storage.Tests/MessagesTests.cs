using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using JetApi.Core.Model;

namespace JetApi.Storage.Tests;

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

public sealed partial class MessagesTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string SourceDirectory =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "JetApi.Storage");

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
    public async Task Diagnostics_FollowTheUiLanguage()
    {
        _directory.Write("jetapi.yaml", "schemaVersion: 1\nname: w\n");
        _directory.Write("collections/c/collection.yaml", "schemaVersion: 1\nname: C\n");
        _directory.Write("collections/c/bad.http.yaml", "schemaVersion: 1\nname: R\nbody: { type: soap }\n");

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");

            var result = await WorkspaceStore.LoadAsync(_directory.Path, Token);

            result.Diagnostics.ShouldHaveSingleItem().Message
                .ShouldBe("Bilinmeyen body.type 'soap'. Geçerli değerler: none, json, xml, text, form, multipart veya file.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task SaveErrors_FollowTheUiLanguage()
    {
        var workspace = new Workspace
        {
            Name = "w",
            Collections =
            [
                new ApiCollection
                {
                    Key = "c",
                    Name = "c",
                    Children = [new HttpRequestDefinition { Key = "get", Name = "a" }, new HttpRequestDefinition { Key = "GET", Name = "b" }],
                },
            ],
        };

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");

            var exception = await Should.ThrowAsync<WorkspaceStorageException>(() => WorkspaceStore.SaveAsync(_directory.Path, workspace, Token));

            exception.Message.ShouldBe(
                "'GET' anahtarı 'c' koleksiyonu içinde birden fazla kez kullanılıyor. Anahtarlar büyük/küçük harf farkı gözetilmeden benzersiz olmalı.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [GeneratedRegex(@"Messages\.(?:Get|Format)\(\s*""([A-Za-z]+_[A-Za-z0-9_]+)""")]
    private static partial Regex UsedKey();
}
