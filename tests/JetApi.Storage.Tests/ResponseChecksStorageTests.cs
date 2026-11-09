using JetApi.Core.Model;

namespace JetApi.Storage.Tests;

/// <summary>The <c>tests</c> and <c>extract</c> sections of request files (ADR-0005).</summary>
public sealed class ResponseChecksStorageTests
{
    private static readonly ResponseCheck[] Checks =
    [
        new(CheckSource.Status, CheckOperator.Equals, Expected: "200"),
        new(CheckSource.Header, CheckOperator.Contains, "Content-Type", "json"),
        new(CheckSource.Json, CheckOperator.Exists, "$.items[0].id"),
        new(CheckSource.Body, CheckOperator.NotContains, Expected: "error", IsEnabled: false),
        new(CheckSource.Time, CheckOperator.LessThan, Expected: "{{maxMs}}"),
        new(CheckSource.Schema, CheckOperator.Equals, Expected: "{\n  \"type\": \"object\"\n}"),
    ];

    private static readonly Extraction[] Extractions =
    [
        new(ExtractionSource.Json, "$.access_token", "token", ExtractionTarget.Environment),
        new(ExtractionSource.Header, "Location", "next"),
        new(ExtractionSource.Status, string.Empty, "lastStatus", IsEnabled: false),
    ];

    [Fact]
    public void HttpRequest_WritesReadableSections_AndReadsThemBack()
    {
        var request = new HttpRequestDefinition { Key = "r", Name = "R", Url = "{{baseUrl}}/users", Checks = Checks, Extractions = Extractions };

        var yaml = RequestFiles.Serialize(request);
        var read = RequestFiles.Deserialize(yaml, "r", isGrpc: false);

        read.Checks.ShouldBe(Checks);
        read.Extractions.ShouldBe(Extractions);
        yaml.ShouldContain("""
            tests:
            - check: status
              op: equals
              value: 200
            - check: header
              name: Content-Type
              op: contains
              value: json
            - check: json
              path: $.items[0].id
              op: exists
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
        yaml.ShouldContain("""
            extract:
            - from: json
              path: $.access_token
              variable: token
              to: environment
            - from: header
              name: Location
              variable: next
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
        yaml.ShouldContain("  enabled: false");
        yaml.ShouldNotContain("to: runtime");
    }

    [Fact]
    public void GrpcRequest_KeepsTheSectionsToo()
    {
        var request = new GrpcRequestDefinition { Key = "g", Name = "G", Checks = [Checks[0]], Extractions = [Extractions[0]] };

        var read = RequestFiles.Deserialize(RequestFiles.Serialize(request), "g", isGrpc: true);

        read.Checks.ShouldBe([Checks[0]]);
        read.Extractions.ShouldBe([Extractions[0]]);
    }

    [Fact]
    public void RequestsWithoutChecks_DoNotWriteTheSections()
    {
        var yaml = RequestFiles.Serialize(new HttpRequestDefinition { Key = "r", Name = "R" });

        yaml.ShouldNotContain("tests");
        yaml.ShouldNotContain("extract");
    }

    [Theory]
    [InlineData("tests:\n- check: speed\n  op: equals\n", "Unknown check 'speed' in tests[0]. Expected status, header, json, body, time or schema.")]
    [InlineData("tests:\n- check: status\n  op: '=='\n  value: 200\n", "Unknown op '==' in tests[0]. Expected equals, notEquals, greaterThan, lessThan, contains, notContains, matches, exists or notExists.")]
    [InlineData("tests:\n- check: json\n  op: exists\n", "'tests[0].path' is required.")]
    [InlineData("tests:\n- check: status\n", "'tests[0].op' is required.")]
    [InlineData("extract:\n- from: cookie\n  variable: x\n", "Unknown from 'cookie' in extract[0]. Expected json, header, status or body.")]
    [InlineData("extract:\n- from: status\n  variable: x\n  to: disk\n", "Unknown to 'disk' in extract[0]. Expected runtime, environment, collection or global.")]
    [InlineData("extract:\n- from: header\n  name: Location\n", "'extract[0].variable' is required.")]
    public void InvalidEntries_AreReportedWithTheirPlace(string section, string message) =>
        Should.Throw<InvalidDataException>(() => RequestFiles.Deserialize($"schemaVersion: 1\nname: R\n{section}", "r", isGrpc: false))
            .Message.ShouldBe(message);
}
