using System.Text.Json;
using JetApi.Core.Model;
using JetApi.Core.Variables;
using JetApi.Grpc.Schema;

namespace JetApi.Grpc.Tests;

public sealed class GrpcInvokerTests(TestGrpcServer server) : IClassFixture<TestGrpcServer>, IDisposable
{
    private const string Service = "jetapi.test.v1.EverythingService";

    private static readonly ProtoSchema Schema = TestSchema.Load();

    private readonly GrpcInvoker _invoker = new();

    public void Dispose() => _invoker.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static GrpcRequestDefinition Request(string method, string message = "{}") => new()
    {
        Key = "r",
        Name = "r",
        ServerUrl = "{{grpcUrl}}",
        Service = Service,
        Method = method,
        Message = message,
    };

    private VariableResolver Variables => new([new VariableLayer(VariableSource.Environment, [new("grpcUrl", server.BaseUrl.ToString().TrimEnd('/')), new("token", "t0k3n")])]);

    private async Task<GrpcCallResult> InvokeAsync(GrpcRequestDefinition request, AuthConfig? auth = null, CancellationToken? cancellationToken = null)
    {
        var prepared = GrpcRequestPreparer.Prepare(request, auth ?? AuthConfig.None, Variables, Schema);
        prepared.Errors.ShouldBeEmpty();
        return await _invoker.InvokeUnaryAsync(prepared.Call!, Schema, cancellationToken: cancellationToken ?? Token);
    }

    [Fact]
    public async Task Invoke_Echo_RoundTripsTheMessage()
    {
        const string message = """{ "text": "Merhaba", "i64": "-5", "color": "COLOR_RED", "address": { "city": "İzmir" }, "createdAt": "2026-01-01T00:00:00Z" }""";

        var result = await InvokeAsync(Request("Echo", message));

        result.IsOk.ShouldBeTrue(result.StatusDetail);
        result.StatusName.ShouldBe("OK");
        using var json = JsonDocument.Parse(result.ResponseJson!);
        json.RootElement.GetProperty("text").GetString().ShouldBe("Merhaba");
        json.RootElement.GetProperty("i64").GetString().ShouldBe("-5");
        json.RootElement.GetProperty("color").GetString().ShouldBe("COLOR_RED");
        json.RootElement.GetProperty("address").GetProperty("city").GetString().ShouldBe("İzmir");
        json.RootElement.GetProperty("createdAt").GetString().ShouldBe("2026-01-01T00:00:00Z");
        result.ResponseHeaders.ShouldContain(new KeyValuePair<string, string>("x-served-by", "test"));
        result.Trailers.ShouldContain(new KeyValuePair<string, string>("x-trailer", "done"));
        result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Invoke_ErrorStatus_IsReportedWithDetailAndTrailers()
    {
        var result = await InvokeAsync(Request("Fail"));

        result.Outcome.ShouldBe(GrpcCallOutcome.Completed);
        result.IsOk.ShouldBeFalse();
        result.StatusCode.ShouldBe(5);
        result.StatusName.ShouldBe("NotFound");
        result.StatusDetail.ShouldBe("User 42 was not found.");
        result.Trailers.ShouldContain(new KeyValuePair<string, string>("x-error-code", "USR-404"));
        result.ResponseJson.ShouldBeNull();
    }

    [Fact]
    public async Task Invoke_SendsMetadataAndAuth()
    {
        var request = Request("EchoMetadata") with { Metadata = [new("X-Tenant", "acme"), new("x-off", "1", IsEnabled: false)] };

        var result = await InvokeAsync(request, new BearerAuth("{{token}}"));

        using var json = JsonDocument.Parse(result.ResponseJson!);
        json.RootElement.GetProperty("x-tenant").GetString().ShouldBe("acme");
        json.RootElement.GetProperty("authorization").GetString().ShouldBe("Bearer t0k3n");
        json.RootElement.TryGetProperty("x-off", out _).ShouldBeFalse();
        result.SentMetadata.ShouldContain(new KeyValuePair<string, string>("x-tenant", "acme"));
    }

    [Fact]
    public async Task Invoke_DeadlineExceeded()
    {
        var request = Request("Slow") with { Settings = new GrpcRequestSettings(Deadline: TimeSpan.FromMilliseconds(200)) };

        var result = await InvokeAsync(request);

        result.StatusName.ShouldBe("DeadlineExceeded");
    }

    [Fact]
    public async Task Invoke_Cancelled()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await InvokeAsync(Request("Slow"), cancellationToken: cancellation.Token);

        result.Outcome.ShouldBe(GrpcCallOutcome.Cancelled);
    }

    [Fact]
    public async Task Invoke_UnreachableServer_IsUnavailable()
    {
        var request = Request("Echo") with { ServerUrl = "http://127.0.0.1:1" };

        var result = await InvokeAsync(request);

        result.StatusName.ShouldBe("Unavailable");
        result.StatusDetail.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("", "Echo", "{}", "server URL is empty")]
    [InlineData("localhost:5000", "Echo", "{}", "not a valid server URL")]
    [InlineData("http://localhost:5000/path", "Echo", "{}", "not a valid server URL")]
    [InlineData("http://localhost:5000", "Missing", "{}", "Method 'Missing' is not defined")]
    [InlineData("http://localhost:5000", "Watch", "{}", "streaming")]
    [InlineData("http://localhost:5000", "Echo", "{\"i32\": \"x\"}", "Request message: $.i32")]
    public void Prepare_ReportsProblems(string url, string method, string message, string error)
    {
        var request = Request(method, message) with { ServerUrl = url };

        var result = GrpcRequestPreparer.Prepare(request, AuthConfig.None, VariableResolver.Empty, Schema);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains(error, StringComparison.Ordinal));
    }

    [Fact]
    public void Prepare_UnknownService_And_ApiKeyInQuery_AreErrors()
    {
        var unknown = Request("Echo") with { ServerUrl = "http://localhost:1", Service = "x.Y" };
        GrpcRequestPreparer.Prepare(unknown, AuthConfig.None, VariableResolver.Empty, Schema).Errors.ShouldHaveSingleItem().ShouldContain("Service 'x.Y'");

        var apiKey = Request("Echo") with { ServerUrl = "http://localhost:1" };
        GrpcRequestPreparer.Prepare(apiKey, new ApiKeyAuth("k", "v", ApiKeyLocation.Query), VariableResolver.Empty, Schema)
            .Errors.ShouldHaveSingleItem().ShouldContain("no query string");
    }
}
