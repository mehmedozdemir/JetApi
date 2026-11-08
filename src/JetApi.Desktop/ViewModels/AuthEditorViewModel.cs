using CommunityToolkit.Mvvm.ComponentModel;
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
}

/// <summary>Edits an <see cref="AuthConfig"/> of a request, folder or collection.</summary>
public sealed partial class AuthEditorViewModel : ViewModelBase
{
    private readonly Func<AuthSource?>? _describeInherited;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBearer), nameof(IsBasic), nameof(IsApiKey), nameof(IsInherit), nameof(InheritedDescription))]
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

    /// <summary>Creates the editor.</summary>
    /// <param name="auth">The current value.</param>
    /// <param name="allowInherit">False for a collection, which has no parent.</param>
    /// <param name="describeInherited">Describes what "inherit" resolves to, for the hint.</param>
    public AuthEditorViewModel(AuthConfig auth, bool allowInherit = true, Func<AuthSource?>? describeInherited = null)
    {
        ArgumentNullException.ThrowIfNull(auth);
        _describeInherited = describeInherited;
        Kinds = allowInherit
            ? [new(AuthKind.Inherit, "Auth_Inherit"), new(AuthKind.None, "Auth_None"), new(AuthKind.Bearer, "Auth_Bearer"), new(AuthKind.Basic, "Auth_Basic"), new(AuthKind.ApiKey, "Auth_ApiKey")]
            : [new(AuthKind.None, "Auth_None"), new(AuthKind.Bearer, "Auth_Bearer"), new(AuthKind.Basic, "Auth_Basic"), new(AuthKind.ApiKey, "Auth_ApiKey")];
        Locations = [new(ApiKeyLocation.Header, "Auth_InHeader"), new(ApiKeyLocation.Query, "Auth_InQuery")];
        _selectedLocation = Locations[0];

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
            _ => AuthKind.None,
        };
        _selectedKind = Kinds.First(k => k.Value == kind);
    }

    /// <summary>Raised when the value changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Kinds offered.</summary>
    public IReadOnlyList<LocalizedOption<AuthKind>> Kinds { get; }

    /// <summary>API key locations.</summary>
    public IReadOnlyList<LocalizedOption<ApiKeyLocation>> Locations { get; }

    /// <summary>Show bearer fields.</summary>
    public bool IsBearer => SelectedKind.Value == AuthKind.Bearer;

    /// <summary>Show basic fields.</summary>
    public bool IsBasic => SelectedKind.Value == AuthKind.Basic;

    /// <summary>Show API key fields.</summary>
    public bool IsApiKey => SelectedKind.Value == AuthKind.ApiKey;

    /// <summary>Inheriting.</summary>
    public bool IsInherit => SelectedKind.Value == AuthKind.Inherit;

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
        _ => AuthConfig.None,
    };

    /// <summary>A short description such as "Bearer token" or "Basic · admin".</summary>
    public static string Describe(AuthConfig auth) => auth switch
    {
        BearerAuth => Loc.Get("Auth_Bearer"),
        BasicAuth basic => $"{Loc.Get("Auth_Basic")} · {basic.Username}",
        ApiKeyAuth apiKey => $"{Loc.Get("Auth_ApiKey")} · {apiKey.Key}",
        InheritAuth => Loc.Get("Auth_Inherit"),
        _ => Loc.Get("Auth_None"),
    };

    /// <summary>Refreshes the inherited hint (e.g. after the parent's auth changed).</summary>
    public void RefreshInherited() => OnPropertyChanged(nameof(InheritedDescription));

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(SelectedKind) or nameof(Token) or nameof(Username) or nameof(Password)
            or nameof(ApiKeyName) or nameof(ApiKeyValue) or nameof(SelectedLocation))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static AuthKind Set(AuthKind kind, Action assign)
    {
        assign();
        return kind;
    }
}
