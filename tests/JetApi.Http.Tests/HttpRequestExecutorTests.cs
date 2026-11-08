using System.Net;
using System.Text;
using System.Text.Json;
using JetApi.Core.Model;

namespace JetApi.Http.Tests;

public sealed class HttpRequestExecutorTests(TestServer server) : IClassFixture<TestServer>, IDisposable
{
    private readonly HttpRequestExecutor _executor = new(new HttpExecutorOptions { MaxResponseBodyBytes = 1000 });

    public void Dispose() => _executor.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private PreparedHttpRequest Request(string path, string method = "GET", PreparedBody? body = null, HttpRequestSettings? settings = null, params KeyValuePair<string, string>[] headers) =>
        new(method, new Uri(server.BaseUrl, path), headers, body ?? PreparedBody.None, settings ?? HttpRequestSettings.Default);

    [Fact]
    public async Task Execute_SendsMethodHeadersAndBody_AndReportsWhatWasSent()
    {
        var body = new BytesBody(Encoding.UTF8.GetBytes("{\"a\":\"ü\"}"), "application/json");

        var result = await _executor.ExecuteAsync(Request("/echo?x=1", "POST", body, headers: new KeyValuePair<string, string>("X-Test", "yes")), Token);

        result.Outcome.ShouldBe(HttpExecutionOutcome.Completed);
        result.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var echo = JsonDocument.Parse(result.Body);
        echo.RootElement.GetProperty("method").GetString().ShouldBe("POST");
        echo.RootElement.GetProperty("query").GetString().ShouldBe("?x=1");
        echo.RootElement.GetProperty("body").GetString().ShouldBe("{\"a\":\"ü\"}");
        var headers = echo.RootElement.GetProperty("headers");
        headers.GetProperty("X-Test").GetString().ShouldBe("yes");
        headers.GetProperty("Content-Type").GetString().ShouldBe("application/json");
        headers.GetProperty("User-Agent").GetString()!.ShouldStartWith("JetApi/");

        result.Sent.Headers.ShouldContain(new KeyValuePair<string, string>("X-Test", "yes"));
        result.Sent.Headers.ShouldContain(h => h.Key == "Accept-Encoding");
        result.Sent.BodySize.ShouldBe(body.Content.Length);
    }

    [Fact]
    public async Task Execute_UserContentTypeHeader_OverridesBodyDefault()
    {
        var body = new BytesBody("x"u8.ToArray(), "text/plain; charset=utf-8");

        var result = await _executor.ExecuteAsync(Request("/echo", "POST", body, headers: new KeyValuePair<string, string>("Content-Type", "application/vnd.custom+json")), Token);

        using var echo = JsonDocument.Parse(result.Body);
        echo.RootElement.GetProperty("headers").GetProperty("Content-Type").GetString().ShouldBe("application/vnd.custom+json");
    }

