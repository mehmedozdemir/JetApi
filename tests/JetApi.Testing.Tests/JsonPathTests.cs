using System.Text.Json;
using JetApi.Testing.JsonPath;

namespace JetApi.Testing.Tests;

/// <summary>JSONPath against the examples of RFC 9535 (expected results as given in the RFC).</summary>
public sealed class JsonPathTests
{
    // RFC 9535, Figure 1.
    private const string Bookstore = """
        { "store": {
            "book": [
              { "category": "reference", "author": "Nigel Rees", "title": "Sayings of the Century", "price": 8.95 },
              { "category": "fiction", "author": "Evelyn Waugh", "title": "Sword of Honour", "price": 12.99 },
              { "category": "fiction", "author": "Herman Melville", "title": "Moby Dick", "isbn": "0-553-21311-3", "price": 8.99 },
              { "category": "fiction", "author": "J. R. R. Tolkien", "title": "The Lord of the Rings", "isbn": "0-395-19395-8", "price": 22.99 }
            ],
            "bicycle": { "color": "red", "price": 399 }
          }
        }
        """;

    // RFC 9535, section 2.3.5.3.
    private const string FilterDocument = """
        { "a": [3, 5, 1, 2, 4, 6, {"b": "j"}, {"b": "k"}, {"b": {}}, {"b": "kilo"}],
          "o": {"p": 1, "q": 2, "r": 3, "s": 5, "t": {"u": 6}},
          "e": "f" }
        """;

