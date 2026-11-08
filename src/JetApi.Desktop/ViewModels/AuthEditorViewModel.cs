using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Core.Model;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;

namespace JetApi.Desktop.ViewModels;

/// <summary>Kinds of auth offered in the editor.</summary>
public enum AuthKind
{
    /// <summary>Inherit from the parent.</summary>
    Inherit,

    /// <summary>No auth.</summary>
    None,

    /// <summary>Bearer token.</summary>
    Bearer,

    /// <summary>Basic.</summary>
    Basic,

    /// <summary>API key.</summary>
    ApiKey,

    /// <summary>OAuth 2.0.</summary>
    OAuth2,
}

/// <summary>Edits an <see cref="AuthConfig"/> of a request, folder or collection.</summary>
public sealed partial class AuthEditorViewModel : ViewModelBase
{
    private readonly Func<AuthSource?>? _describeInherited;
    private readonly IOAuthTokenService? _tokens;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBearer), nameof(IsBasic), nameof(IsApiKey), nameof(IsOAuth2), nameof(IsInherit), nameof(InheritedDescription))]
    private LocalizedOption<AuthKind> _selectedKind;

    [ObservableProperty]
    private string _token = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _apiKeyName = string.Empty;

    [ObservableProperty]
    private string _apiKeyValue = string.Empty;

    [ObservableProperty]
    private LocalizedOption<ApiKeyLocation> _selectedLocation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthorizationCode), nameof(IsPasswordGrant), nameof(OAuthHint))]
    private LocalizedOption<OAuth2GrantType> _selectedGrant;

    [ObservableProperty]
    private string _tokenUrl = string.Empty;

    [ObservableProperty]
    private string _authorizationUrl = string.Empty;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _clientSecret = string.Empty;

    [ObservableProperty]
    private string _scope = string.Empty;

    [ObservableProperty]
    private string _audience = string.Empty;

    [ObservableProperty]
    private string _oAuthUsername = string.Empty;

    [ObservableProperty]
    private string _oAuthPassword = string.Empty;

    [ObservableProperty]
    private string _redirectUri = OAuth2Auth.DefaultRedirectUri;

    [ObservableProperty]
    private bool _usePkce = true;

    [ObservableProperty]
    private LocalizedOption<OAuth2ClientAuthentication> _selectedClientAuthentication;

    [ObservableProperty]
    private string? _tokenError;

    /// <summary>Creates the editor.</summary>
    /// <param name="auth">The current value.</param>
    /// <param name="allowInherit">False for a collection, which has no parent.</param>
    /// <param name="describeInherited">Describes what "inherit" resolves to, for the hint.</param>
    /// <param name="tokens">OAuth 2.0 tokens; without it the token panel is hidden.</param>
    public AuthEditorViewModel(AuthConfig auth, bool allowInherit = true, Func<AuthSource?>? describeInherited = null, IOAuthTokenService? tokens = null)
    {
        ArgumentNullException.ThrowIfNull(auth);
        _describeInherited = describeInherited;
        _tokens = tokens;
        List<LocalizedOption<AuthKind>> kinds = [new(AuthKind.None, "Auth_None"), new(AuthKind.Bearer, "Auth_Bearer"), new(AuthKind.Basic, "Auth_Basic"), new(AuthKind.ApiKey, "Auth_ApiKey"), new(AuthKind.OAuth2, "Auth_OAuth2")];
        if (allowInherit)
        {
            kinds.Insert(0, new(AuthKind.Inherit, "Auth_Inherit"));
        }

        Kinds = kinds;
        Locations = [new(ApiKeyLocation.Header, "Auth_InHeader"), new(ApiKeyLocation.Query, "Auth_InQuery")];
        Grants = [new(OAuth2GrantType.ClientCredentials, "OAuth_GrantClientCredentials"), new(OAuth2GrantType.AuthorizationCode, "OAuth_GrantAuthorizationCode"), new(OAuth2GrantType.Password, "OAuth_GrantPassword")];
        ClientAuthentications = [new(OAuth2ClientAuthentication.BasicHeader, "OAuth_ClientAuthHeader"), new(OAuth2ClientAuthentication.Body, "OAuth_ClientAuthBody")];
        _selectedLocation = Locations[0];
        _selectedGrant = Grants[0];
        _selectedClientAuthentication = ClientAuthentications[0];

        var kind = auth switch
        {
            InheritAuth => allowInherit ? AuthKind.Inherit : AuthKind.None,
            BearerAuth bearer => Set(AuthKind.Bearer, () => _token = bearer.Token),
            BasicAuth basic => Set(AuthKind.Basic, () => (_username, _password) = (basic.Username, basic.Password)),
            ApiKeyAuth apiKey => Set(AuthKind.ApiKey, () =>
            {
                (_apiKeyName, _apiKeyValue) = (apiKey.Key, apiKey.Value);
                _selectedLocation = Locations.First(l => l.Value == apiKey.Location);
            }),
            OAuth2Auth oauth => Set(AuthKind.OAuth2, () =>
            {
                _selectedGrant = Grants.First(g => g.Value == oauth.GrantType);
                (_tokenUrl, _authorizationUrl, _clientId, _clientSecret) = (oauth.TokenUrl, oauth.AuthorizationUrl, oauth.ClientId, oauth.ClientSecret);
                (_scope, _audience, _oAuthUsername, _oAuthPassword) = (oauth.Scope, oauth.Audience, oauth.Username, oauth.Password);
                (_redirectUri, _usePkce) = (oauth.RedirectUri, oauth.UsePkce);
                _selectedClientAuthentication = ClientAuthentications.First(c => c.Value == oauth.ClientAuthentication);
            }),
            _ => AuthKind.None,
        };
        _selectedKind = Kinds.First(k => k.Value == kind);
        GetTokenCommand.PropertyChanged += OnGetTokenCommandChanged;
    }

    /// <summary>Raised when the value changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Kinds offered.</summary>
    public IReadOnlyList<LocalizedOption<AuthKind>> Kinds { get; }

    /// <summary>API key locations.</summary>
    public IReadOnlyList<LocalizedOption<ApiKeyLocation>> Locations { get; }

    /// <summary>OAuth 2.0 grant types.</summary>
    public IReadOnlyList<LocalizedOption<OAuth2GrantType>> Grants { get; }

    /// <summary>Ways of sending the client credentials.</summary>
    public IReadOnlyList<LocalizedOption<OAuth2ClientAuthentication>> ClientAuthentications { get; }

    /// <summary>Show bearer fields.</summary>
    public bool IsBearer => SelectedKind.Value == AuthKind.Bearer;

    /// <summary>Show basic fields.</summary>
    public bool IsBasic => SelectedKind.Value == AuthKind.Basic;

    /// <summary>Show API key fields.</summary>
    public bool IsApiKey => SelectedKind.Value == AuthKind.ApiKey;

    /// <summary>Show OAuth 2.0 fields.</summary>
    public bool IsOAuth2 => SelectedKind.Value == AuthKind.OAuth2;

    /// <summary>Authorization code grant: show the sign-in fields.</summary>
    public bool IsAuthorizationCode => SelectedGrant.Value == OAuth2GrantType.AuthorizationCode;

    /// <summary>Password grant: show the user name and password.</summary>
    public bool IsPasswordGrant => SelectedGrant.Value == OAuth2GrantType.Password;

    /// <summary>Inheriting.</summary>
    public bool IsInherit => SelectedKind.Value == AuthKind.Inherit;

    /// <summary>How the token is obtained for the selected grant.</summary>
    public string OAuthHint => Loc.Get(IsAuthorizationCode ? "OAuth_HintBrowser" : "OAuth_HintAutomatic");

    /// <summary>Show the token panel: OAuth 2.0 applies (set here or inherited) and tokens are available.</summary>
    public bool ShowToken => _tokens is not null && EffectiveOAuth() is not null;

    /// <summary>True while a token is being fetched (or the browser sign-in is pending).</summary>
    public bool IsFetchingToken => GetTokenCommand.IsRunning;

    /// <summary>A token is cached for the settings.</summary>
    public bool HasToken => EffectiveOAuth() is { } auth && _tokens?.Current(auth) is not null;

    /// <summary>"Access token valid until 14:32." and the like.</summary>
    public string TokenStatus
    {
        get
        {
            if (IsFetchingToken)
            {
                return Loc.Get(EffectiveOAuth()?.GrantType == OAuth2GrantType.AuthorizationCode ? "OAuth_WaitingForBrowser" : "OAuth_Fetching");
            }

            if (EffectiveOAuth() is not { } auth || _tokens?.Current(auth) is not { } token)
            {
                return Loc.Get("OAuth_NoTokenYet");
            }

            if (token.ExpiresAt is not { } expiresAt)
            {
                return Loc.Get("OAuth_TokenNoExpiry");
            }

            if (!token.IsExpired(TimeProvider.System, TimeSpan.Zero))
            {
                var local = expiresAt.ToLocalTime();
                var format = local.Date == DateTimeOffset.Now.Date ? "t" : "g";
                return Loc.Format("OAuth_TokenValidUntil", local.ToString(format, Loc.Instance.Culture));
            }

            var renews = token.RefreshToken is { Length: > 0 } || auth.GrantType != OAuth2GrantType.AuthorizationCode;
            return Loc.Get(renews ? "OAuth_TokenExpiredRenews" : "OAuth_TokenExpired");
        }
    }

    /// <summary>"Uses the auth of Admin: Basic." when inheriting.</summary>
    public string InheritedDescription
    {
        get
        {
            if (!IsInherit || _describeInherited?.Invoke() is not { } source)
            {
                return string.Empty;
            }

            return source.Auth is NoAuth || source.SourceName is null
                ? Loc.Get("Auth_InheritedNone")
                : Loc.Format("Auth_InheritedFrom", source.SourceName, Describe(source.Auth));
        }
    }

    /// <summary>The auth as edited.</summary>
    public AuthConfig ToAuth() => SelectedKind.Value switch
    {
        AuthKind.Inherit => AuthConfig.Inherit,
        AuthKind.Bearer => new BearerAuth(Token),
        AuthKind.Basic => new BasicAuth(Username, Password),
        AuthKind.ApiKey => new ApiKeyAuth(ApiKeyName, ApiKeyValue, SelectedLocation.Value),
        AuthKind.OAuth2 => ToOAuth(),
        _ => AuthConfig.None,
    };

    /// <summary>A short description such as "Bearer token" or "Basic · admin".</summary>
    public static string Describe(AuthConfig auth) => auth switch
    {
        BearerAuth => Loc.Get("Auth_Bearer"),
        BasicAuth basic => $"{Loc.Get("Auth_Basic")} · {basic.Username}",
        ApiKeyAuth apiKey => $"{Loc.Get("Auth_ApiKey")} · {apiKey.Key}",
        OAuth2Auth oauth => $"{Loc.Get("Auth_OAuth2")} · {Loc.Get(GrantLabelKey(oauth.GrantType))}",
        InheritAuth => Loc.Get("Auth_Inherit"),
        _ => Loc.Get("Auth_None"),
    };

    /// <summary>Refreshes the inherited hint and the token state (e.g. after the parent's auth or the environment changed).</summary>
    public void RefreshInherited()
    {
        OnPropertyChanged(nameof(InheritedDescription));
        RefreshToken();
    }

    /// <summary>Refreshes the token state (e.g. after sending got or renewed a token).</summary>
    public void RefreshToken()
    {
        OnPropertyChanged(nameof(ShowToken));
        OnPropertyChanged(nameof(HasToken));
        OnPropertyChanged(nameof(TokenStatus));
        OnPropertyChanged(nameof(IsFetchingToken));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(SelectedKind) or nameof(Token) or nameof(Username) or nameof(Password)
            or nameof(ApiKeyName) or nameof(ApiKeyValue) or nameof(SelectedLocation)
            or nameof(SelectedGrant) or nameof(TokenUrl) or nameof(AuthorizationUrl) or nameof(ClientId) or nameof(ClientSecret)
            or nameof(Scope) or nameof(Audience) or nameof(OAuthUsername) or nameof(OAuthPassword) or nameof(RedirectUri)
            or nameof(UsePkce) or nameof(SelectedClientAuthentication))
        {
            Changed?.Invoke(this, EventArgs.Empty);
            if (e.PropertyName is not (nameof(Token) or nameof(Username) or nameof(Password) or nameof(ApiKeyName) or nameof(ApiKeyValue) or nameof(SelectedLocation)))
            {
                TokenError = null;
                RefreshToken();
            }
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_tokens is null || EffectiveOAuth() is not { } auth)
        {
            return;
        }

        TokenError = null;
        var error = await _tokens.GetNewTokenAsync(auth, cancellationToken).ConfigureAwait(true);
        TokenError = cancellationToken.IsCancellationRequested ? null : error;
    }

    [RelayCommand]
    private void ForgetToken()
    {
        if (EffectiveOAuth() is { } auth)
        {
            _tokens?.Forget(auth);
            TokenError = null;
            RefreshToken();
        }
    }

    private void OnGetTokenCommandChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IAsyncRelayCommand.IsRunning))
        {
            RefreshToken();
        }
    }

    // The OAuth 2.0 settings the token panel works on: the edited ones, or the inherited ones.
    private OAuth2Auth? EffectiveOAuth() => SelectedKind.Value switch
    {
        AuthKind.OAuth2 => ToOAuth(),
        AuthKind.Inherit => _describeInherited?.Invoke()?.Auth as OAuth2Auth,
        _ => null,
    };

    private OAuth2Auth ToOAuth() => new(
        SelectedGrant.Value,
        TokenUrl,
        ClientId,
        ClientSecret,
        Scope,
        Audience,
        IsAuthorizationCode ? AuthorizationUrl : string.Empty,
        IsPasswordGrant ? OAuthUsername : string.Empty,
        IsPasswordGrant ? OAuthPassword : string.Empty,
        string.IsNullOrWhiteSpace(RedirectUri) ? OAuth2Auth.DefaultRedirectUri : RedirectUri,
        UsePkce,
        SelectedClientAuthentication.Value);

    private static string GrantLabelKey(OAuth2GrantType grant) => grant switch
    {
        OAuth2GrantType.AuthorizationCode => "OAuth_GrantAuthorizationCode",
        OAuth2GrantType.Password => "OAuth_GrantPassword",
        _ => "OAuth_GrantClientCredentials",
    };

    private static AuthKind Set(AuthKind kind, Action assign)
    {
        assign();
        return kind;
    }
}
