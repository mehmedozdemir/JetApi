using JetApi.Core.Model;

namespace JetApi.Testing.Tests;

public sealed class ResponseCheckerTests
{
    private const string Body = """
        { "id": 42, "name": "Ayşe", "price": 10.50, "tags": ["admin", "beta"],
          "items": [{ "id": 1 }, { "id": 2 }], "token": "abc.def", "nothing": null }
        """;

    private static readonly ResponseSnapshot Response = new(
        200,
        [new("Content-Type", "application/json; charset=utf-8"), new("Set-Cookie", "a=1"), new("Set-Cookie", "b=2")],
        Body,
        TimeSpan.FromMilliseconds(123.4));

    private static CheckResult Check(CheckSource source, CheckOperator op, string target = "", string expected = "", ResponseSnapshot? response = null) =>
        ResponseChecker.Evaluate(response ?? Response, new ResponseCheck(source, op, target, expected));

    [Theory]
    [InlineData(CheckSource.Status, CheckOperator.Equals, "", "200")]
    [InlineData(CheckSource.Status, CheckOperator.LessThan, "", "300")]
    [InlineData(CheckSource.Status, CheckOperator.NotEquals, "", "404")]
    [InlineData(CheckSource.Header, CheckOperator.Contains, "content-type", "json")]
    [InlineData(CheckSource.Header, CheckOperator.Equals, "Set-Cookie", "a=1, b=2")]
    [InlineData(CheckSource.Header, CheckOperator.NotExists, "X-Missing", "")]
    [InlineData(CheckSource.Json, CheckOperator.Equals, "$.id", "42")]
    [InlineData(CheckSource.Json, CheckOperator.Equals, "$.id", "42.0")]
    [InlineData(CheckSource.Json, CheckOperator.Equals, "$.name", "Ayşe")]
    [InlineData(CheckSource.Json, CheckOperator.Equals, "$.name", "\"Ayşe\"")]
    [InlineData(CheckSource.Json, CheckOperator.Equals, "$.tags", "[\"admin\", \"beta\"]")]
    [InlineData(CheckSource.Json, CheckOperator.Equals, "$.nothing", "null")]
    [InlineData(CheckSource.Json, CheckOperator.Exists, "$.nothing", "")]
    [InlineData(CheckSource.Json, CheckOperator.NotExists, "$.missing", "")]
    [InlineData(CheckSource.Json, CheckOperator.GreaterThan, "$.price", "10.4")]
    [InlineData(CheckSource.Json, CheckOperator.Contains, "$.tags", "admin")]
    [InlineData(CheckSource.Json, CheckOperator.Contains, "$.items[*].id", "2")]
    [InlineData(CheckSource.Json, CheckOperator.NotContains, "$.items[*].id", "3")]
    [InlineData(CheckSource.Json, CheckOperator.Contains, "$", "token")]
    [InlineData(CheckSource.Json, CheckOperator.Matches, "$.token", "^[a-z]+\\.[a-z]+$")]
    [InlineData(CheckSource.Body, CheckOperator.Contains, "", "\"id\": 42")]
    [InlineData(CheckSource.Body, CheckOperator.Exists, "", "")]
    [InlineData(CheckSource.Time, CheckOperator.LessThan, "", "500")]
    [InlineData(CheckSource.Time, CheckOperator.Equals, "", "123")]
    public void Passing(CheckSource source, CheckOperator op, string target, string expected)
    {
        var result = Check(source, op, target, expected);

        result.Message.ShouldBeNull();
        result.Passed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(CheckSource.Status, CheckOperator.Equals, "", "201", "Expected 201, got 200.")]
    [InlineData(CheckSource.Status, CheckOperator.GreaterThan, "", "299", "Expected more than 299, got 200.")]
    [InlineData(CheckSource.Header, CheckOperator.Exists, "X-Trace", "", "There is no X-Trace header.")]
    [InlineData(CheckSource.Header, CheckOperator.Equals, "X-Trace", "1", "There is no X-Trace header.")]
    [InlineData(CheckSource.Json, CheckOperator.Equals, "$.id", "43", "Expected 43, got 42.")]
    [InlineData(CheckSource.Json, CheckOperator.Exists, "$.user.id", "", "Nothing found at $.user.id.")]
    [InlineData(CheckSource.Json, CheckOperator.NotExists, "$.id", "", "Expected it to be missing, got 42.")]
    [InlineData(CheckSource.Json, CheckOperator.LessThan, "$.name", "5", "Ayşe is not a number.")]
    [InlineData(CheckSource.Json, CheckOperator.Contains, "$.tags", "root", "Expected it to contain root, got [\"admin\",\"beta\"].")]
    [InlineData(CheckSource.Json, CheckOperator.Equals, "$.id[", "1", "Invalid JSONPath at character 6: the path ends too early")]
    [InlineData(CheckSource.Body, CheckOperator.Matches, "", "(", "Invalid regular expression: Invalid pattern '(' at offset 1. Not enough )'s.")]
    [InlineData(CheckSource.Time, CheckOperator.LessThan, "", "100", "Expected less than 100, got 123.")]
    [InlineData(CheckSource.Time, CheckOperator.LessThan, "", "fast", "fast is not a number.")]
    public void Failing_SayWhy(CheckSource source, CheckOperator op, string target, string expected, string message)
    {
        var result = Check(source, op, target, expected);

        result.Passed.ShouldBeFalse();
        result.Message.ShouldBe(message);
    }

