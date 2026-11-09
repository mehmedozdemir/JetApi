using JetApi.Core.Model;

namespace JetApi.Testing.Tests;

public sealed class ResponseExtractorTests
{
    private static readonly ResponseSnapshot Response = new(
        201,
        [new("Location", "/users/42")],
        """{ "access_token": "eyJ.abc", "user": { "id": 42, "roles": ["admin"] } }""",
        TimeSpan.FromMilliseconds(80));

    [Theory]
    [InlineData(ExtractionSource.Json, "$.access_token", "eyJ.abc")]
    [InlineData(ExtractionSource.Json, "$.user.id", "42")]
    [InlineData(ExtractionSource.Json, "$.user.roles", "[\"admin\"]")]
    [InlineData(ExtractionSource.Json, "$..id", "42")]
    [InlineData(ExtractionSource.Header, "location", "/users/42")]
    [InlineData(ExtractionSource.Status, "", "201")]
    public void Values_AreTakenAsUsersExpect(ExtractionSource source, string target, string value)
    {
        var result = ResponseExtractor.Extract(Response, new Extraction(source, target, "v"));

        result.Error.ShouldBeNull();
        result.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData(ExtractionSource.Json, "$.refresh_token", "Nothing found at $.refresh_token.")]
    [InlineData(ExtractionSource.Header, "ETag", "There is no ETag header.")]
    [InlineData(ExtractionSource.Json, "access_token", "Invalid JSONPath at character 1: a path starts with $")]
    public void Missing_IsReported_AndNothingIsStored(ExtractionSource source, string target, string error)
    {
        var result = ResponseExtractor.Extract(Response, new Extraction(source, target, "v"));

        result.IsSuccess.ShouldBeFalse();
        result.Value.ShouldBeNull();
        result.Error.ShouldBe(error);
    }

    [Fact]
    public void NoResponse_ExtractsNothing() =>
        ResponseExtractor.Extract(ResponseSnapshot.None, new Extraction(ExtractionSource.Status, string.Empty, "v")).Error.ShouldBe("There is no response.");

    [Fact]
    public void Run_SkipsDisabled_AndFillsVariables()
    {
        Extraction[] extractions = [new(ExtractionSource.Json, "$.{{field}}", "token"), new(ExtractionSource.Body, string.Empty, "all", IsEnabled: false)];

        var results = ResponseExtractor.Run(Response, extractions, text => text.Replace("{{field}}", "access_token", StringComparison.Ordinal));

        results.ShouldHaveSingleItem().Value.ShouldBe("eyJ.abc");
    }
}
