using System.Net;
using JetApi.Core.Model;
using JetApi.Core.Variables;
using JetApi.Grpc;
using JetApi.Grpc.Schema;
using JetApi.Http;
using JetApi.Runner;
using JetApi.Storage;
using JetApi.Testing;

namespace JetApi.Cli;

/// <summary>
/// Sends the requests of a CLI run with the same engines as the app: variables (workspace, <c>--var</c>, run values),
/// inherited auth, OAuth 2.0 tokens (the workspace's token cache, refreshed or requested as needed), cookies kept for
/// the run, and gRPC schemas loaded once per collection.
/// </summary>
internal sealed class CliSender : IRequestSender, IDisposable
{
    private readonly string _root;
    private readonly IReadOnlyList<KeyValuePair<string, string>> _variables;
    private readonly TimeSpan _timeout;
    private readonly HttpRequestExecutor _http;
    private readonly GrpcInvoker _grpc;
    private readonly OAuth2Client _oauth;
    private readonly OAuth2TokenStore _tokens;
    private readonly CookieContainer _cookies = new();
    private readonly Dictionary<string, ProtoLoadResult> _schemas = new(StringComparer.Ordinal);

    /// <summary>Creates the sender.</summary>
    /// <param name="root">The workspace folder.</param>
    /// <param name="workspace">The loaded workspace.</param>
    /// <param name="environment">The chosen environment, if any.</param>
    /// <param name="variables">Values from <c>--var</c>.</param>
    /// <param name="timeout">Timeout of requests that do not set their own.</param>
    public CliSender(string root, Workspace workspace, ApiEnvironment? environment, IReadOnlyList<KeyValuePair<string, string>> variables, TimeSpan timeout)
    {
        _root = root;
        Workspace = workspace;
        Environment = environment;
        _variables = variables;
        _timeout = timeout;
        _http = new HttpRequestExecutor(new HttpExecutorOptions { DefaultTimeout = timeout });
        _grpc = new GrpcInvoker(new GrpcInvokerOptions { DefaultDeadline = timeout });
        _oauth = new OAuth2Client(timeout: timeout);
        _tokens = new OAuth2TokenStore(root);
    }

    /// <summary>The workspace, with values stored by <see cref="StoreAsync"/>.</summary>
    public Workspace Workspace { get; private set; }

    /// <summary>The environment, with values stored by <see cref="StoreAsync"/>.</summary>
    public ApiEnvironment? Environment { get; private set; }

    /// <inheritdoc />
    public async Task<SendResult> SendAsync(RunRequest request, IReadOnlyDictionary<string, string> runVariables, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var collection = Workspace.FindCollection(request.CollectionKey) ?? throw new ArgumentException("Unknown collection.", nameof(request));
        var resolver = Resolver(collection, runVariables);
        var auth = AuthResolver.Resolve(request.Request, CollectionTree.Ancestors(collection, request.Path), collection);
        var headers = request.Request switch
        {
            HttpRequestDefinition http => http.Headers,
            GrpcRequestDefinition grpc => grpc.Metadata,
            _ => [],
        };
        if (auth is OAuth2Auth oauth && !headers.Any(h => h.IsEnabled && h.Key.Trim().Equals("Authorization", StringComparison.OrdinalIgnoreCase)))
        {
            var resolved = oauth.Resolve(template => resolver.Resolve(template).Value);
            if (await _oauth.EnsureTokenAsync(resolved, _tokens.Get(resolved), token => _tokens.Set(resolved, token), cancellationToken).ConfigureAwait(false) is { } tokenError)
            {
                return Failed(tokenError);
            }
        }

        return request.Request switch
        {
            HttpRequestDefinition http => await SendHttpAsync(http, auth, resolver, cancellationToken).ConfigureAwait(false),
            GrpcRequestDefinition grpc => await SendGrpcAsync(collection, grpc, auth, resolver, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Unsupported request type {request.Request.GetType().Name}."),
        };
    }

    /// <inheritdoc />
    public string Resolve(RunRequest request, string text, IReadOnlyDictionary<string, string> runVariables)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Resolver(Workspace.FindCollection(request.CollectionKey), runVariables).Resolve(text).Value;
    }

