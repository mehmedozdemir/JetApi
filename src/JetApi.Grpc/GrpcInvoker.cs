using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using Grpc.Core;
using Grpc.Net.Client;
using JetApi.Grpc.Codec;
using JetApi.Grpc.Schema;

namespace JetApi.Grpc;

/// <summary>Application-wide gRPC options.</summary>
public sealed record GrpcInvokerOptions
{
    /// <summary>Deadline when a request does not set one.</summary>
    public TimeSpan DefaultDeadline { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Largest response message accepted.</summary>
    public int MaxReceiveMessageBytes { get; init; } = 64 * 1024 * 1024;
}

/// <summary>
/// Makes unary gRPC calls without generated code: messages travel as raw bytes and are
/// converted with <see cref="ProtoJsonCodec"/>. Channels are pooled per server. Thread-safe.
/// </summary>
public sealed class GrpcInvoker : IDisposable
{
    private static readonly Marshaller<byte[]> BytesMarshaller = Marshallers.Create(static bytes => bytes, static bytes => bytes);

    private readonly GrpcInvokerOptions _options;
    private readonly ConcurrentDictionary<(string Url, bool ValidateCertificates), GrpcChannel> _channels = new();

    /// <summary>Creates an invoker.</summary>
    public GrpcInvoker(GrpcInvokerOptions? options = null)
    {
        _options = options ?? new GrpcInvokerOptions();
    }

    /// <summary>Calls the method. Never throws for gRPC or network errors; they are reported as a status.</summary>
    /// <param name="call">The prepared call.</param>
    /// <param name="schema">Schema used to decode the response.</param>
    /// <param name="decodeOptions">JSON formatting of the response.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<GrpcCallResult> InvokeUnaryAsync(
        PreparedGrpcCall call,
        ProtoSchema schema,
        ProtoJsonDecodeOptions? decodeOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(schema);

        var channel = _channels.GetOrAdd((call.ServerUrl.GetLeftPart(UriPartial.Authority), call.Settings.ValidateCertificates), CreateChannel);
        var method = new Method<byte[], byte[]>(MethodType.Unary, call.Method.ServiceFullName, call.Method.Name, BytesMarshaller, BytesMarshaller);
        var headers = CreateMetadata(call.Metadata);
        var deadline = DateTime.UtcNow + (call.Settings.Deadline ?? _options.DefaultDeadline);

        var stopwatch = Stopwatch.StartNew();
        using var grpcCall = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new CallOptions(headers, deadline, cancellationToken), call.RequestMessage.ToArray());
        try
        {
            var response = await grpcCall.ResponseAsync.ConfigureAwait(false);
            var responseHeaders = await grpcCall.ResponseHeadersAsync.ConfigureAwait(false);
            var elapsed = stopwatch.Elapsed;

            string json;
            var detail = string.Empty;
            try
            {
                json = new ProtoJsonCodec(schema).Decode(call.Method.OutputType, response, decodeOptions);
            }
            catch (ProtoJsonException exception)
            {
                json = string.Empty;
                detail = $"The response could not be decoded: {exception.Message}";
            }

            return new GrpcCallResult
            {
                Outcome = GrpcCallOutcome.Completed,
                StatusCode = (int)StatusCode.OK,
                StatusName = nameof(StatusCode.OK),
                StatusDetail = detail,
                ResponseJson = json,
                ResponseMessage = response,
                ResponseHeaders = ToPairs(responseHeaders),
                Trailers = ToPairs(grpcCall.GetTrailers()),
                Duration = elapsed,
                SentMetadata = call.Metadata,
            };
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled && cancellationToken.IsCancellationRequested)
        {
            return new GrpcCallResult
            {
                Outcome = GrpcCallOutcome.Cancelled,
                StatusCode = (int)StatusCode.Cancelled,
                StatusName = nameof(StatusCode.Cancelled),
                StatusDetail = "The call was cancelled.",
                Duration = stopwatch.Elapsed,
                SentMetadata = call.Metadata,
            };
        }
        catch (RpcException exception)
        {
            return new GrpcCallResult
            {
                Outcome = GrpcCallOutcome.Completed,
                StatusCode = (int)exception.StatusCode,
                StatusName = exception.StatusCode.ToString(),
                StatusDetail = DescribeStatus(exception),
                ResponseHeaders = await TryGetHeadersAsync(grpcCall).ConfigureAwait(false),
                Trailers = ToPairs(exception.Trailers),
                Duration = stopwatch.Elapsed,
                SentMetadata = call.Metadata,
            };
        }
    }

    /// <summary>Closes all channels.</summary>
    public void Dispose()
    {
        foreach (var channel in _channels.Values)
        {
            channel.Dispose();
        }

        _channels.Clear();
    }

    private GrpcChannel CreateChannel((string Url, bool ValidateCertificates) key)
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        };

        if (!key.ValidateCertificates)
        {
            handler.SslOptions = new SslClientAuthenticationOptions
            {
                // The user explicitly disabled certificate validation for this request.
#pragma warning disable CA5359
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
#pragma warning restore CA5359
            };
        }

        return GrpcChannel.ForAddress(key.Url, new GrpcChannelOptions
        {
            HttpHandler = handler,
            DisposeHttpClient = true,
            MaxReceiveMessageSize = _options.MaxReceiveMessageBytes,
            MaxSendMessageSize = null,
        });
    }

    private static Metadata CreateMetadata(IReadOnlyList<KeyValuePair<string, string>> entries)
    {
        var metadata = new Metadata();
        foreach (var (key, value) in entries)
        {
            if (key.EndsWith(Metadata.BinaryHeaderSuffix, StringComparison.Ordinal))
            {
                metadata.Add(key, Convert.FromBase64String(value));
            }
            else
            {
                metadata.Add(key, value);
            }
        }

        return metadata;
    }

    private static List<KeyValuePair<string, string>> ToPairs(Metadata? metadata) =>
        metadata is null
            ? []
            : [.. metadata.Select(entry => new KeyValuePair<string, string>(entry.Key, entry.IsBinary ? Convert.ToBase64String(entry.ValueBytes) : entry.Value))];

    private static async Task<IReadOnlyList<KeyValuePair<string, string>>> TryGetHeadersAsync(AsyncUnaryCall<byte[]> call)
    {
        try
        {
            return ToPairs(await call.ResponseHeadersAsync.ConfigureAwait(false));
        }
        catch (RpcException)
        {
            return [];
        }
    }

    private static string DescribeStatus(RpcException exception)
    {
        if (exception.StatusCode == StatusCode.Unavailable && exception.Status.DebugException is { } debug)
        {
            return $"{exception.Status.Detail} ({(debug.InnerException ?? debug).Message})";
        }

        return exception.StatusCode == StatusCode.DeadlineExceeded && string.IsNullOrEmpty(exception.Status.Detail)
            ? "The deadline was exceeded."
            : exception.Status.Detail;
    }
}
