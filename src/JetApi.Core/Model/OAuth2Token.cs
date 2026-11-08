namespace JetApi.Core.Model;

/// <summary>An OAuth 2.0 access token as returned by a token endpoint (RFC 6749 §5.1).</summary>
/// <param name="AccessToken">The access token.</param>
/// <param name="TokenType">Token type, usually <c>Bearer</c>.</param>
/// <param name="ExpiresAt">When the token expires; <c>null</c> when the server did not say.</param>
/// <param name="RefreshToken">Refresh token, if the server issued one.</param>
/// <param name="Scope">Granted scope, if known.</param>
public sealed record OAuth2Token(
    string AccessToken,
    string TokenType = "Bearer",
    DateTimeOffset? ExpiresAt = null,
    string? RefreshToken = null,
    string? Scope = null)
{
    /// <summary>The default safety margin of <see cref="IsExpired"/>: a token about to expire is treated as expired.</summary>
    public static readonly TimeSpan DefaultSkew = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The value of the <c>Authorization</c> header. <c>bearer</c> (any case) and an empty type become <c>Bearer</c>,
    /// because some servers only accept that spelling; other types are kept as returned.
    /// </summary>
    public string AuthorizationHeaderValue =>
        (string.IsNullOrWhiteSpace(TokenType) || TokenType.Equals("bearer", StringComparison.OrdinalIgnoreCase) ? "Bearer" : TokenType.Trim())
        + " " + AccessToken;

    /// <summary>True when the token has expired or expires within <paramref name="skew"/>.</summary>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="skew">Safety margin; <see cref="DefaultSkew"/> when <c>null</c>.</param>
    public bool IsExpired(TimeProvider timeProvider, TimeSpan? skew = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return ExpiresAt is { } expiresAt && timeProvider.GetUtcNow() + (skew ?? DefaultSkew) >= expiresAt;
    }
}
