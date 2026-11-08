using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using JetApi.Grpc.Codec;
using JetApi.Grpc.Tests.Generated;

namespace JetApi.Grpc.Tests;

/// <summary>
/// Cross-checks the dynamic codec against Google's generated code and its official JSON
/// parser/formatter, which are the reference implementation of the proto3 JSON mapping.
/// </summary>
public sealed class ProtoJsonCodecTests
{
    private static readonly Schema.ProtoSchema Schema = TestSchema.Load();
    private static readonly ProtoJsonCodec Codec = new(Schema);
    private static readonly TypeRegistry Registry = TypeRegistry.FromMessages(Address.Descriptor, Everything.Descriptor);
    private static readonly JsonParser ReferenceParser = new(JsonParser.Settings.Default.WithTypeRegistry(Registry));
    private static readonly JsonFormatter ReferenceFormatter = new(JsonFormatter.Settings.Default.WithTypeRegistry(Registry));

    private const string FullJson = """
        {
          "d": -1.5, "f": 0.25, "i32": -42, "i64": "-9007199254740993", "u32": 4294967295, "u64": "18446744073709551615",
          "s32": -7, "s64": "-8", "fx32": 9, "fx64": "10", "sfx32": -11, "sfx64": "-12",
          "flag": true, "text": "Merhaba dünya ✓", "data": "AAEC/w==",
          "color": "COLOR_GREEN",
          "address": { "street": "İstiklal Cd.", "city": "İstanbul" },
          "numbers": [1, -2, 300000],
          "tags": ["a", "", "c"],
          "addresses": [{ "city": "Ankara" }, {}],
          "counts": { "x": 1, "y": 0 },
          "addressById": { "-5": { "city": "İzmir" }, "9223372036854775807": {} },
          "flags": { "true": "yes", "false": "no" },
          "phone": "+90 555",
          "maybe": 0,
          "createdAt": "2026-03-01T12:34:56.789Z",
          "timeout": "-1.500s",
          "bigWrapper": "123",
          "nameWrapper": "",
          "attributes": { "n": 1.5, "s": "x", "b": false, "nil": null, "o": { "inner": [1, "two"] } },
          "dynamic": [1, { "k": null }],
          "list": [true, "x", 2],
          "mask": "address.city,bigWrapper",
          "details": { "@type": "type.googleapis.com/jetapi.test.v1.Address", "city": "Bursa" },
          "colors": ["COLOR_RED", 2, "COLOR_UNSPECIFIED"],
          "tree": { "name": "root", "children": [{ "name": "a", "children": [{ "name": "a1" }] }, { "name": "b" }] },
          "unpacked": [5, 6]
        }
        """;

    [Fact]
    public void Encode_ProducesTheSameMessageAsTheReferenceParser()
    {
        var expected = ReferenceParser.Parse<Everything>(FullJson);

        var actual = Everything.Parser.ParseFrom(Codec.Encode(Schema.Everything(), FullJson));

        actual.ShouldBe(expected);
    }

    [Fact]
    public void Decode_ProducesJsonTheReferenceParserReadsBackIdentically()
    {
        var original = ReferenceParser.Parse<Everything>(FullJson);

        var json = Codec.Decode(Schema.Everything(), original.ToByteArray());

        ReferenceParser.Parse<Everything>(json).ShouldBe(original);
    }

    [Fact]
    public void Decode_MatchesReferenceFormatter_ForValuesWithSpecialJsonForms()
    {
        var original = ReferenceParser.Parse<Everything>(FullJson);

        using var actual = JsonDocument.Parse(Codec.Decode(Schema.Everything(), original.ToByteArray(), new ProtoJsonDecodeOptions(EmitDefaultValues: false)));
        using var expected = JsonDocument.Parse(ReferenceFormatter.Format(original));

        string[] properties =
        [
            "i64", "u64", "s64", "fx64", "sfx64", "data", "color", "createdAt", "timeout", "bigWrapper",
            "nameWrapper", "mask", "colors", "counts", "addressById", "flags", "attributes", "dynamic", "list", "details",
        ];
        foreach (var property in properties)
        {
            Compact(actual.RootElement.GetProperty(property)).ShouldBe(Compact(expected.RootElement.GetProperty(property)), property);
        }
    }

    private static string Compact(JsonElement element) => JsonSerializer.Serialize(element);