    [Fact]
    public void NoResponse_FailsEveryCheck() =>
        Check(CheckSource.Status, CheckOperator.Equals, expected: "200", response: ResponseSnapshot.None).Message.ShouldBe("There is no response.");

    [Fact]
    public void JsonChecks_OnAnHtmlBody_SayItIsNotJson() =>
        Check(CheckSource.Json, CheckOperator.Exists, "$.id", response: Response with { Body = "<html>Bad gateway</html>" }).Message.ShouldBe("The body is not valid JSON.");

    [Fact]
    public void Schema_PassesOrListsTheProblems()
    {
        const string schema = """
            { "type": "object", "required": ["id", "email"],
              "properties": { "id": { "type": "integer" }, "name": { "type": "string" } } }
            """;

        var failed = Check(CheckSource.Schema, CheckOperator.Equals, expected: schema);
        var passed = Check(CheckSource.Schema, CheckOperator.Equals, expected: schema.Replace(", \"email\"", string.Empty, StringComparison.Ordinal));

        passed.Passed.ShouldBeTrue();
        failed.Passed.ShouldBeFalse();
        failed.Message.ShouldStartWith("The body does not match the schema: ");
        failed.Message.ShouldContain("email");
    }

    [Fact]
    public void Schema_ThatIsNotJson_IsReported() =>
        Check(CheckSource.Schema, CheckOperator.Equals, expected: "{ type: object").Message!.ShouldStartWith("The JSON Schema is not valid: ");

    [Fact]
    public void Variables_AreFilledIn_AndDisabledChecksSkipped()
    {
        ResponseCheck[] checks =
        [
            new(CheckSource.Json, CheckOperator.Equals, "$.{{field}}", "{{expectedId}}"),
            new(CheckSource.Status, CheckOperator.Equals, Expected: "500", IsEnabled: false),
        ];
        var variables = new Dictionary<string, string> { ["{{field}}"] = "id", ["{{expectedId}}"] = "42" };

        var results = ResponseChecker.Run(Response, checks, text => variables.Aggregate(text, (t, v) => t.Replace(v.Key, v.Value, StringComparison.Ordinal)));

        results.ShouldHaveSingleItem().Passed.ShouldBeTrue();
    }

    [Fact]
    public void LongValues_AreShortened() =>
        Check(CheckSource.Body, CheckOperator.Equals, expected: "x", response: Response with { Body = new string('a', 1000) }).Actual!.Length.ShouldBe(301);
}
