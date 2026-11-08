using JetApi.Core.Model;
using JetApi.Core.Variables;
using JetApi.Grpc;
using JetApi.Grpc.Codec;
using JetApi.Grpc.Schema;
using JetApi.Http;

namespace JetApi.Desktop.Services;

/// <summary>Where the auth of a request comes from, for the "inherited from" hint.</summary>
/// <param name="Auth">The auth that applies.</param>
/// <param name="SourceName">Name of the folder or collection it is inherited from; <c>null</c> when set on the request.</param>
public sealed record AuthSource(AuthConfig Auth, string? SourceName);

/// <summary>The outcome of preparing a gRPC call, including the schema used.</summary>
/// <param name="Preparation">The prepared call or the errors.</param>
/// <param name="Schema">The schema, when the proto files loaded.</param>
public sealed record GrpcPrepareOutcome(GrpcPreparationResult Preparation, ProtoSchema? Schema);

/// <summary>Connects the editors to the engines: builds variables and auth for a request in a session and runs it.</summary>
public sealed class RequestRunner : IDisposable
{
    private readonly HttpRequestExecutor _http;
    private readonly GrpcInvoker _grpc;

    /// <summary>Creates a runner with its own engines.</summary>
    public RequestRunner(ProtoSchemaCache protoSchemas, HttpExecutorOptions? httpOptions = null, GrpcInvokerOptions? grpcOptions = null)
    {
        ProtoSchemas = protoSchemas;
        _http = new HttpRequestExecutor(httpOptions);
        _grpc = new GrpcInvoker(grpcOptions);
    }

    /// <summary>The proto schema cache.</summary>
    public ProtoSchemaCache ProtoSchemas { get; }

    /// <summary>Variables in scope for requests of <paramref name="collectionKey"/> with the active environment.</summary>
    public static VariableResolver CreateResolver(WorkspaceSession session, string? collectionKey)
    {
        ArgumentNullException.ThrowIfNull(session);
        var collection = collectionKey is null ? null : session.Workspace.FindCollection(collectionKey);
        return VariableResolverFactory.Create(session.Workspace, collection, session.ActiveEnvironment);
    }

    /// <summary>The auth that applies to a request at <paramref name="path"/> and where it comes from.</summary>
    public static AuthSource ResolveAuth(WorkspaceSession session, string collectionKey, IReadOnlyList<string> path, AuthConfig requestAuth)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(requestAuth);

        if (requestAuth is not InheritAuth)
        {
            return new AuthSource(requestAuth, null);
        }

        var collection = session.Workspace.FindCollection(collectionKey);
        if (collection is null)
        {
            return new AuthSource(AuthConfig.None, null);
        }

        foreach (var folder in CollectionTree.Ancestors(collection, path))
        {
            if (folder.Auth is not InheritAuth)
            {
                return new AuthSource(folder.Auth, folder.Name);
            }
        }

        return new AuthSource(collection.Auth is InheritAuth ? AuthConfig.None : collection.Auth, collection.Name);
    }

    /// <summary>Resolves variables and auth; does not send anything.</summary>
    public static HttpPreparationResult PrepareHttp(WorkspaceSession session, string collectionKey, IReadOnlyList<string> path, HttpRequestDefinition request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var auth = ResolveAuth(session, collectionKey, path, request.Auth).Auth;
        return HttpRequestPreparer.Prepare(request, auth, CreateResolver(session, collectionKey), session.Root);
    }

    /// <summary>Timeout for requests that do not set their own (from the settings).</summary>
    public TimeSpan DefaultHttpTimeout { get; set; } = TimeSpan.FromSeconds(AppSettings.DefaultRequestTimeoutSeconds);

    /// <summary>Sends a prepared HTTP request.</summary>
    public Task<HttpExecutionResult> ExecuteHttpAsync(PreparedHttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var withTimeout = request.Settings.Timeout is null ? request with { Settings = request.Settings with { Timeout = DefaultHttpTimeout } } : request;
        return _http.ExecuteAsync(withTimeout, cancellationToken);
    }

    /// <summary>Loads the collection's schema, resolves variables and auth and encodes the message.</summary>
    public GrpcPrepareOutcome PrepareGrpc(WorkspaceSession session, string collectionKey, IReadOnlyList<string> path, GrpcRequestDefinition request)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var collection = session.Workspace.FindCollection(collectionKey)
            ?? throw new ArgumentException($"Unknown collection '{collectionKey}'.", nameof(collectionKey));
        var schemaResult = ProtoSchemas.Get(session.Root, collection);
        if (schemaResult.Schema is null)
        {
            var errors = schemaResult.Diagnostics.Where(d => d.IsError).Select(FormatDiagnostic).ToList();
            return new GrpcPrepareOutcome(new GrpcPreparationResult(null, errors, []), null);
        }

        var auth = ResolveAuth(session, collectionKey, path, request.Auth).Auth;
        var preparation = GrpcRequestPreparer.Prepare(request, auth, CreateResolver(session, collectionKey), schemaResult.Schema);
        return new GrpcPrepareOutcome(preparation, schemaResult.Schema);
    }

    /// <summary>Makes a prepared unary call.</summary>
    public Task<GrpcCallResult> InvokeGrpcAsync(PreparedGrpcCall call, ProtoSchema schema, ProtoJsonDecodeOptions? options, CancellationToken cancellationToken) =>
        _grpc.InvokeUnaryAsync(call, schema, options, cancellationToken);

    /// <summary>Formats a proto diagnostic as <c>file:line:column message</c>.</summary>
    public static string FormatDiagnostic(ProtoDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return diagnostic.Line > 0
            ? $"{diagnostic.File}:{diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}"
            : string.IsNullOrEmpty(diagnostic.File) ? diagnostic.Message : $"{diagnostic.File}: {diagnostic.Message}";
    }

    /// <summary>Closes pooled connections.</summary>
    public void Dispose()
    {
        _http.Dispose();
        _grpc.Dispose();
    }
}
