using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using JetApi.Grpc.Tests.Generated;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JetApi.Grpc.Tests;

/// <summary>A real gRPC server (plaintext HTTP/2) implemented with generated code.</summary>
public sealed class TestGrpcServer : IAsyncLifetime
{
    private WebApplication? _app;

    public Uri BaseUrl { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        _app = builder.Build();
        _app.MapGrpcService<EverythingServiceImpl>();
        await _app.StartAsync();

        BaseUrl = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private sealed class EverythingServiceImpl : EverythingService.EverythingServiceBase
    {
        public override async Task<Everything> Echo(Everything request, ServerCallContext context)
        {
            await context.WriteResponseHeadersAsync(new Metadata { { "x-served-by", "test" } });
            context.ResponseTrailers.Add("x-trailer", "done");
            return request;
        }

        public override Task<Empty> Fail(Empty request, ServerCallContext context) =>
            throw new RpcException(new Status(StatusCode.NotFound, "User 42 was not found."), new Metadata { { "x-error-code", "USR-404" } });

        public override async Task<Empty> Slow(Empty request, ServerCallContext context)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), context.CancellationToken);
            return new Empty();
        }

        public override Task<Struct> EchoMetadata(Empty request, ServerCallContext context)
        {
            var result = new Struct();
            foreach (var entry in context.RequestHeaders.Where(h => !h.IsBinary))
            {
                result.Fields[entry.Key] = Value.ForString(entry.Value);
            }

            return Task.FromResult(result);
        }
    }
}
