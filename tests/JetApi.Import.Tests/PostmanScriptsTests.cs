using JetApi.Core.Model;

namespace JetApi.Import.Tests;

/// <summary>Postman test scripts turned into checks and extractions (ADR-0010).</summary>
public sealed class PostmanScriptsTests
{
    private static ResponseCheck Json(CheckOperator op, string path, string expected = "") => new(CheckSource.Json, op, path, expected);

    [Fact]
    public void Snippets_FromPostman_AreConverted()
    {
        // The snippets Postman offers in its test editor.
        var result = PostmanScripts.ConvertTest("""
            pm.test("Status code is 200", function () {
                pm.response.to.have.status(200);
            });
            pm.test("Response time is less than 200ms", function () {
                pm.expect(pm.response.responseTime).to.be.below(200);
            });
            pm.test("Content-Type is present", function () {
                pm.response.to.have.header("Content-Type");
            });
            pm.test("Body matches string", function () {
                pm.expect(pm.response.text()).to.include("string_you_want_to_search");
            });
            pm.test("Body is correct", function () {
                pm.response.to.have.body("response_body_string");
            });
            pm.test("Your test name", function () {
                var jsonData = pm.response.json();
                pm.expect(jsonData.value).to.eql(100);
            });
            """);

        result.IsComplete.ShouldBeTrue();
        result.Checks.ShouldBe([
            new ResponseCheck(CheckSource.Status, CheckOperator.Equals, Expected: "200"),
            new ResponseCheck(CheckSource.Time, CheckOperator.LessThan, Expected: "200"),
            new ResponseCheck(CheckSource.Header, CheckOperator.Exists, "Content-Type"),
            new ResponseCheck(CheckSource.Body, CheckOperator.Equals, Expected: "response_body_string"),
            new ResponseCheck(CheckSource.Body, CheckOperator.Contains, Expected: "string_you_want_to_search"),
            Json(CheckOperator.Equals, "$.value", "100"),
        ], ignoreOrder: true);
        result.Extractions.ShouldBeEmpty();
    }

    [Fact]
    public void JsonAssertions_BecomeJsonPathChecks()
    {
        var result = PostmanScripts.ConvertTest("""
            const body = pm.response.json();
            pm.test('user', () => {
                pm.expect(body.data[0].name).to.equal('Ali');
                pm.expect(body.data[0]["e-mail"]).to.include("@example.com");
                pm.expect(body.total).to.be.above(5);
                pm.expect(body.items).to.not.include("x");
                pm.expect(body.id).to.exist;
                pm.expect(body.error).to.not.exist;
                pm.expect(body.active).to.be.true;
                pm.expect(body).to.have.property('id');
                pm.expect(body.meta).to.have.property("page", 1);
                pm.expect(body.code).to.match(/^ab+c$/i);
                pm.expect(body.zip).to.eql("01234");
                pm.expect(body.count).to.eql("7");
            });
            """);

        result.IsComplete.ShouldBeTrue();
        result.Checks.ShouldBe([
            Json(CheckOperator.Exists, "$.id"),
            Json(CheckOperator.Equals, "$.meta.page", "1"),
            Json(CheckOperator.Equals, "$.active", "true"),
            Json(CheckOperator.Equals, "$.data[0].name", "Ali"),
            Json(CheckOperator.Contains, "$.data[0]['e-mail']", "@example.com"),
            Json(CheckOperator.GreaterThan, "$.total", "5"),
            Json(CheckOperator.NotContains, "$.items", "x"),
            Json(CheckOperator.Exists, "$.id"),
            Json(CheckOperator.NotExists, "$.error"),
            Json(CheckOperator.Matches, "$.code", "(?i)^ab+c$"),
            Json(CheckOperator.Equals, "$.zip", "01234"),
            Json(CheckOperator.Equals, "$.count", "\"7\""),
        ], ignoreOrder: true);
    }

    [Fact]
    public void VariableSetters_BecomeExtractions()
    {
        var result = PostmanScripts.ConvertTest("""
            var jsonData = JSON.parse(responseBody);
            pm.environment.set("token", jsonData.access_token);
            pm.collectionVariables.set('userId', jsonData.user.id);
            pm.globals.set("location", pm.response.headers.get("Location"));
            pm.variables.set("status", pm.response.code);
            postman.setEnvironmentVariable("raw", responseBody);
            """);

        result.IsComplete.ShouldBeTrue();
        result.Checks.ShouldBeEmpty();
        result.Extractions.ShouldBe([
            new Extraction(ExtractionSource.Json, "$.access_token", "token", ExtractionTarget.Environment),
            new Extraction(ExtractionSource.Json, "$.user.id", "userId", ExtractionTarget.Collection),
            new Extraction(ExtractionSource.Header, "Location", "location", ExtractionTarget.Global),
            new Extraction(ExtractionSource.Status, string.Empty, "status"),
            new Extraction(ExtractionSource.Body, string.Empty, "raw", ExtractionTarget.Environment),
        ], ignoreOrder: true);
    }

