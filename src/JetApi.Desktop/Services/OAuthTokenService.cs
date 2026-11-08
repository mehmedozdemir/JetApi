using JetApi.Core.Model;

namespace JetApi.Desktop.Services;

/// <summary>The token of the OAuth 2.0 settings shown in an auth editor: its state, getting a new one, forgetting it.</summary>
public interface IOAuthTokenService
{
    /// <summary>The cached token for <paramref name="auth"/> (as written, variables not resolved), expired or not.</summary>
    OAuth2Token? Current(OAuth2Auth auth);

    /// <summary>Gets a new token, signing in through the browser for the authorization code grant.</summary>
    /// <returns>An error message, or <c>null</c> on success.</returns>
    Task<string?> GetNewTokenAsync(OAuth2Auth auth, CancellationToken cancellationToken);

    /// <summary>Forgets the cached token of <paramref name="auth"/>.</summary>
    void Forget(OAuth2Auth auth);
}

/// <summary>Tokens of the open workspace, with variables resolved for one collection and the active environment.</summary>
/// <param name="session">The workspace.</param>
/// <param name="runner">Talks to the token endpoint.</param>
/// <param name="collectionKey">The collection whose variables apply (it can change when a request is moved).</param>
/// <param name="openBrowser">Opens the sign-in page.</param>
public sealed class OAuthTokenService(WorkspaceSession session, RequestRunner runner, Func<string?> collectionKey, Func<Uri, Task> openBrowser) : IOAuthTokenService
{
    /// <inheritdoc />
    public OAuth2Token? Current(OAuth2Auth auth) => session.Tokens.Get(Resolve(auth));

    /// <inheritdoc />
    public async Task<string?> GetNewTokenAsync(OAuth2Auth auth, CancellationToken cancellationToken)
    {
        var result = await runner.GetNewOAuthTokenAsync(session, Resolve(auth), openBrowser, cancellationToken).ConfigureAwait(true);
        return result.IsSuccess ? null : result.ErrorMessage ?? result.Error ?? string.Empty;
    }

    /// <inheritdoc />
    public void Forget(OAuth2Auth auth) => session.Tokens.Remove(Resolve(auth));

    private OAuth2Auth Resolve(OAuth2Auth auth) => RequestRunner.ResolveOAuth(session, collectionKey(), auth);
}
