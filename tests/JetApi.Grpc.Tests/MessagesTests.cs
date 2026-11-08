using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using JetApi.Core.Model;
using JetApi.Core.Variables;
using JetApi.Grpc.Codec;
using JetApi.Grpc.Schema;

namespace JetApi.Grpc.Tests;

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
    private static readonly ProtoSchema Schema = TestSchema.Load();

    private static string SourceDirectory =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "JetApi.Grpc");

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
            var codec = new ProtoJsonCodec(Schema);

            Should.Throw<ProtoJsonException>(() => codec.Encode(Schema.Everything(), "{\"text\": 5}"))
                .Message.ShouldBe("$.text: bir metin bekleniyordu, 5 sayısı geldi.");
            Should.Throw<ProtoJsonException>(() => codec.Encode(Schema.Everything(), "{\"i64\": 1.5}"))
                .Message.ShouldBe("$.i64: 1.5 bir tam sayı değil (int64).");

            var request = new GrpcRequestDefinition { Key = "r", Name = "r", ServerUrl = " ", Service = "x.Y", Method = "Echo" };
            GrpcRequestPreparer.Prepare(request, AuthConfig.None, VariableResolver.Empty, Schema).Errors.ShouldBe(
            [
                "Sunucu URL'si boş. TLS için https://host:port, şifresiz bağlantı için http://host:port kullanın.",
                "'x.Y' servisi yüklenen proto dosyalarında tanımlı değil.",
            ]);

            ProtoSchemaLoader.Load([TestSchema.ProtoRoot], []).Diagnostics.ShouldHaveSingleItem()
                .Message.ShouldBe("Hiç proto dosyası yapılandırılmamış.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [GeneratedRegex(@"Messages\.(?:Get|Format)\(""([A-Za-z]+_[A-Za-z0-9_]+)""")]
    private static partial Regex UsedKey();
}