    /// <inheritdoc />
    public async Task<string?> StoreAsync(RunRequest request, Extraction extraction, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(extraction);
        try
        {
            switch (extraction.To)
            {
                case ExtractionTarget.Environment:
                    if (Environment is null)
                    {
                        return Messages.Format("Cli_NoEnvironmentToStore", extraction.Variable);
                    }

                    Environment = Environment with { Variables = VariableList.Set(Environment.Variables, extraction.Variable, value) };
                    Workspace = Workspace with { Environments = [.. Workspace.Environments.Select(e => e.Key == Environment.Key ? Environment : e)] };
                    await WorkspaceStore.SaveEnvironmentAsync(_root, Environment, cancellationToken).ConfigureAwait(false);
                    break;
                case ExtractionTarget.Collection:
                    var collection = Workspace.FindCollection(request.CollectionKey)!;
                    collection = collection with { Variables = VariableList.Set(collection.Variables, extraction.Variable, value) };
                    Workspace = Workspace.WithCollection(collection);
                    await WorkspaceStore.SaveCollectionAsync(_root, collection, cancellationToken).ConfigureAwait(false);
                    break;
                case ExtractionTarget.Global:
                    Workspace = Workspace with { GlobalVariables = VariableList.Set(Workspace.GlobalVariables, extraction.Variable, value) };
                    await WorkspaceStore.SaveAsync(_root, Workspace, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    break;
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or WorkspaceStorageException)
        {
            return Messages.Format("Cli_StoreFailed", extraction.Variable, exception.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
        _grpc.Dispose();
        _oauth.Dispose();
    }

    private VariableResolver Resolver(ApiCollection? collection, IReadOnlyDictionary<string, string> runVariables)
    {
        var runtime = _variables.Concat(runVariables).Select(v => new Variable(v.Key, v.Value)).ToList();
        return VariableResolverFactory.Create(Workspace, collection, Environment, runtime.Count == 0 ? null : runtime);
    }

    private async Task<SendResult> SendHttpAsync(HttpRequestDefinition request, AuthConfig auth, VariableResolver resolver, CancellationToken cancellationToken)
    {
        var preparation = HttpRequestPreparer.Prepare(request, auth, resolver, _root, UsableToken);
        if (!preparation.IsSuccess)
        {
            return Failed(string.Join(System.Environment.NewLine, preparation.Errors));
        }

        var prepared = preparation.Request!;
        if (prepared.Settings.Timeout is null)
        {
            prepared = prepared with { Settings = prepared.Settings with { Timeout = _timeout } };
        }

        var result = await _http.ExecuteAsync(HttpCookies.Apply(prepared, _cookies), cancellationToken).ConfigureAwait(false);
        HttpCookies.Keep(result, prepared.Url, _cookies);
        return result.Outcome == HttpExecutionOutcome.Completed && result.StatusCode is { } status
            ? new SendResult(
                new ResponseSnapshot((int)status, result.Headers, ResponseText.Decode(result.Body.Span, result.ContentType), result.Timings?.Total),
                $"{(int)status} {result.ReasonPhrase}".TrimEnd(),
                null)
            : Failed(result.ErrorMessage ?? Messages.Get("Cli_Cancelled"));
    }

    private async Task<SendResult> SendGrpcAsync(ApiCollection collection, GrpcRequestDefinition request, AuthConfig auth, VariableResolver resolver, CancellationToken cancellationToken)
    {
        if (!_schemas.TryGetValue(collection.Key, out var schema))
        {
            schema = ProtoSchemaLoader.Load(collection.Proto, _root);
            _schemas[collection.Key] = schema;
        }

        if (schema.Schema is null)
        {
            return Failed(string.Join(System.Environment.NewLine, schema.Diagnostics.Where(d => d.IsError).Select(Diagnostic)));
        }

        var preparation = GrpcRequestPreparer.Prepare(request, auth, resolver, schema.Schema, UsableToken);
        if (!preparation.IsSuccess)
        {
            return Failed(string.Join(System.Environment.NewLine, preparation.Errors));
        }

        var result = await _grpc.InvokeUnaryAsync(preparation.Call!, schema.Schema, null, cancellationToken).ConfigureAwait(false);
        return result.Outcome == GrpcCallOutcome.Completed
            ? new SendResult(new ResponseSnapshot(result.StatusCode, [.. result.ResponseHeaders, .. result.Trailers], result.ResponseJson, result.Duration), $"{result.StatusName} ({result.StatusCode})", null)
            : Failed(Messages.Get("Cli_Cancelled"));
    }

    private OAuth2Token? UsableToken(OAuth2Auth auth) => _tokens.Get(auth) is { } token && !token.IsExpired(TimeProvider.System) ? token : null;

    private static string Diagnostic(ProtoDiagnostic diagnostic) =>
        diagnostic.Line > 0 ? $"{diagnostic.File}:{diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}"
        : string.IsNullOrEmpty(diagnostic.File) ? diagnostic.Message : $"{diagnostic.File}: {diagnostic.Message}";

    private static SendResult Failed(string error) => new(ResponseSnapshot.None, string.Empty, error);
}
