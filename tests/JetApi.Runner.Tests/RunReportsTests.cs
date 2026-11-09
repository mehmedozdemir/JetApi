using System.Xml.Linq;
using JetApi.Core.Model;
using JetApi.Testing;

namespace JetApi.Runner.Tests;

public sealed class RunReportsTests
{
    private static readonly ReportInfo Info = new("Users <API>", "Development", new DateTimeOffset(2026, 11, 9, 10, 30, 0, TimeSpan.Zero));

    private static RunRequest Request(string name, params string[] folders) =>
        new("users", [.. folders, name.ToLowerInvariant()], new HttpRequestDefinition { Key = name.ToLowerInvariant(), Name = name, Method = "POST" });

    private static RequestRunResult Result(int iteration, RunRequest request, string? error = null, params CheckResult[] checks) =>
        new(iteration, request,
            new SendResult(error is null ? new ResponseSnapshot(201, [], "{}", TimeSpan.FromMilliseconds(40)) : ResponseSnapshot.None, error is null ? "201 Created" : string.Empty, error),
            checks, [], TimeSpan.FromMilliseconds(40));

    private static readonly CheckResult StatusPassed = new(new ResponseCheck(CheckSource.Status, CheckOperator.Equals, Expected: "201"), true, "201", null);

    private static readonly CheckResult IdFailed = new(new ResponseCheck(CheckSource.Json, CheckOperator.Exists, "$.id"), false, null, "Nothing at $.id.");

    private static RunSummary Summary() => new(
        [
            Result(1, Request("Create", "admin"), null, StatusPassed, IdFailed),
            Result(1, Request("List")),
            Result(2, Request("Create", "admin"), null, StatusPassed),
            Result(2, Request("List"), "Connection refused"),
        ],
        2, false, false, TimeSpan.FromMilliseconds(160));

    [Fact]
    public void JUnit_HasASuitePerIteration_AndACasePerCheck()
    {
        var xml = XDocument.Parse(RunReports.JUnit(Summary(), Info));

        var root = xml.Root!;
        root.Name.LocalName.ShouldBe("testsuites");
        root.Attribute("tests")!.Value.ShouldBe("5");
        root.Attribute("failures")!.Value.ShouldBe("1");
        root.Attribute("errors")!.Value.ShouldBe("1");
        root.Attribute("time")!.Value.ShouldBe("0.160");
        var suites = root.Elements("testsuite").ToList();
        suites.Select(s => s.Attribute("name")!.Value).ShouldBe(["Users <API> · iteration 1", "Users <API> · iteration 2"]);
        suites[0].Attribute("tests")!.Value.ShouldBe("3");
        suites[0].Attribute("failures")!.Value.ShouldBe("1");

        var cases = suites[0].Elements("testcase").ToList();
        cases.Select(c => c.Attribute("name")!.Value).ShouldBe(["Create · Status equals 201", "Create · $.id exists", "List · 201 Created"]);
        cases[0].Attribute("classname")!.Value.ShouldBe("users.admin");
        cases[2].Attribute("classname")!.Value.ShouldBe("users");
        cases[1].Element("failure")!.Attribute("message")!.Value.ShouldBe("Nothing at $.id.");
        cases[0].Element("failure").ShouldBeNull();

        var error = suites[1].Elements("testcase").Last();
        error.Element("error")!.Attribute("message")!.Value.ShouldBe("Connection refused");
    }

    [Fact]
    public void JUnit_DeclaresUtf8()
    {
        RunReports.JUnit(Summary(), Info).ShouldStartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
    }

    [Fact]
    public void Html_IsSelfContained_AndEscapesText()
    {
        var html = RunReports.Html(Summary(), Info);

        html.ShouldStartWith("<!doctype html>");
        html.ShouldContain("Users &lt;API&gt;");
        html.ShouldNotContain("Users <API>");
        html.ShouldContain("Development");
        html.ShouldContain("Failed");
        html.ShouldContain("2 checks passed");
        html.ShouldContain("1 without a response");
        html.ShouldContain("$.id exists</code> · Nothing at $.id.");
        html.ShouldContain("Connection refused");
        html.ShouldNotContain("<script", Case.Insensitive);
        html.ShouldNotContain("<link", Case.Insensitive);
        html.ShouldNotContain("src=", Case.Insensitive);
    }

    [Fact]
    public void Html_SaysWhenTheRunStoppedEarly()
    {
        var summary = Summary() with { StoppedEarly = true };

        RunReports.Html(summary, Info).ShouldContain("Stopped at the first failure.");
    }

    [Theory]
    [InlineData(CheckSource.Status, CheckOperator.Equals, "", "200", "Status equals 200")]
    [InlineData(CheckSource.Header, CheckOperator.Contains, "Content-Type", "json", "Content-Type contains json")]
    [InlineData(CheckSource.Json, CheckOperator.NotExists, "$.error", "", "$.error is missing")]
    [InlineData(CheckSource.Time, CheckOperator.LessThan, "", "500", "Time less than 500 ms")]
    [InlineData(CheckSource.Body, CheckOperator.Matches, "", "^ok", "Body matches ^ok")]
    [InlineData(CheckSource.Schema, CheckOperator.Matches, "", "{}", "Matches the JSON Schema")]
    public void CheckText_DescribesChecks(CheckSource source, CheckOperator op, string target, string expected, string text)
    {
        CheckText.Describe(new ResponseCheck(source, op, target, expected)).ShouldBe(text);
    }

    [Fact]
    public void CheckText_KnowsEveryOperator()
    {
        foreach (var op in Enum.GetValues<CheckOperator>())
        {
            CheckText.Describe(new ResponseCheck(CheckSource.Body, op, Expected: "x")).ShouldNotContain("[Op_");
        }
    }
}
