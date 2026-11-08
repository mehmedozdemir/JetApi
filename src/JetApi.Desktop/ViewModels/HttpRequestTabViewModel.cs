using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Core.Model;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Http;
using JetApi.Storage;

namespace JetApi.Desktop.ViewModels;

/// <summary>Sections of the request editor.</summary>
public enum RequestSection
{
    /// <summary>Query parameters.</summary>
    Params,

    /// <summary>Headers or metadata.</summary>
    Headers,

    /// <summary>Auth.</summary>
    Auth,

    /// <summary>Body or message.</summary>
    Body,

    /// <summary>Settings.</summary>
    Settings,
}

/// <summary>Sections of the response panel.</summary>
public enum ResponseSection
{
    /// <summary>Body or response message.</summary>
    Body,

    /// <summary>Headers.</summary>
    Headers,

    /// <summary>Timing or trailers.</summary>
    Timing,

    /// <summary>Sent request or metadata.</summary>
    Sent,
}

/// <summary>A body type pill; selecting it changes the body mode of its tab.</summary>
public sealed class BodyModeChoice : ViewModelBase, ILabeled
{
    private readonly HttpRequestTabViewModel _owner;

    /// <summary>Creates the choice.</summary>
    public BodyModeChoice(HttpRequestTabViewModel owner, LocalizedOption<HttpBodyMode> option)
    {
        _owner = owner;
        Option = option;
    }

    /// <summary>The option.</summary>
    public LocalizedOption<HttpBodyMode> Option { get; }

    /// <inheritdoc />
    public string Label => Option.Label;

    /// <summary>True for the tab's current body mode.</summary>
    public bool IsSelected
    {
        get => _owner.SelectedBodyMode == Option;
        set
        {
            if (value)
            {
                _owner.SelectedBodyMode = Option;
            }
        }
    }

