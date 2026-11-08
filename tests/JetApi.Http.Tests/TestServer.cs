using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JetApi.Http.Tests;

/// <summary>A real Kestrel server on a random loopback port, shared by all tests in the class.</summary>
public sealed class TestServer : IAsyncLifetime
{
    private WebApplication? _app;

    public Uri BaseUrl { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.Map("/echo", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            return Results.Json(new
            {
                method = context.Request.Method,
                path = context.Request.Path.Value,
                query = context.Request.QueryString.Value,
                headers,
                body,
            });
        });
        _app.MapGet("/status/{code:int}", (int code) => Results.StatusCode(code));
        _app.MapGet("/redirect", () => Results.Redirect("/echo"));
        _app.MapGet("/slow", async (CancellationToken token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return Results.Ok();
        });
        _app.MapGet("/bytes/{count:int}", (int count) => Results.Bytes(new byte[count], "application/octet-stream"));
        _app.MapGet("/gzip", (HttpContext context) =>
        {
            context.Response.Headers.ContentEncoding = "gzip";
            context.Response.Headers.Append("X-Multi", "a");
            context.Response.Headers.Append("X-Multi", "b");
            using var buffer = new MemoryStream();
            using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write("çok güzel"u8);
            }

            return Results.Bytes(buffer.ToArray(), "text/plain; charset=utf-8");
        });

        OAuthTestEndpoints.Map(_app);

        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        BaseUrl = new Uri(address);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