    [Fact]
    public async Task Execute_SendsFormAndMultipartBodies()
    {
        var form = await _executor.ExecuteAsync(Request("/echo", "POST", new FormBody([new("a", "1 2"), new("ş", "&")])), Token);
        using (var echo = JsonDocument.Parse(form.Body))
        {
            echo.RootElement.GetProperty("body").GetString().ShouldBe("a=1+2&%C5%9F=%26");
        }

        var file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "file-content", Token);
        try
        {
            var multipart = await _executor.ExecuteAsync(
                Request("/echo", "POST", new MultipartBody([new("text", "hello", false, null), new("upload", file, true, "text/plain")])),
                Token);
            using var echo = JsonDocument.Parse(multipart.Body);
            var sentBody = echo.RootElement.GetProperty("body").GetString()!;
            sentBody.ShouldContain("name=text");
            sentBody.ShouldContain("hello");
            sentBody.ShouldContain($"filename={Path.GetFileName(file)}");
            sentBody.ShouldContain("file-content");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Execute_MeasuresTimings_AndReusesConnections()
    {
        var first = await _executor.ExecuteAsync(Request("/echo"), Token);
        var second = await _executor.ExecuteAsync(Request("/echo"), Token);

        first.Timings!.Dns.ShouldNotBeNull();
        first.Timings.Connect.ShouldNotBeNull();
        first.Timings.IsConnectionReused.ShouldBeFalse();
        first.Timings.Total.ShouldBeGreaterThanOrEqualTo(first.Timings.TimeToFirstByte);
        first.RemoteEndPoint.ShouldNotBeNull().ShouldContain("127.0.0.1");
        second.Timings!.IsConnectionReused.ShouldBeTrue();
    }

    [Fact]
    public async Task Execute_ReturnsErrorStatusCodes_AsCompleted()
    {
        var result = await _executor.ExecuteAsync(Request("/status/503"), Token);

        result.Outcome.ShouldBe(HttpExecutionOutcome.Completed);
        result.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Execute_FollowsRedirects_OnlyWhenEnabled()
    {
        var followed = await _executor.ExecuteAsync(Request("/redirect"), Token);
        var notFollowed = await _executor.ExecuteAsync(Request("/redirect", settings: new HttpRequestSettings(FollowRedirects: false)), Token);

        followed.StatusCode.ShouldBe(HttpStatusCode.OK);
        followed.FinalUrl!.AbsolutePath.ShouldBe("/echo");
        notFollowed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        notFollowed.Headers.ShouldContain(h => h.Key == "Location" && h.Value == "/echo");
    }

    [Fact]
    public async Task Execute_DecompressesBody_AndKeepsDuplicateHeaders()
    {
        var result = await _executor.ExecuteAsync(Request("/gzip"), Token);

        Encoding.UTF8.GetString(result.Body.Span).ShouldBe("çok güzel");
        result.Headers.Where(h => h.Key == "X-Multi").Select(h => h.Value).ShouldBe(["a", "b"]);
        result.ContentType.ShouldBe("text/plain; charset=utf-8");
    }

    [Fact]
    public async Task Execute_TruncatesLargeBodies()
    {
        var result = await _executor.ExecuteAsync(Request("/bytes/5000"), Token);

        result.IsBodyTruncated.ShouldBeTrue();
        result.Body.Length.ShouldBe(1000);
    }

    [Fact]
    public async Task Execute_TimesOut()
    {
        var result = await _executor.ExecuteAsync(Request("/slow", settings: new HttpRequestSettings(Timeout: TimeSpan.FromMilliseconds(200))), Token);

        result.Outcome.ShouldBe(HttpExecutionOutcome.TimedOut);
        result.ErrorMessage.ShouldBe("No response within 200 ms.");
    }

    [Fact]
    public async Task Execute_ReportsUserCancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await _executor.ExecuteAsync(Request("/slow"), cancellation.Token);

        result.Outcome.ShouldBe(HttpExecutionOutcome.Cancelled);
    }

    [Fact]
    public async Task Execute_ConnectionRefused_IsReportedClearly()
    {
        var unusedPort = new Uri("http://127.0.0.1:1/");
        var request = new PreparedHttpRequest("GET", unusedPort, [], PreparedBody.None, HttpRequestSettings.Default);

        var result = await _executor.ExecuteAsync(request, Token);

        result.Outcome.ShouldBe(HttpExecutionOutcome.Failed);
        result.ErrorMessage!.ShouldContain("Connection refused");
    }

    [Fact]
    public async Task Execute_EndToEnd_FromDefinition()
    {
        var definition = new HttpRequestDefinition
        {
            Key = "r",
            Name = "r",
            Method = "PUT",
            Url = "{{baseUrl}}/echo",
            QueryParameters = [new("id", "{{id}}")],
            Body = HttpBody.Json("{\"id\": {{id}}}"),
        };
        var variables = new Core.Variables.VariableResolver(
        [
            new Core.Variables.VariableLayer(Core.Variables.VariableSource.Environment, [new("baseUrl", server.BaseUrl.ToString().TrimEnd('/')), new("id", "7")]),
        ]);

        var prepared = HttpRequestPreparer.Prepare(definition, new BearerAuth("abc"), variables, Path.GetTempPath());
        var result = await _executor.ExecuteAsync(prepared.Request!, Token);

        using var echo = JsonDocument.Parse(result.Body);
        echo.RootElement.GetProperty("method").GetString().ShouldBe("PUT");
        echo.RootElement.GetProperty("query").GetString().ShouldBe("?id=7");
        echo.RootElement.GetProperty("body").GetString().ShouldBe("{\"id\": 7}");
        echo.RootElement.GetProperty("headers").GetProperty("Authorization").GetString().ShouldBe("Bearer abc");
    }
}
