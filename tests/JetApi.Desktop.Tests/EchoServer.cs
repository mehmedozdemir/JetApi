using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JetApi.Desktop.Tests;

/// <summary>A small local HTTP server for end-to-end tab tests.</summary>
public sealed class EchoServer : IAsyncLifetime
{
    private WebApplication? _app;

    public string BaseUrl { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.Map("/echo", (HttpContext context) => Results.Json(new
        {
            method = context.Request.Method,
            query = context.Request.QueryString.Value,
            authorization = context.Request.Headers.Authorization.ToString(),
            cookie = context.Request.Headers.Cookie.ToString(),
        }));
        _app.Map("/login", (HttpContext context) =>
        {
            context.Response.Headers.Append("Set-Cookie", "session=abc123; Path=/; HttpOnly");
            return Results.Ok();
        });
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
