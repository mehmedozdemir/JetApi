using System.Text.Json;
using JetApi.Testing.JsonPath;

namespace JetApi.Testing.Tests;

public sealed class JsonPathLocatorTests
{
    private const string Json = """
        {
          "id": 1042,
          "name": "Ayşe Yılmaz",
          "email": "ayse@example.com",
          "roles": ["admin", "beta"],
          "address": { "city": "İzmir", "zip code": "35000" },
          "items": [{ "id": 1, "ok": true }, { "id": 2, "ok": null }]
        }
        """;

    private static JsonPathHit? At(string marker, int extra = 1) => JsonPathLocator.Find(Json, Json.IndexOf(marker, StringComparison.Ordinal) + extra);

    [Theory]
    [InlineData("1042", "$.id", "1042", "id")]
    [InlineData("Yılmaz", "$.name", "Ayşe Yılmaz", "name")]
    [InlineData("\"email\"", "$.email", "ayse@example.com", "email")]
    [InlineData("\"beta\"", "$.roles[1]", "beta", "roles")]
    [InlineData("İzmir", "$.address.city", "İzmir", "city")]
    [InlineData("35000", "$.address['zip code']", "35000", "zip code")]
    [InlineData("true", "$.items[0].ok", "true", "ok")]
    [InlineData("null", "$.items[1].ok", "null", "ok")]
    public void TheValueUnderTheCaret_HasItsPathAndValue(string marker, string path, string value, string name)
    {
        var hit = At(marker).ShouldNotBeNull();

        hit.Path.ShouldBe(path);
        hit.Value.ShouldBe(value);
        hit.Name.ShouldBe(name);
    }

    [Fact]
    public void AMemberNameOfAnObject_SelectsTheObject()
    {
        var hit = At("\"address\"").ShouldNotBeNull();

        hit.Path.ShouldBe("$.address");
        hit.IsContainer.ShouldBeTrue();
    }

    [Fact]
    public void WhitespaceInsideAnArray_SelectsTheInnermostContainer() =>
        At(", { \"id\": 2", 1).ShouldNotBeNull().Path.ShouldBe("$.items");

    [Fact]
    public void EveryFoundPath_SelectsThatValue()
    {
        using var document = JsonDocument.Parse(Json);
        foreach (var marker in (string[])["1042", "Yılmaz", "\"beta\"", "İzmir", "35000", "true", "null"])
        {
            var hit = At(marker)!;
            JsonPathQuery.Parse(hit.Path).Query!.Select(document.RootElement).ShouldHaveSingleItem();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>")]
    public void NotJson_FindsNothing(string text) => JsonPathLocator.Find(text, 0).ShouldBeNull();
}