    private static string Select(string document, string path)
    {
        var parsed = JsonPathQuery.Parse(path);
        parsed.Error.ShouldBeNull();
        using var json = JsonDocument.Parse(document);
        return string.Join(" | ", parsed.Query!.Select(json.RootElement).Select(e => e.GetRawText().Replace(" ", string.Empty, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("$.store.book[*].author", "\"NigelRees\" | \"EvelynWaugh\" | \"HermanMelville\" | \"J.R.R.Tolkien\"")]
    [InlineData("$..author", "\"NigelRees\" | \"EvelynWaugh\" | \"HermanMelville\" | \"J.R.R.Tolkien\"")]
    [InlineData("$.store..price", "8.95 | 12.99 | 8.99 | 22.99 | 399")]
    [InlineData("$..book[2].author", "\"HermanMelville\"")]
    [InlineData("$..book[2].publisher", "")]
    [InlineData("$..book[-1].title", "\"TheLordoftheRings\"")]
    [InlineData("$..book[0,1].price", "8.95 | 12.99")]
    [InlineData("$..book[:2].price", "8.95 | 12.99")]
    [InlineData("$..book[?@.isbn].title", "\"MobyDick\" | \"TheLordoftheRings\"")]
    [InlineData("$..book[?@.price<10].title", "\"SayingsoftheCentury\" | \"MobyDick\"")]
    [InlineData("$.store['bicycle'][\"color\"]", "\"red\"")]
    [InlineData("  $.store.bicycle.price  ", "399")]
    public void Bookstore_Examples(string path, string expected) => Select(Bookstore, path).ShouldBe(expected);

    [Fact]
    public void StoreWildcard_SelectsBothMembers() =>
        JsonDocument.Parse(Bookstore).RootElement.GetProperty("store").EnumerateObject().Count().ShouldBe(
            JsonPathQuery.Parse("$.store.*").Query!.Select(JsonDocument.Parse(Bookstore).RootElement).Count);

    [Theory]
    [InlineData("$[1:3]", "\"b\" | \"c\"")]
    [InlineData("$[5:]", "\"f\" | \"g\"")]
    [InlineData("$[1:5:2]", "\"b\" | \"d\"")]
    [InlineData("$[5:1:-2]", "\"f\" | \"d\"")]
    [InlineData("$[::-1]", "\"g\" | \"f\" | \"e\" | \"d\" | \"c\" | \"b\" | \"a\"")]
    [InlineData("$[-2:]", "\"f\" | \"g\"")]
    [InlineData("$[0:7:0]", "")]
    [InlineData("$[10]", "")]
    public void Slices(string path, string expected) => Select("""["a", "b", "c", "d", "e", "f", "g"]""", path).ShouldBe(expected);

    [Theory]
    [InlineData("$.a[?@.b == 'kilo']", "{\"b\":\"kilo\"}")]
    [InlineData("$.a[?(@.b == 'kilo')]", "{\"b\":\"kilo\"}")]
    [InlineData("$.a[?@>3.5]", "5 | 4 | 6")]
    [InlineData("$.a[?@.b]", "{\"b\":\"j\"} | {\"b\":\"k\"} | {\"b\":{}} | {\"b\":\"kilo\"}")]
    [InlineData("$[?@.*]", "[3,5,1,2,4,6,{\"b\":\"j\"},{\"b\":\"k\"},{\"b\":{}},{\"b\":\"kilo\"}] | {\"p\":1,\"q\":2,\"r\":3,\"s\":5,\"t\":{\"u\":6}}")]
    [InlineData("$.o[?@<3, ?@<3]", "1 | 2 | 1 | 2")]
    [InlineData("$.a[?@<2 || @.b == \"k\"]", "1 | {\"b\":\"k\"}")]
    [InlineData("$.a[?match(@.b, \"[jk]\")]", "{\"b\":\"j\"} | {\"b\":\"k\"}")]
    [InlineData("$.a[?search(@.b, \"[jk]\")]", "{\"b\":\"j\"} | {\"b\":\"k\"} | {\"b\":\"kilo\"}")]
    [InlineData("$.o[?@>1 && @<4]", "2 | 3")]
    [InlineData("$.o[?@.u || @.x]", "{\"u\":6}")]
    [InlineData("$.a[?@.b == $.x]", "3 | 5 | 1 | 2 | 4 | 6")]
    [InlineData("$.a[?!@.b]", "3 | 5 | 1 | 2 | 4 | 6")]
    [InlineData("$.a[?length(@.b) == 4]", "{\"b\":\"kilo\"}")]
    [InlineData("$[?count(@.*) == 5]", "{\"p\":1,\"q\":2,\"r\":3,\"s\":5,\"t\":{\"u\":6}}")]
    [InlineData("$.a[?value(@.b) == 'j']", "{\"b\":\"j\"}")]
    [InlineData("$.a[?@ == 2.0]", "2")]
    public void Filters(string path, string expected) => Select(FilterDocument, path).ShouldBe(expected);

    [Fact]
    public void EveryElementEqualsItself() =>
        Select(FilterDocument, "$.a[?@ == @]").Split(" | ").Length.ShouldBe(10);

    [Theory]
    [InlineData("store.book", "Invalid JSONPath at character 1: a path starts with $")]
    [InlineData("$.store[", "Invalid JSONPath at character 9: the path ends too early")]
    [InlineData("$.a[0", "Invalid JSONPath at character 6: ']' expected")]
    [InlineData("$.a[0}", "Invalid JSONPath at character 6: ']' expected instead of '}'")]
    [InlineData("$[01]", "Invalid JSONPath at character 3: invalid number")]
    [InlineData("$.a[?1]", "Invalid JSONPath at character 6: a filter needs a comparison or a query here")]
    [InlineData("$.a[?size(@) > 1]", "Invalid JSONPath at character 6: unknown function 'size' (supported: length, count, match, search, value)")]
    [InlineData("$.", "Invalid JSONPath at character 3: the path ends too early")]
    [InlineData("$.store x", "Invalid JSONPath at character 9: unexpected 'x'")]
    [InlineData("$['a", "Invalid JSONPath at character 5: the path ends too early")]
    public void Errors_SayWhereAndWhat(string path, string message)
    {
        var parsed = JsonPathQuery.Parse(path);

        parsed.IsSuccess.ShouldBeFalse();
        parsed.Error.ShouldBe(message);
    }

    [Fact]
    public void Escapes_InNames_AreDecoded() =>
        Select("""{"a'b": 1, "c\"d": 2, "é": 3, "éx": 4}""", """$['a\'b', "c\"d", 'é', 'éx']""").ShouldBe("1 | 2 | 3 | 4");
}