    [Fact]
    public void OlderForms_AreConverted()
    {
        var result = PostmanScripts.ConvertTest("""
            tests["Status code is 200"] = responseCode.code === 200;
            tests["Fast"] = responseTime < 500;
            tests["Has id"] = responseBody.has("id");
            """);

        result.IsComplete.ShouldBeTrue();
        result.Checks.ShouldBe([
            new ResponseCheck(CheckSource.Status, CheckOperator.Equals, Expected: "200"),
            new ResponseCheck(CheckSource.Time, CheckOperator.LessThan, Expected: "500"),
            new ResponseCheck(CheckSource.Body, CheckOperator.Contains, Expected: "id"),
        ], ignoreOrder: true);
    }

    [Theory]
    [InlineData("pm.expect(pm.response.json().items.length).to.be.above(0);")]
    [InlineData("pm.expect(pm.response.json()).to.eql({ id: 1 });")]
    [InlineData("pm.expect(pm.response.json().kind).to.be.a('string');")]
    [InlineData("if (pm.response.code === 200) { pm.environment.set('ok', 'yes'); }")]
    [InlineData("console.log(pm.response.json());")]
    [InlineData("pm.expect(pm.response.json().code).to.match(/x/g);")]
    public void UnknownStatements_LeaveTheConversionIncomplete(string script)
    {
        PostmanScripts.ConvertTest(script).IsComplete.ShouldBeFalse();
    }

    [Fact]
    public void Comments_AndStringsWithSlashes_AreHandled()
    {
        var result = PostmanScripts.ConvertTest("""
            // checks the redirect target
            /* block
               comment */
            pm.response.to.have.header("Location", "https://example.com/next"); // trailing
            """);

        result.IsComplete.ShouldBeTrue();
        result.Checks.ShouldBe([new ResponseCheck(CheckSource.Header, CheckOperator.Equals, "Location", "https://example.com/next")]);
    }

    [Fact]
    public void Import_ConvertsRequestTests_AndKeepsTheRest()
    {
        const string Collection = """
            {
              "info": { "name": "Shop", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "event": [ { "listen": "prerequest", "script": { "exec": [ "pm.variables.set('ts', Date.now());" ] } } ],
              "item": [
                {
                  "name": "Admin",
                  "event": [ { "listen": "test", "script": { "exec": [ "pm.test('ok', () => pm.response.to.have.status(200));" ] } } ],
                  "item": [
                    {
                      "name": "Login",
                      "event": [ { "listen": "test", "script": { "exec": [ "var j = pm.response.json();", "pm.environment.set('token', j.token);", "pm.test('ok', function () {", "  pm.response.to.have.status(200);", "});" ] } } ],
                      "request": { "method": "POST", "url": "https://x/login" }
                    },
                    {
                      "name": "Orders",
                      "event": [ { "listen": "test", "script": { "exec": "pm.response.to.have.status(200);\nconsole.log('done');" } } ],
                      "request": { "method": "GET", "url": "https://x/orders" }
                    }
                  ]
                }
              ]
            }
            """;

        var result = PostmanImporter.ImportCollection(Collection, []);

        var collection = result.Value.ShouldNotBeNull().Collection;
        collection.Scripts.ShouldBe(new ScriptSet("pm.variables.set('ts', Date.now());"));
        var admin = collection.Children.OfType<Folder>().Single();
        admin.Scripts.Test.ShouldBe("pm.test('ok', () => pm.response.to.have.status(200));");

        var login = admin.Children.OfType<HttpRequestDefinition>().Single(r => r.Name == "Login");
        login.Checks.ShouldBe([new ResponseCheck(CheckSource.Status, CheckOperator.Equals, Expected: "200")]);
        login.Extractions.ShouldBe([new Extraction(ExtractionSource.Json, "$.token", "token", ExtractionTarget.Environment)]);
        login.Scripts.ShouldBe(ScriptSet.None);

        var orders = admin.Children.OfType<HttpRequestDefinition>().Single(r => r.Name == "Orders");
        orders.Checks.ShouldBe([new ResponseCheck(CheckSource.Status, CheckOperator.Equals, Expected: "200")]);
        orders.Scripts.Test.ShouldBe("pm.response.to.have.status(200);\nconsole.log('done');");

        result.Issues.ShouldContain(new ImportIssue("Collection", "Pre-request script is kept in the file but does not run; JetApi does not run scripts yet."));
        result.Issues.ShouldContain(new ImportIssue("Admin", "Test script is kept in the file but does not run; JetApi does not run scripts yet."));
        result.Issues.ShouldContain(new ImportIssue("Admin / Login", "Test script converted: 1 checks and 1 extraction rules.", ImportIssueSeverity.Info));
        result.Issues.ShouldContain(new ImportIssue("Admin / Orders", "Test script partly converted: 1 checks and 0 extraction rules. The rest is kept in the file but does not run."));
    }

    [Fact]
    public void PartlyUnderstood_KeepsWhatItCan()
    {
        var result = PostmanScripts.ConvertTest("""
            pm.test("ok", () => pm.response.to.have.status(201));
            pm.test("custom", () => { pm.expect(_.isEmpty(pm.response.json())).to.be.false; });
            """);

        result.IsComplete.ShouldBeFalse();
        result.Checks.ShouldBe([new ResponseCheck(CheckSource.Status, CheckOperator.Equals, Expected: "201")]);
    }
}
