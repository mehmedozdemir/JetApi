using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace JetApi.Http;

/// <summary>
/// Sends prepared requests and measures them. One instance should live for the whole
/// application: it pools connections like a browser does. Thread-safe.
/// </summary>
public sealed class HttpRequestExecutor : IDisposable
{
    private const int ReadBufferSize = 81920;

    private static readonly HttpRequestOptionsKey<TimingRecorder> TimingKey = new("JetApi.Timing");

    private readonly HttpExecutorOptions _options;
    private readonly ConcurrentDictionary<(bool FollowRedirects, bool ValidateCertificates), HttpMessageInvoker> _invokers = new();

    /// <summary>Creates an executor.</summary>
    public HttpRequestExecutor(HttpExecutorOptions? options = null)
    {
        _options = options ?? new HttpExecutorOptions();
    }

    /// <summary>Sends <paramref name="request"/>. Never throws for network or HTTP errors; inspect <see cref="HttpExecutionResult.Outcome"/>.</summary>
    /// <exception cref="ObjectDisposedException">The executor was disposed.</exception>
    public async Task<HttpExecutionResult> ExecuteAsync(PreparedHttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = CreateMessage(request);
        var recorder = new TimingRecorder();
        message.Options.Set(TimingKey, recorder);

        var invoker = _invokers.GetOrAdd((request.Settings.FollowRedirects, request.Settings.ValidateCertificates), CreateInvoker);
        using var timeout = new CancellationTokenSource(request.Settings.Timeout ?? _options.DefaultTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var stopwatch = Stopwatch.StartNew();
        recorder.Start(stopwatch);
        try
        {
            using var response = await invoker.SendAsync(message, linked.Token).ConfigureAwait(false);
            var timeToFirstByte = stopwatch.Elapsed;
            var (body, isTruncated) = await ReadBodyAsync(response.Content, linked.Token).ConfigureAwait(false);
            var total = stopwatch.Elapsed;

            return new HttpExecutionResult
            {
                Outcome = HttpExecutionOutcome.Completed,
                Sent = DescribeSent(message),
                StatusCode = response.StatusCode,
                ReasonPhrase = response.ReasonPhrase,
                HttpVersion = response.Version,
                FinalUrl = response.RequestMessage?.RequestUri,
                Headers = CollectHeaders(response),
                Body = body,
                IsBodyTruncated = isTruncated,
                ContentType = response.Content.Headers.ContentType?.ToString(),
                Timings = new HttpTimings(recorder.Dns, recorder.Connect, timeToFirstByte, total - timeToFirstByte, total),
                RemoteEndPoint = recorder.RemoteEndPoint,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(HttpExecutionOutcome.Cancelled, HttpErrorKind.Cancelled, message, recorder, stopwatch, Messages.Get("Http_Cancelled"));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            var limit = request.Settings.Timeout ?? _options.DefaultTimeout;
            return Failure(HttpExecutionOutcome.TimedOut, HttpErrorKind.TimedOut, message, recorder, stopwatch, Messages.Format("Http_TimedOut", limit.TotalMilliseconds.ToString("0", CultureInfo.CurrentCulture)));
        }
        catch (HttpRequestException exception)
        {
            var (kind, description, detail) = Describe(exception);
            return Failure(HttpExecutionOutcome.Failed, kind, message, recorder, stopwatch, description, detail);
        }
        catch (IOException exception)
        {
            return Failure(HttpExecutionOutcome.Failed, HttpErrorKind.Network, message, recorder, stopwatch, Messages.Format("Http_Network", exception.Message), exception.Message);
        }
    }

    /// <summary>Closes all pooled connections.</summary>
    public void Dispose()
    {
        foreach (var invoker in _invokers.Values)
        {
            invoker.Dispose();
        }

        _invokers.Clear();
    }

    private HttpMessageInvoker CreateInvoker((bool FollowRedirects, bool ValidateCertificates) key)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = key.FollowRedirects,
            MaxAutomaticRedirections = _options.MaxRedirects,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            UseProxy = true,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectCallback = ConnectAsync,
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

        return new HttpMessageInvoker(handler, disposeHandler: true);
    }

    // Replaces the default connect logic only to measure DNS and TCP connect separately.
    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        context.InitialRequestMessage.Options.TryGetValue(TimingKey, out var recorder);

        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        recorder?.MarkDnsResolved();

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            recorder?.MarkConnected(socket.RemoteEndPoint?.ToString());
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private HttpRequestMessage CreateMessage(PreparedHttpRequest request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url)
        {
            Content = CreateContent(request.Body),
        };

        foreach (var (name, value) in request.Headers)
        {
            AddHeader(message, name, value);
        }

        if (!message.Headers.Contains("User-Agent"))
        {
            message.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }

        if (!message.Headers.Contains("Accept"))
        {
            message.Headers.TryAddWithoutValidation("Accept", "*/*");
        }

        return message;
    }

