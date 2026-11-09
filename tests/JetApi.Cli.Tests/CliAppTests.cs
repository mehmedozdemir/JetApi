using System.Globalization;
using System.Xml.Linq;
using JetApi.Core.Model;
using JetApi.Storage;

namespace JetApi.Cli.Tests;

public sealed class CliAppTests(TestServer server) : IClassFixture<TestServer>, IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("jetapi-cli-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static HttpRequestDefinition Request(string key, string name, string url, ResponseCheck[]? checks = null, Extraction[]? extractions = null) =>
        new() { Key = key, Name = name, Url = url, Checks = checks ?? [], Extractions = extractions ?? [] };

    private static ResponseCheck Status(int code) => new(CheckSource.Status, CheckOperator.Equals, Expected: code.ToString(CultureInfo.InvariantCulture));

    private async Task SaveAsync(params CollectionNode[] items)
    {
        var workspace = new Workspace
        {
            Name = "Shop",
            Collections =
            [
                new ApiCollection { Key = "users", Name = "Users", Children = items, Variables = [new Variable("base", server.BaseUrl)] },
            ],
            Environments = [new ApiEnvironment { Key = "dev", Name = "Development", Variables = [new Variable("user", "ali")] }],
        };
        await WorkspaceStore.SaveAsync(_root, workspace, TestContext.Current.CancellationToken);
    }

    private async Task<(int Code, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await CliApp.RunAsync(["run", _root, .. args], output, error, color: false, TestContext.Current.CancellationToken);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Passing_Run_ExitsWithZero_AndPrintsEveryCheck()
    {
        await SaveAsync(
            Request("list", "List users", "{{base}}/users", [Status(200), new ResponseCheck(CheckSource.Json, CheckOperator.Equals, "$.path", "/users")]),
            new Folder { Key = "admin", Name = "Admin", Children = [Request("audit", "Audit", "{{base}}/audit?u={{user}}", [Status(200)])] });

        var (code, output, error) = await RunAsync("-e", "Development");

        error.ShouldBeEmpty();
        code.ShouldBe(0);
        output.ShouldContain("Shop · Development");
        output.ShouldContain("Iteration 1/1");
        output.ShouldContain("✓ GET List users · 200 OK");
        output.ShouldContain("✓ Status equals 200");
        output.ShouldContain("✓ $.path equals /users");
        output.ShouldContain("✓ GET Audit · 200 OK");
        output.ShouldContain("Passed · 2 requests · 3 checks passed · 0 failed");
    }

    [Fact]
    public async Task FailedCheck_ExitsWithOne_AndWritesReports()
    {
        await SaveAsync(Request("missing", "Missing", "{{base}}/missing", [Status(200)]));
        var junit = Path.Combine(_root, "out", "junit.xml");
        var html = Path.Combine(_root, "out", "report.html");

        var (code, output, _) = await RunAsync("--junit", junit, "--html", html);

        code.ShouldBe(1);
        output.ShouldContain("✗ GET Missing · 404 Not Found");
        output.ShouldContain("✗ Status equals 200 · ");
        output.ShouldContain("Failed · 1 requests · 0 checks passed · 1 failed");
        output.ShouldContain("JUnit report: " + junit);
        XDocument.Load(junit).Root!.Attribute("failures")!.Value.ShouldBe("1");
        (await File.ReadAllTextAsync(html, TestContext.Current.CancellationToken)).ShouldContain("Missing");
    }

    [Fact]
    public async Task NoResponse_ExitsWithOne()
    {
        await SaveAsync(Request("bad", "Bad", "http://127.0.0.1:1/"));

        var (code, output, _) = await RunAsync("--timeout", "3000");

        code.ShouldBe(1);
        output.ShouldContain("✗ GET Bad");
        output.ShouldContain("1 without a response");
    }

    [Fact]
    public async Task Target_PicksAFolder_ByKeyOrName()
    {
        await SaveAsync(
            Request("list", "List users", "{{base}}/users"),
            new Folder { Key = "admin", Name = "Admin", Children = [Request("audit", "Audit", "{{base}}/audit")] });

        var (code, output, _) = await RunAsync("Users/admin");

        code.ShouldBe(0);
        output.ShouldContain("JetApi");
        output.ShouldContain("· Admin");
        output.ShouldContain("Audit");
        output.ShouldNotContain("List users");
    }

    [Theory]
    [InlineData("users/nope", "There is nothing named 'nope' in Users. Choose from: List users")]
    [InlineData("orders", "There is nothing named 'orders' in Shop. Choose from: Users")]
    public async Task UnknownTarget_ExitsWithTwo(string target, string message)
    {
        await SaveAsync(Request("list", "List users", "{{base}}/users"));

        var (code, _, error) = await RunAsync(target);

        code.ShouldBe(2);
        error.ShouldContain(message);
    }

    [Fact]
    public async Task UnknownEnvironment_ExitsWithTwo()
    {
        await SaveAsync(Request("list", "List users", "{{base}}/users"));

        var (code, _, error) = await RunAsync("-e", "prod");

        code.ShouldBe(2);
        error.ShouldContain("There is no environment 'prod'. Environments: Development");
    }

    [Fact]
    public async Task NotAWorkspace_ExitsWithTwo()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await CliApp.RunAsync(["run", Path.Combine(_root, "nothing")], output, error, false, TestContext.Current.CancellationToken);

        code.ShouldBe(2);
        error.ToString().ShouldContain("is not a JetApi workspace");
    }

    [Fact]
    public async Task DataFile_DrivesIterations_AndVarsOverride()
    {
        await SaveAsync(Request("audit", "Audit", "{{base}}/audit?u={{user}}&t={{tag}}", [new ResponseCheck(CheckSource.Json, CheckOperator.Contains, "$.query", "t=x")]));
        var data = Path.Combine(_root, "rows.csv");
        await File.WriteAllTextAsync(data, "user\nayse\nmehmet\n", TestContext.Current.CancellationToken);

        var (code, output, _) = await RunAsync("-e", "dev", "--data", data, "--var", "tag=x");

        code.ShouldBe(0);
        output.ShouldContain("Iteration 1/2 · user=ayse");
        output.ShouldContain("Iteration 2/2 · user=mehmet");
        output.ShouldContain("2 requests · 2 checks passed");
    }

    [Fact]
    public async Task ExtractedValues_AndCookies_PassToLaterRequests()
    {
        await SaveAsync(
            Request("login", "Login", "{{base}}/login?user={{user}}", extractions: [new Extraction(ExtractionSource.Json, "$.token", "token")]),
            Request("me", "Me", "{{base}}/me?t={{token}}", [
                new ResponseCheck(CheckSource.Json, CheckOperator.Equals, "$.query", "?t=tok-ali"),
                new ResponseCheck(CheckSource.Json, CheckOperator.Equals, "$.cookie", "session=abc123"),
            ]));

        var (code, output, _) = await RunAsync("-e", "dev");

        output.ShouldContain("2 checks passed · 0 failed");
        code.ShouldBe(0);
    }

    [Fact]
    public async Task Persist_SavesExtractedValues_ToTheEnvironment()
    {
        await SaveAsync(Request("login", "Login", "{{base}}/login?user={{user}}", extractions: [new Extraction(ExtractionSource.Json, "$.token", "token", ExtractionTarget.Environment)]));

        (await RunAsync("-e", "dev")).Code.ShouldBe(0);
        var before = await WorkspaceStore.LoadAsync(_root, TestContext.Current.CancellationToken);
        before.Workspace.FindEnvironment("dev")!.Variables.ShouldNotContain(v => v.Name == "token");

        (await RunAsync("-e", "dev", "--persist")).Code.ShouldBe(0);
        var after = await WorkspaceStore.LoadAsync(_root, TestContext.Current.CancellationToken);
        after.Workspace.FindEnvironment("dev")!.Variables.ShouldContain(new Variable("token", "tok-ali"));
    }

    [Fact]
    public async Task KeptPostmanScripts_AreReported_AsNotRun()
    {
        await SaveAsync(
            Request("list", "List users", "{{base}}/users") with { Scripts = new ScriptSet(Test: "console.log(1);") },
            Request("audit", "Audit", "{{base}}/audit"));

        var (code, _, error) = await RunAsync();

        code.ShouldBe(0);
        error.ShouldContain("1 requests have Postman scripts kept from an import; they do not run.");
    }

    [Fact]
    public async Task Bail_StopsAtTheFirstFailure()
    {
        await SaveAsync(Request("missing", "Missing", "{{base}}/missing", [Status(200)]), Request("list", "List users", "{{base}}/users"));

        var (code, output, _) = await RunAsync("--bail");

        code.ShouldBe(1);
        output.ShouldContain("Stopped at the first failure (--bail).");
        output.ShouldNotContain("List users");
    }

    [Fact]
    public async Task Lang_Tr_PrintsTurkish()
    {
        await SaveAsync(Request("list", "List users", "{{base}}/users", [Status(200)]));
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            var (code, output, _) = await RunAsync("--lang", "tr");

            code.ShouldBe(0);
            output.ShouldContain("Tekrar 1/1");
            output.ShouldContain("✓ Status eşittir 200");
            output.ShouldContain("Geçti · 1 istek · 1 test geçti · 0 kaldı");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task Help_PrintsUsage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await CliApp.RunAsync(["--help"], output, error, false, TestContext.Current.CancellationToken);

        code.ShouldBe(0);
        output.ToString().ShouldContain("jetapi run <workspace>");
        output.ToString().ShouldContain("Exit codes: 0 all passed");
    }
}
