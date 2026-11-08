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
    private readonly OAuth2Client _oauth;

    /// <summary>Creates a runner with its own engines.</summary>
    /// <param name="protoSchemas">The proto schema cache.</param>
    /// <param name="httpOptions">HTTP engine options.</param>
    /// <param name="grpcOptions">gRPC engine options.</param>
    /// <param name="oauthHandler">Handler for token requests (tests); a real connection when <c>null</c>.</param>
    public RequestRunner(ProtoSchemaCache protoSchemas, HttpExecutorOptions? httpOptions = null, GrpcInvokerOptions? grpcOptions = null, HttpMessageHandler? oauthHandler = null)
    {
        ProtoSchemas = protoSchemas;
        _http = new HttpRequestExecutor(httpOptions);
        _grpc = new GrpcInvoker(grpcOptions);
        _oauth = new OAuth2Client(oauthHandler);
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
    /// <param name="session">The workspace.</param>
    /// <param name="collectionKey">The request's collection.</param>
    /// <param name="path">The request's path.</param>
    /// <param name="request">The request as edited.</param>
    /// <param name="tokenPlaceholder">
    /// For code snippets: an OAuth 2.0 request without a usable token gets this text as its token instead of an error.
    /// </param>
    public static HttpPreparationResult PrepareHttp(WorkspaceSession session, string collectionKey, IReadOnlyList<string> path, HttpRequestDefinition request, string? tokenPlaceholder = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var auth = ResolveAuth(session, collectionKey, path, request.Auth).Auth;
        var tokens = UsableToken(session);
        return HttpRequestPreparer.Prepare(
            request,
            auth,
            CreateResolver(session, collectionKey),
            session.Root,
            tokenPlaceholder is null ? tokens : oauth => tokens(oauth) ?? new OAuth2Token(tokenPlaceholder));
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
        var preparation = GrpcRequestPreparer.Prepare(request, auth, CreateResolver(session, collectionKey), schemaResult.Schema, UsableToken(session));
        return new GrpcPrepareOutcome(preparation, schemaResult.Schema);
    }

    /// <summary>
    /// The OAuth 2.0 settings that apply to a request, with variables resolved; <c>null</c> when it does not use OAuth 2.0.
    /// </summary>
    public static OAuth2Auth? ResolveOAuth(WorkspaceSession session, string collectionKey, IReadOnlyList<string> path, AuthConfig requestAuth) =>
        ResolveAuth(session, collectionKey, path, requestAuth).Auth is OAuth2Auth oauth ? ResolveOAuth(session, collectionKey, oauth) : null;

    /// <summary>Resolves the variables of <paramref name="auth"/> for requests of <paramref name="collectionKey"/>.</summary>
    public static OAuth2Auth ResolveOAuth(WorkspaceSession session, string? collectionKey, OAuth2Auth auth)
    {
        ArgumentNullException.ThrowIfNull(auth);
        var resolver = CreateResolver(session, collectionKey);
        return auth.Resolve(template => resolver.Resolve(template).Value);
    }

    /// <summary>
    /// Makes sure a usable token is cached before a request is prepared: an expired token is refreshed, and the client
    /// credentials and password grants get a new one without asking. The authorization code grant needs the browser,
    /// so it is left to "Get new access token".
    /// </summary>
    /// <returns>An error message for the response pane, or <c>null</c>.</returns>
    public async Task<string?> EnsureOAuthTokenAsync(WorkspaceSession session, OAuth2Auth resolvedAuth, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolvedAuth);

        var token = session.Tokens.Get(resolvedAuth);
        if (token is not null && !token.IsExpired(TimeProvider.System))
        {
            return null;
        }

        OAuth2TokenResult? result = null;
        if (token?.RefreshToken is { Length: > 0 } refreshToken)
        {
            result = await _oauth.RefreshAsync(resolvedAuth, refreshToken, cancellationToken).ConfigureAwait(true);
            if (result.Token is { } refreshed)
            {
                // Servers may keep the refresh token and not send it again.
                Store(session, resolvedAuth, refreshed.RefreshToken is null ? refreshed with { RefreshToken = refreshToken } : refreshed);
                return null;
            }
        }

        if (resolvedAuth.GrantType != OAuth2GrantType.AuthorizationCode)
        {
            result = await _oauth.RequestTokenAsync(resolvedAuth, cancellationToken).ConfigureAwait(true);
        }

        if (result is null)
        {
            return null;
        }

        if (result.Token is { } newToken)
        {
            Store(session, resolvedAuth, newToken);
            return null;
        }

        return result.ErrorMessage ?? result.Error;
    }

    /// <summary>Gets a new token with the grant of <paramref name="resolvedAuth"/> and caches it.</summary>
    /// <param name="session">The workspace.</param>
    /// <param name="resolvedAuth">Settings with variables resolved.</param>
    /// <param name="openBrowser">Opens the sign-in page (authorization code grant).</param>
    /// <param name="cancellationToken">Cancels the request or the wait for the browser.</param>
    public async Task<OAuth2TokenResult> GetNewOAuthTokenAsync(WorkspaceSession session, OAuth2Auth resolvedAuth, Func<Uri, Task> openBrowser, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolvedAuth);
        var result = await _oauth.GetTokenAsync(resolvedAuth, openBrowser, cancellationToken: cancellationToken).ConfigureAwait(true);
        if (result.Token is { } token)
        {
            Store(session, resolvedAuth, token);
        }

        return result;
    }

    /// <summary>Whether the enabled entries set the <c>Authorization</c> header themselves, which wins over auth.</summary>
    public static bool SetsAuthorization(IEnumerable<KeyValueEntry> entries) =>
        entries.Any(e => e.IsEnabled && e.Key.Trim().Equals("Authorization", StringComparison.OrdinalIgnoreCase));

    private static void Store(WorkspaceSession session, OAuth2Auth resolvedAuth, OAuth2Token token) => session.Tokens.Set(resolvedAuth, token);

    private static Func<OAuth2Auth, OAuth2Token?> UsableToken(WorkspaceSession session) =>
        auth => session.Tokens.Get(auth) is { } token && !token.IsExpired(TimeProvider.System) ? token : null;

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
        _oauth.Dispose();
        _grpc.Dispose();
    }
}