    [Theory]
    [InlineData("{\"d\": \"NaN\", \"f\": \"-Infinity\"}")]
    [InlineData("{\"i64\": 1e3, \"u32\": \"7\"}")]
    [InlineData("{\"created_at\": \"2026-03-01T15:34:56+03:00\", \"big_wrapper\": 0}")]
    [InlineData("{\"timeout\": \"0.000000001s\", \"createdAt\": \"0001-01-01T00:00:00Z\"}")]
    [InlineData("{\"dynamic\": null, \"attributes\": {}}")]
    [InlineData("{\"details\": {\"@type\": \"type.googleapis.com/google.protobuf.Duration\", \"value\": \"2s\"}}")]
    [InlineData("{\"details\": {}}")]
    [InlineData("{\"email\": \"x@y.z\", \"text\": null, \"address\": null}")]
    [InlineData("// comment\n{\"i32\": 1,}")]
    [InlineData("")]
    public void Encode_HandlesEdgeCases_LikeTheReferenceParser(string json)
    {
        var reference = json.StartsWith("//", StringComparison.Ordinal) ? "{\"i32\": 1}" : json.Length == 0 ? "{}" : json;
        var expected = ReferenceParser.Parse<Everything>(reference);

        Everything.Parser.ParseFrom(Codec.Encode(Schema.Everything(), json)).ShouldBe(expected);
    }

    [Fact]
    public void Encode_AcceptsUrlSafeBase64WithoutPadding() =>
        Everything.Parser.ParseFrom(Codec.Encode(Schema.Everything(), "{\"data\": \"_-8\"}")).Data.ToBase64().ShouldBe("/+8=");

    [Fact]
    public void Decode_EmitsDefaults_ButNotFieldsWithPresence()
    {
        using var json = JsonDocument.Parse(Codec.Decode(Schema.Everything(), ReadOnlyMemory<byte>.Empty));
        var root = json.RootElement;

        root.GetProperty("i32").GetInt32().ShouldBe(0);
        root.GetProperty("i64").GetString().ShouldBe("0");
        root.GetProperty("text").GetString().ShouldBe(string.Empty);
        root.GetProperty("flag").GetBoolean().ShouldBeFalse();
        root.GetProperty("color").GetString().ShouldBe("COLOR_UNSPECIFIED");
        root.GetProperty("numbers").GetArrayLength().ShouldBe(0);
        root.GetProperty("counts").EnumerateObject().ShouldBeEmpty();
        root.TryGetProperty("address", out _).ShouldBeFalse();
        root.TryGetProperty("email", out _).ShouldBeFalse();
        root.TryGetProperty("maybe", out _).ShouldBeFalse();
        root.TryGetProperty("createdAt", out _).ShouldBeFalse();
    }

    [Fact]
    public void Decode_WithoutDefaults_OmitsUnsetFields() =>
        Codec.Decode(Schema.Everything(), new Everything { I32 = 5 }.ToByteArray(), new ProtoJsonDecodeOptions(EmitDefaultValues: false, Indented: false))
            .ShouldBe("{\"i32\":5}");

    [Fact]
    public void Decode_AcceptsPackedAndUnpackedEncodings()
    {
        var unpackedNumbers = new byte[] { 0x90, 0x01, 0x01, 0x90, 0x01, 0x02 }; // field 18 as two varints
        var packedUnpacked = new byte[] { 0xB2, 0x02, 0x02, 0x05, 0x06 };         // field 38 packed although declared unpacked

        using var first = JsonDocument.Parse(Codec.Decode(Schema.Everything(), unpackedNumbers));
        using var second = JsonDocument.Parse(Codec.Decode(Schema.Everything(), packedUnpacked));

        first.RootElement.GetProperty("numbers").EnumerateArray().Select(e => e.GetInt32()).ShouldBe([1, 2]);
        second.RootElement.GetProperty("unpacked").EnumerateArray().Select(e => e.GetInt32()).ShouldBe([5, 6]);
    }

    [Fact]
    public void Decode_SkipsUnknownFields()
    {
        var withUnknown = new Everything { Text = "x" }.ToByteArray().Concat(new byte[] { 0xF8, 0x3E, 0x01 }).ToArray(); // field 999 varint

        using var json = JsonDocument.Parse(Codec.Decode(Schema.Everything(), withUnknown));

        json.RootElement.GetProperty("text").GetString().ShouldBe("x");
    }

    [Fact]
    public void Decode_UnresolvableAny_ShowsRawBytes()
    {
        var message = new Everything { Details = new Any { TypeUrl = "type.googleapis.com/other.Unknown", Value = ByteString.CopyFrom(1, 2, 3) } };

        using var json = JsonDocument.Parse(Codec.Decode(Schema.Everything(), message.ToByteArray()));

        var details = json.RootElement.GetProperty("details");
        details.GetProperty("@type").GetString().ShouldBe("type.googleapis.com/other.Unknown");
        details.GetProperty("@unresolvedValue").GetString().ShouldBe("AQID");
    }

