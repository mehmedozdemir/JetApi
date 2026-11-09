using JetApi.Core.Model;
using JetApi.Grpc;
using JetApi.Http;
using JetApi.Runner;
using JetApi.Testing;

namespace JetApi.Desktop.Services;

/// <summary>Sends the requests of a collection run in the app: the same preparation, auth, OAuth tokens and cookies as a tab.</summary>
/// <param name="session">The workspace.</param>
/// <param name="runner">Prepares and sends requests.</param>
public sealed class RunSender(WorkspaceSession session, RequestRunner runner) : IRequestSender
{
    /// <inheritdoc />
    public async Task<SendResult> SendAsync(RunRequest request, IReadOnlyDictionary<string, string> runVariables, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        switch (request.Request)
        {
            case HttpRequestDefinition http:
                {
                    if (await EnsureTokenAsync(request, http.Auth, http.Headers, cancellationToken).ConfigureAwait(true) is { } tokenError)
                    {
                        return Failed(tokenError);
                    }

                    var preparation = RequestRunner.PrepareHttp(session, request.CollectionKey, request.Path, http, runVariables: runVariables);
                    if (!preparation.IsSuccess)
                    {
                        return Failed(string.Join(Environment.NewLine, preparation.Errors));
                    }

                    var result = await runner.ExecuteHttpAsync(preparation.Request!, session.Cookies, cancellationToken).ConfigureAwait(true);
                    cancellationToken.ThrowIfCancellationRequested();
                    return result.Outcome == HttpExecutionOutcome.Completed && result.StatusCode is { } status
                        ? new SendResult(
                            new ResponseSnapshot((int)status, result.Headers, ViewModels.Formatting.DecodeText(result.Body.Span, result.ContentType), result.Timings?.Total),
                            $"{(int)status} {result.ReasonPhrase}".TrimEnd(),
                            null)
                        : Failed(result.ErrorMessage ?? Localization.Loc.Get("Err_Cancelled"));
                }

            case GrpcRequestDefinition grpc:
                {
                    if (await EnsureTokenAsync(request, grpc.Auth, grpc.Metadata, cancellationToken).ConfigureAwait(true) is { } tokenError)
                    {
                        return Failed(tokenError);
                    }

                    var outcome = runner.PrepareGrpc(session, request.CollectionKey, request.Path, grpc, runVariables);
                    if (!outcome.Preparation.IsSuccess)
                    {
                        return Failed(string.Join(Environment.NewLine, outcome.Preparation.Errors));
                    }

                    var result = await runner.InvokeGrpcAsync(outcome.Preparation.Call!, outcome.Schema!, null, cancellationToken).ConfigureAwait(true);
                    cancellationToken.ThrowIfCancellationRequested();
                    return result.Outcome == GrpcCallOutcome.Completed
                        ? new SendResult(new ResponseSnapshot(result.StatusCode, [.. result.ResponseHeaders, .. result.Trailers], result.ResponseJson, result.Duration), $"{result.StatusName} ({result.StatusCode})", null)
                        : Failed(Localization.Loc.Get("Err_Cancelled"));
                }

            default:
                throw new NotSupportedException($"Unsupported request type {request.Request.GetType().Name}.");
        }
    }

    /// <inheritdoc />
    public string Resolve(RunRequest request, string text, IReadOnlyDictionary<string, string> runVariables)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RequestRunner.CreateResolver(session, request.CollectionKey, runVariables).Resolve(text).Value;
    }

    /// <inheritdoc />
    public Task<string?> StoreAsync(RunRequest request, Extraction extraction, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return VariableWriter.StoreExtractionAsync(session, request.CollectionKey, extraction, value);
    }

    // OAuth 2.0 requests get or renew their token first, as when a tab sends them.
    private async Task<string?> EnsureTokenAsync(RunRequest request, AuthConfig auth, IReadOnlyList<KeyValueEntry> headers, CancellationToken cancellationToken) =>
        !RequestRunner.SetsAuthorization(headers) && RequestRunner.ResolveOAuth(session, request.CollectionKey, request.Path, auth) is { } oauth
            ? await runner.EnsureOAuthTokenAsync(session, oauth, cancellationToken).ConfigureAwait(true)
            : null;

    private static SendResult Failed(string error) => new(ResponseSnapshot.None, string.Empty, error);
}
