using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JetApi.Cli.Tests;

/// <summary>A local HTTP server that answers every path with what it received.</summary>
public sealed class TestServer : IAsyncLifetime
{
    private WebApplication? _app;

    public string BaseUrl { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.Map("/login", (HttpContext context) =>
        {
            context.Response.Headers.Append("Set-Cookie", "session=abc123; Path=/; HttpOnly");
            return Results.Json(new { token = "tok-" + context.Request.Query["user"] });
        });
        _app.Map("/missing", () => Results.NotFound(new { error = "not found" }));
        _app.MapFallback((HttpContext context) => Results.Json(new
        {
            method = context.Request.Method,
            path = context.Request.Path.Value,
            query = context.Request.QueryString.Value,
            authorization = context.Request.Headers.Authorization.ToString(),
            cookie = context.Request.Headers.Cookie.ToString(),
        }));
        await _app.StartAsync();
        BaseUrl = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
