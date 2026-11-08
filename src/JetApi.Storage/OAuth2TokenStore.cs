using System.Text.Json;
using System.Text.Json.Serialization;
using JetApi.Core.Model;

namespace JetApi.Storage;

/// <summary>
/// Per-machine OAuth 2.0 token cache of a workspace in <c>.jetapi/tokens.json</c> (git-ignored, see ADR-0007).
/// Tokens are keyed by <see cref="OAuth2Auth.GetTokenCacheKey"/> of the <b>resolved</b> auth, so environments with
/// different token URLs, clients, scopes or users never share a token. Thread-safe.
/// </summary>
/// <remarks>
/// The cache is a convenience: a damaged file is ignored and a failed write is not reported, the user just gets a new
/// token. Expired tokens without a refresh token are dropped when the file is written.
/// </remarks>
public sealed class OAuth2TokenStore
{
    /// <summary>File name inside <see cref="WorkspaceLayout.LocalStateDirectory"/>.</summary>
    public const string FileName = "tokens.json";

    private const int FileVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly Lock _lock = new();
    private readonly string _directory;
    private readonly string _filePath;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, OAuth2Token> _tokens;

    /// <summary>Opens the token cache of the workspace at <paramref name="workspaceRoot"/>.</summary>
    /// <param name="workspaceRoot">Workspace directory.</param>
    /// <param name="timeProvider">Clock used to drop expired tokens; the system clock when <c>null</c>.</param>
    public OAuth2TokenStore(string workspaceRoot, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        _directory = Path.Combine(workspaceRoot, WorkspaceLayout.LocalStateDirectory);
        _filePath = Path.Combine(_directory, FileName);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _tokens = Read(_filePath);
    }

    /// <summary>The cached token for <paramref name="resolvedAuth"/>, expired or not; <c>null</c> when there is none.</summary>
    /// <param name="resolvedAuth">The auth with variables already resolved.</param>
    public OAuth2Token? Get(OAuth2Auth resolvedAuth)
    {
        ArgumentNullException.ThrowIfNull(resolvedAuth);
        lock (_lock)
        {
            return _tokens.GetValueOrDefault(resolvedAuth.GetTokenCacheKey());
        }
    }

    /// <summary>Stores <paramref name="token"/> for <paramref name="resolvedAuth"/> and writes the file.</summary>
    /// <param name="resolvedAuth">The auth with variables already resolved.</param>
    /// <param name="token">The token.</param>
    public void Set(OAuth2Auth resolvedAuth, OAuth2Token token)
    {
        ArgumentNullException.ThrowIfNull(resolvedAuth);
        ArgumentNullException.ThrowIfNull(token);
        lock (_lock)
        {
            _tokens[resolvedAuth.GetTokenCacheKey()] = token;
            Write();
        }
    }

    /// <summary>Forgets the token of <paramref name="resolvedAuth"/>.</summary>
    /// <param name="resolvedAuth">The auth with variables already resolved.</param>
    /// <returns>True when a token was removed.</returns>
    public bool Remove(OAuth2Auth resolvedAuth)
    {
        ArgumentNullException.ThrowIfNull(resolvedAuth);
        lock (_lock)
        {
            if (!_tokens.Remove(resolvedAuth.GetTokenCacheKey()))
            {
                return false;
            }

            Write();
            return true;
        }
    }

    /// <summary>Forgets every token of the workspace and deletes the file.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _tokens.Clear();
            try
            {
                File.Delete(_filePath);
            }
            catch (IOException)
            {
                // The file is rewritten without tokens on the next change.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above.
            }
        }
    }

    private void Write()
    {
        foreach (var key in _tokens.Where(t => t.Value.RefreshToken is null && t.Value.IsExpired(_timeProvider, TimeSpan.Zero)).Select(t => t.Key).ToList())
        {
            _tokens.Remove(key);
        }

        try
        {
            Directory.CreateDirectory(_directory);
            EnsureIgnoredByGit();
            var temporary = _filePath + ".tmp";
            var tokens = new SortedDictionary<string, TokenDto>(StringComparer.Ordinal);
            foreach (var (key, token) in _tokens)
            {
                tokens[key] = new TokenDto(token.AccessToken, token.TokenType, token.ExpiresAt, token.RefreshToken, token.Scope);
            }

            var json = JsonSerializer.Serialize(new TokenFile(FileVersion, tokens), JsonOptions);
            File.WriteAllText(temporary, json.ReplaceLineEndings("\n"));
            if (!OperatingSystem.IsWindows())
            {
                // Tokens are credentials: readable by the current user only.
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporary, _filePath, overwrite: true);
        }
        catch (IOException)
        {
            // A token cache that cannot be written only means the user gets a new token next time.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }

    // Workspaces not created by JetApi may lack the .gitignore entry; tokens must never be committed.
    private void EnsureIgnoredByGit()
    {
        var gitIgnore = Path.Combine(_directory, ".gitignore");
        if (!File.Exists(gitIgnore))
        {
            File.WriteAllText(gitIgnore, "# JetApi: per-machine state, never commit\n*\n");
        }
    }

    private static Dictionary<string, OAuth2Token> Read(string path)
    {
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize<TokenFile>(File.ReadAllText(path), JsonOptions) is { Version: FileVersion, Tokens: { } tokens })
            {
                return tokens
                    .Where(t => !string.IsNullOrEmpty(t.Value?.AccessToken))
                    .ToDictionary(
                        t => t.Key,
                        t => new OAuth2Token(t.Value.AccessToken!, t.Value.TokenType ?? "Bearer", t.Value.ExpiresAt, t.Value.RefreshToken, t.Value.Scope),
                        StringComparer.Ordinal);
            }
        }
        catch (JsonException)
        {
            // A damaged cache is ignored; it is replaced on the next write.
        }
        catch (IOException)
        {
            // Same as above.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }

        return new Dictionary<string, OAuth2Token>(StringComparer.Ordinal);
    }

    private sealed record TokenFile(int Version, IDictionary<string, TokenDto>? Tokens);

    private sealed record TokenDto(string? AccessToken, string? TokenType, DateTimeOffset? ExpiresAt, string? RefreshToken, string? Scope);
}