    private static void AddHeader(HttpRequestMessage message, string name, string value)
    {
        if (message.Headers.TryAddWithoutValidation(name, value))
        {
            return;
        }

        // Content-* headers belong to the content. A user-specified value replaces the default one.
        if (message.Content is not null)
        {
            message.Content.Headers.Remove(name);
            message.Content.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static HttpContent? CreateContent(PreparedBody body)
    {
        switch (body)
        {
            case BytesBody bytes:
                var content = new ReadOnlyMemoryContent(bytes.Content);
                content.Headers.TryAddWithoutValidation("Content-Type", bytes.ContentType);
                return content;
            case FormBody form:
                return new FormUrlEncodedContent(form.Fields);
            case FileBody file:
                var stream = new StreamContent(File.OpenRead(file.FilePath));
                stream.Headers.TryAddWithoutValidation("Content-Type", file.ContentType);
                return stream;
            case MultipartBody multipart:
                return CreateMultipart(multipart);
            default:
                return null;
        }
    }

    private static MultipartFormDataContent CreateMultipart(MultipartBody multipart)
    {
        var content = new MultipartFormDataContent();
        foreach (var part in multipart.Parts)
        {
            if (part.IsFile)
            {
                var file = new StreamContent(File.OpenRead(part.Value));
                file.Headers.ContentType = MediaTypeHeaderValue.Parse(part.ContentType ?? "application/octet-stream");
                content.Add(file, part.Name, Path.GetFileName(part.Value));
            }
            else
            {
                var text = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(part.Value));
                if (part.ContentType is not null)
                {
                    text.Headers.TryAddWithoutValidation("Content-Type", part.ContentType);
                }

                content.Add(text, part.Name);
            }
        }

        return content;
    }

    private async Task<(ReadOnlyMemory<byte> Body, bool IsTruncated)> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[ReadBufferSize];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                var remaining = _options.MaxResponseBodyBytes - buffer.Length;
                if (read > remaining)
                {
                    buffer.Write(chunk, 0, (int)remaining);
                    return (buffer.ToArray(), true);
                }

                buffer.Write(chunk, 0, read);
            }

            return (buffer.ToArray(), false);
        }
    }

    private static List<KeyValuePair<string, string>> CollectHeaders(HttpResponseMessage response)
    {
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> all = response.Headers
            .Concat(response.Content.Headers)
            .Concat(response.TrailingHeaders);
        return [.. all.SelectMany(header => header.Value.Select(value => new KeyValuePair<string, string>(header.Key, value)))];
    }

    private static SentHttpRequest DescribeSent(HttpRequestMessage message)
    {
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers = message.Headers.NonValidated
            .Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value));
        if (message.Content is not null)
        {
            headers = headers.Concat(message.Content.Headers.NonValidated.Select(h => new KeyValuePair<string, IEnumerable<string>>(h.Key, h.Value)));
        }

        return new SentHttpRequest(
            message.Method.Method,
            message.RequestUri!,
            [.. headers.Select(h => new KeyValuePair<string, string>(h.Key, string.Join(", ", h.Value)))],
            message.Content?.Headers.ContentLength);
    }

    private static HttpExecutionResult Failure(
        HttpExecutionOutcome outcome,
        HttpErrorKind kind,
        HttpRequestMessage message,
        TimingRecorder recorder,
        Stopwatch stopwatch,
        string error,
        string? detail = null) => new()
        {
            Outcome = outcome,
            Sent = DescribeSent(message),
            Timings = new HttpTimings(recorder.Dns, recorder.Connect, TimeSpan.Zero, TimeSpan.Zero, stopwatch.Elapsed),
            RemoteEndPoint = recorder.RemoteEndPoint,
            ErrorMessage = error,
            ErrorDetail = detail,
            ErrorKind = kind,
        };

    private static (HttpErrorKind Kind, string Message, string? Detail) Describe(HttpRequestException exception)
    {
        var root = exception.InnerException;
        return root switch
        {
            SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData } =>
                (HttpErrorKind.DnsFailure, Messages.Get("Http_Dns"), root.Message),
            SocketException { SocketErrorCode: SocketError.ConnectionRefused } =>
                (HttpErrorKind.ConnectionRefused, Messages.Get("Http_Refused"), root.Message),
            SocketException socket => (HttpErrorKind.Network, Messages.Format("Http_Network", socket.Message), socket.Message),
            AuthenticationException auth =>
                (HttpErrorKind.TlsFailure, Messages.Format("Http_Tls", (auth.InnerException ?? auth).Message), (auth.InnerException ?? auth).Message),
            _ => (HttpErrorKind.Network, Messages.Format("Http_Network", exception.Message), exception.Message),
        };
    }

    private sealed class TimingRecorder
    {
        private Stopwatch? _stopwatch;
        private TimeSpan? _dnsResolvedAt;

        public TimeSpan? Dns { get; private set; }

        public TimeSpan? Connect { get; private set; }

        public string? RemoteEndPoint { get; private set; }

        public void Start(Stopwatch stopwatch) => _stopwatch = stopwatch;

        public void MarkDnsResolved()
        {
            // Only the first connection of a request is measured (redirects may open more).
            if (_stopwatch is null || Dns is not null)
            {
                return;
            }

            _dnsResolvedAt = _stopwatch.Elapsed;
            Dns = _dnsResolvedAt;
        }

        public void MarkConnected(string? remoteEndPoint)
        {
            if (_stopwatch is null || Connect is not null || _dnsResolvedAt is null)
            {
                return;
            }

            Connect = _stopwatch.Elapsed - _dnsResolvedAt.Value;
            RemoteEndPoint = remoteEndPoint;
        }
    }
}