    [Fact]
    public void Decode_ReportsSchemaMismatch()
    {
        var wrongWireType = new byte[] { 0x1A, 0x01, 0x41 }; // field 3 (int32) as length-delimited

        Should.Throw<ProtoJsonException>(() => Codec.Decode(Schema.Everything(), wrongWireType)).Message.ShouldContain("different version of the proto file");
        Should.Throw<ProtoJsonException>(() => Codec.Decode(Schema.Everything(), new byte[] { 0x18 })).Message.ShouldContain("not a valid");
    }

    [Theory]
    [InlineData("{\"nope\": 1}", "$.nope", "Unknown field 'nope'")]
    [InlineData("{\"i32\": \"abc\"}", "$.i32", "not a valid int32")]
    [InlineData("{\"i32\": 2147483648}", "$.i32", "out of range for int32")]
    [InlineData("{\"u32\": -1}", "$.u32", "out of range for uint32")]
    [InlineData("{\"i64\": 1.5}", "$.i64", "not a whole number")]
    [InlineData("{\"f\": 1e39}", "$.f", "out of range for float")]
    [InlineData("{\"flag\": \"true\"}", "$.flag", "Expected true or false")]
    [InlineData("{\"text\": 5}", "$.text", "Expected a string")]
    [InlineData("{\"data\": \"%%%\"}", "$.data", "base64")]
    [InlineData("{\"color\": \"PURPLE\"}", "$.color", "COLOR_RED")]
    [InlineData("{\"address\": []}", "$.address", "Expected an object")]
    [InlineData("{\"address\": {\"zip\": 1}}", "$.address.zip", "Known fields: street, city")]
    [InlineData("{\"numbers\": 5}", "$.numbers", "Expected an array")]
    [InlineData("{\"tags\": [\"a\", null]}", "$.tags[1]", "null is not allowed")]
    [InlineData("{\"counts\": {\"a\": \"x\"}}", "$.counts[\"a\"]", "not a valid int32")]
    [InlineData("{\"addressById\": {\"abc\": {}}}", "$.addressById[\"abc\"]", "not a valid int64")]
    [InlineData("{\"flags\": {\"yes\": \"x\"}}", "$.flags[\"yes\"]", "\"true\" or \"false\"")]
    [InlineData("{\"email\": \"a\", \"phone\": \"b\"}", "$.phone", "oneof 'contact'")]
    [InlineData("{\"i32\": 1, \"i32\": 2}", "$.i32", "more than once")]
    [InlineData("{\"createdAt\": \"2026-13-01T00:00:00Z\"}", "$.createdAt", "RFC 3339")]
    [InlineData("{\"createdAt\": \"2026-01-01 00:00:00\"}", "$.createdAt", "RFC 3339")]
    [InlineData("{\"timeout\": \"5m\"}", "$.timeout", "duration")]
    [InlineData("{\"details\": {\"city\": \"x\"}}", "$.details", "@type")]
    [InlineData("{\"details\": {\"@type\": \"type.googleapis.com/x.Y\"}}", "$.details.@type", "not defined")]
    [InlineData("{\"i32\": ", "$", "Invalid JSON")]
    [InlineData("[]", "$", "Expected an object")]
    public void Encode_ReportsPreciseErrors(string json, string path, string message)
    {
        var exception = Should.Throw<ProtoJsonException>(() => Codec.Encode(Schema.Everything(), json));

        exception.Path.ShouldBe(path);
        exception.Message.ShouldContain(message);
    }

    [Fact]
    public void Template_IsValidInputForTheMessage()
    {
        var template = ProtoJsonTemplate.Create(Schema.Everything());

        Should.NotThrow(() => Codec.Encode(Schema.Everything(), template));
        using var json = JsonDocument.Parse(template);
        json.RootElement.TryGetProperty("email", out _).ShouldBeTrue();
        json.RootElement.TryGetProperty("phone", out _).ShouldBeFalse();
        json.RootElement.GetProperty("color").GetString().ShouldBe("COLOR_UNSPECIFIED");
        json.RootElement.GetProperty("createdAt").GetString().ShouldBe("1970-01-01T00:00:00Z");
        json.RootElement.GetProperty("tree").GetProperty("children")[0].EnumerateObject().ShouldBeEmpty();
    }
}