    /// <summary>Refreshes <see cref="IsSelected"/>.</summary>
    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>An open HTTP request.</summary>
public sealed partial class HttpRequestTabViewModel : RequestTabViewModel
{
    /// <summary>Methods offered in the method box (custom methods can be typed).</summary>
    public static readonly IReadOnlyList<string> CommonMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    private const string DefaultUserAgent = "JetApi/0.1.0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge))]
    private string _method;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UrlHints))]
    private string _url;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRawBody), nameof(IsFormBody), nameof(IsMultipartBody), nameof(IsFileBody), nameof(IsNoBody), nameof(BodyLanguage), nameof(AutoHeaders), nameof(BodyBadge))]
    private LocalizedOption<HttpBodyMode> _selectedBodyMode;

    [ObservableProperty]
    private string _bodyText;

    [ObservableProperty]
    private string _bodyFilePath;

    [ObservableProperty]
    private bool _followRedirects;

    [ObservableProperty]
    private bool _validateCertificates;

    [ObservableProperty]
    private string _timeoutText;

    [ObservableProperty]
    private RequestSection _section = RequestSection.Body;

    [ObservableProperty]
    private ResponseSection _responseSection = ResponseSection.Body;

    [ObservableProperty]
    private string? _bodyError;

    /// <summary>Opens <paramref name="request"/> in a tab.</summary>
    public HttpRequestTabViewModel(IRequestHost host, string collectionKey, IReadOnlyList<string>? path, HttpRequestDefinition request)
        : base(host, collectionKey, path, request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _method = request.Method;
        _url = request.Url;
        BodyModes =
        [
            new(HttpBodyMode.None, "Req_Body_None"), new(HttpBodyMode.Json, "Req_Body_Json"), new(HttpBodyMode.Xml, "Req_Body_Xml"),
            new(HttpBodyMode.Text, "Req_Body_Text"), new(HttpBodyMode.FormUrlEncoded, "Req_Body_Form"),
            new(HttpBodyMode.Multipart, "Req_Body_Multipart"), new(HttpBodyMode.File, "Req_Body_File"),
        ];
        _selectedBodyMode = BodyModes.First(m => m.Value == request.Body.Mode);
        BodyModeChoices = [.. BodyModes.Select(mode => new BodyModeChoice(this, mode))];
        _bodyText = request.Body.Content;
        _bodyFilePath = request.Body.FilePath;
        _followRedirects = request.Settings.FollowRedirects;
        _validateCertificates = request.Settings.ValidateCertificates;
        _timeoutText = request.Settings.Timeout is { } timeout ? ((int)timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) : string.Empty;
        _section = request.Body.Mode == HttpBodyMode.None ? (request.QueryParameters.Count > 0 ? RequestSection.Params : RequestSection.Headers) : RequestSection.Body;

        QueryParameters = new KeyValueListViewModel(request.QueryParameters);
        Headers = new KeyValueListViewModel(request.Headers);
        FormFields = new KeyValueListViewModel(request.Body.FormFields);
        MultipartFields = KeyValueListViewModel.FromMultipart(request.Body.MultipartFields);
        Auth = new AuthEditorViewModel(
            request.Auth,
            allowInherit: true,
            () => RequestRunner.ResolveAuth(Host.Session, CollectionKey, Path ?? [Key], AuthConfig.Inherit),
            new OAuthTokenService(Host.Session, Host.Runner, () => CollectionKey, Host.Dialogs.OpenBrowserAsync));

        QueryParameters.Changed += (_, _) => OnEdited(nameof(QueryParameters));
        Headers.Changed += (_, _) => OnEdited(nameof(Headers));
        FormFields.Changed += (_, _) => OnEdited(nameof(FormFields));
        MultipartFields.Changed += (_, _) => OnEdited(nameof(MultipartFields));
        Auth.Changed += (_, _) => OnEdited(nameof(Auth));
        UpdateDirty();
    }

    /// <inheritdoc />
    public override bool IsGrpc => false;

    /// <inheritdoc />
    public override string Badge => Method.Length > 6 ? Method[..6] : Method;

    /// <summary>Body modes.</summary>
    public IReadOnlyList<LocalizedOption<HttpBodyMode>> BodyModes { get; }

    /// <summary>Body modes as selectable pills.</summary>
    public IReadOnlyList<BodyModeChoice> BodyModeChoices { get; }

    /// <summary>Query parameters.</summary>
    public KeyValueListViewModel QueryParameters { get; }

    /// <summary>Headers.</summary>
    public KeyValueListViewModel Headers { get; }

    /// <summary>Form fields.</summary>
    public KeyValueListViewModel FormFields { get; }

    /// <summary>Multipart fields (rows with "file" checked are file parts).</summary>
    public KeyValueListViewModel MultipartFields { get; }

    /// <summary>Auth editor.</summary>
    public AuthEditorViewModel Auth { get; }

    /// <summary>The response panel.</summary>
    public HttpResponseViewModel Response { get; } = new();

    /// <summary>Variables used in the URL with their values.</summary>
    public IReadOnlyList<VariableHint> UrlHints => BuildHints(Url);

    /// <summary>Raw text body (JSON, XML, text).</summary>
    public bool IsRawBody => SelectedBodyMode.Value is HttpBodyMode.Json or HttpBodyMode.Xml or HttpBodyMode.Text;

    /// <summary>Form body.</summary>
    public bool IsFormBody => SelectedBodyMode.Value == HttpBodyMode.FormUrlEncoded;

    /// <summary>Multipart body.</summary>
    public bool IsMultipartBody => SelectedBodyMode.Value == HttpBodyMode.Multipart;

    /// <summary>File body.</summary>
    public bool IsFileBody => SelectedBodyMode.Value == HttpBodyMode.File;

    /// <summary>No body.</summary>
    public bool IsNoBody => SelectedBodyMode.Value == HttpBodyMode.None;

    /// <summary>Editor syntax.</summary>
    public string BodyLanguage => SelectedBodyMode.Value switch
    {
        HttpBodyMode.Json => "json",
        HttpBodyMode.Xml => "xml",
        _ => "text",
    };

    /// <summary>Body tab badge.</summary>
    public string BodyBadge => SelectedBodyMode.Value == HttpBodyMode.None ? string.Empty : SelectedBodyMode.Label;

    /// <summary>Auth tab badge.</summary>
    public string AuthBadge => Auth.IsInherit ? Loc.Get("Auth_InheritBadge") : string.Empty;

    /// <summary>Headers JetApi will add, unless the user sets the same header.</summary>
    public IReadOnlyList<HeaderLine> AutoHeaders
    {
        get
        {
            var user = Headers.ToEntries().Where(h => h.IsEnabled).Select(h => h.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var lines = new List<HeaderLine>();
            var source = RequestRunner.ResolveAuth(Host.Session, CollectionKey, Path ?? [Key], Auth.ToAuth());
            if (source.Auth is BearerAuth or BasicAuth && !user.Contains("Authorization"))
            {
                var origin = source.SourceName is null ? AuthEditorViewModel.Describe(source.Auth) : $"{AuthEditorViewModel.Describe(source.Auth)} · {source.SourceName}";
                lines.Add(new HeaderLine("Authorization", Loc.Format("Req_AutoFromAuth", origin), true));
            }
            else if (source.Auth is ApiKeyAuth { Location: ApiKeyLocation.Header } apiKey && !user.Contains(apiKey.Key))
            {
                lines.Add(new HeaderLine(apiKey.Key, Loc.Format("Req_AutoFromAuth", AuthEditorViewModel.Describe(apiKey)), true));
            }

            var contentType = SelectedBodyMode.Value switch
            {
                HttpBodyMode.Json => "application/json",
                HttpBodyMode.Xml => "application/xml",
                HttpBodyMode.Text => "text/plain; charset=utf-8",
                HttpBodyMode.FormUrlEncoded => "application/x-www-form-urlencoded",
                HttpBodyMode.Multipart => "multipart/form-data; boundary=…",
                HttpBodyMode.File => "application/octet-stream",
                _ => null,
            };
            if (contentType is not null && !user.Contains("Content-Type"))
            {
                lines.Add(new HeaderLine("Content-Type", $"{contentType}  ({Loc.Get("Req_AutoFromBody")})", true));
            }

            foreach (var (name, value) in new[] { ("User-Agent", DefaultUserAgent), ("Accept", "*/*"), ("Accept-Encoding", "gzip, deflate, br") })
            {
                if (!user.Contains(name))
                {
                    lines.Add(new HeaderLine(name, value, true));
                }
            }

            return lines;
        }
    }

    /// <summary>Selects a request section.</summary>
    [RelayCommand]
    private void ShowSection(RequestSection section) => Section = section;

    /// <summary>Selects a response section.</summary>
    [RelayCommand]
    private void ShowResponseSection(ResponseSection section) => ResponseSection = section;

    /// <summary>Indents the JSON body.</summary>
    [RelayCommand]
    private void FormatBody()
    {
        if (SelectedBodyMode.Value == HttpBodyMode.Json)
        {
            if (Formatting.TryFormatJson(BodyText, out var formatted))
            {
                BodyText = formatted;
                BodyError = null;
            }
            else
            {
                BodyError = Loc.Get("Req_FormatInvalidJson");
            }
        }
    }

    /// <summary>Picks the file for a file body.</summary>
    [RelayCommand]
    private async Task BrowseBodyFileAsync()
    {
        var file = await Host.Dialogs.PickFileAsync(Loc.Get("Req_Body_FilePath")).ConfigureAwait(true);
        if (file is not null)
        {
            BodyFilePath = System.IO.Path.GetRelativePath(Host.Session.Root, file).Replace('\\', '/');
        }
    }

    /// <summary>Copies the response body.</summary>
    [RelayCommand]
    private Task CopyResponseAsync() => Host.Dialogs.CopyToClipboardAsync(Response.BodyForCopy);

    /// <summary>Saves the raw response body to a file.</summary>
    [RelayCommand]
    private async Task SaveResponseAsync()
    {
        var extension = Response.BodyLanguage switch { "json" => ".json", "xml" => ".xml", _ => ".txt" };
        var target = await Host.Dialogs.PickSaveFileAsync(Loc.Get("Btn_SaveToFile"), Key + extension).ConfigureAwait(true);
        if (target is not null)
        {
            await File.WriteAllBytesAsync(target, Response.RawBody.ToArray()).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public override RequestNode BuildRequest() => new HttpRequestDefinition
    {
        Key = Key,
        Name = Name,
        Description = Description,
        Method = string.IsNullOrWhiteSpace(Method) ? "GET" : Method.Trim().ToUpperInvariant(),
        Url = Url,
        QueryParameters = QueryParameters.ToEntries(),
        Headers = Headers.ToEntries(),
        Auth = Auth.ToAuth(),
        Body = new HttpBody
        {
            Mode = SelectedBodyMode.Value,
            Content = IsRawBody ? BodyText : string.Empty,
            FormFields = IsFormBody ? FormFields.ToEntries() : [],
            MultipartFields = IsMultipartBody ? MultipartFields.ToMultipartFields() : [],
            FilePath = IsFileBody ? BodyFilePath : string.Empty,
        },
        Settings = new HttpRequestSettings(
            FollowRedirects,
            ValidateCertificates,
            int.TryParse(TimeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) && ms > 0 ? TimeSpan.FromMilliseconds(ms) : null),
    };

    /// <inheritdoc />
    public override void OnContextChanged()
    {
        base.OnContextChanged();
        OnPropertyChanged(nameof(UrlHints));
        OnPropertyChanged(nameof(AutoHeaders));
        Auth.RefreshInherited();
    }

    /// <inheritdoc />
    protected override void OnTokenChanged() => Auth.RefreshToken();

    /// <inheritdoc />
    protected override async Task SendCoreAsync(CancellationToken cancellationToken)
    {
        var request = (HttpRequestDefinition)BuildRequest();
        if (!RequestRunner.SetsAuthorization(request.Headers) && await EnsureOAuthTokenAsync(request.Auth, cancellationToken).ConfigureAwait(true) is { } tokenError)
        {
            Response.SetPreparationErrors([tokenError]);
            return;
        }

        var preparation = RequestRunner.PrepareHttp(Host.Session, CollectionKey, Path ?? [Key], request);
        if (!preparation.IsSuccess)
        {
            Response.SetPreparationErrors(preparation.Errors);
            return;
        }

        if (!await ConfirmVariablesAsync(preparation.VariableIssues).ConfigureAwait(true))
        {
            return;
        }

        Response.IsSending = true;
        var result = await Host.Runner.ExecuteHttpAsync(preparation.Request!, cancellationToken).ConfigureAwait(true);
        Response.SetResult(result, preparation.Request!.Headers);
        ResponseSection = result.Outcome == HttpExecutionOutcome.Completed ? ResponseSection.Body : ResponseSection.Timing;

        Host.AddHistory(new HistoryEntry(
            DateTimeOffset.Now,
            IsGrpc: false,
            Name,
            $"{request.Method} {request.Url}",
            Response.StatusText,
            (long)(result.Timings?.Total.TotalMilliseconds ?? 0),
            CollectionKey,
            Path,
            RequestFiles.Serialize(request)));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Method) or nameof(Url) or nameof(SelectedBodyMode) or nameof(BodyText) or nameof(BodyFilePath)
            or nameof(FollowRedirects) or nameof(ValidateCertificates) or nameof(TimeoutText) or nameof(Name))
        {
            UpdateDirtySafe();
        }

        if (e.PropertyName == nameof(BodyText))
        {
            BodyError = null;
        }

        if (e.PropertyName == nameof(SelectedBodyMode) && BodyModeChoices is not null)
        {
            foreach (var choice in BodyModeChoices)
            {
                choice.Refresh();
            }
        }
    }

    private void OnEdited(string section)
    {
        OnPropertyChanged(section);
        if (section is nameof(Headers) or nameof(Auth))
        {
            OnPropertyChanged(nameof(AutoHeaders));
            OnPropertyChanged(nameof(AuthBadge));
        }

        UpdateDirtySafe();
    }

    // Called from OnPropertyChanged, which also fires during construction before every list exists.
    private void UpdateDirtySafe()
    {
        if (QueryParameters is not null && Headers is not null && FormFields is not null && MultipartFields is not null && Auth is not null)
        {
            UpdateDirty();
        }
    }
}
