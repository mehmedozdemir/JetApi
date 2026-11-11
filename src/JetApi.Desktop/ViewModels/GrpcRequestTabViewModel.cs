using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JetApi.Core.Model;
using JetApi.Desktop.Localization;
using JetApi.Desktop.Services;
using JetApi.Grpc.Codec;
using JetApi.Grpc.Schema;
using JetApi.Storage;

namespace JetApi.Desktop.ViewModels;

/// <summary>A method in the method picker.</summary>
/// <param name="Name">Method name.</param>
/// <param name="IsUnary">Only unary methods can be called for now.</param>
public sealed record MethodOption(string Name, bool IsUnary)
{
    /// <summary>"GetUser · unary" or "Watch · streaming (v0.4)".</summary>
    public string Label => IsUnary ? $"{Name} · unary" : $"{Name} · {Loc.Get("Grpc_StreamingLater")}";
}

/// <summary>An open gRPC request.</summary>
public sealed partial class GrpcRequestTabViewModel : RequestTabViewModel
{
    private ProtoSchema? _schema;
    private IReadOnlyList<string> _protoErrors = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UrlPreview), nameof(IsPlaintext))]
    private string _serverUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Methods))]
    private string? _selectedService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputTypeText), nameof(OutputTypeText), nameof(Fields), nameof(RequestType), nameof(HasFields), nameof(FieldsBadge))]
    private MethodOption? _selectedMethod;

    [ObservableProperty]
    private string _message;

    [ObservableProperty]
    private string? _messageError;

    [ObservableProperty]
    private bool _validateCertificates;

    [ObservableProperty]
    private string _deadlineText;

    [ObservableProperty]
    private bool _emitDefaults = true;

    [ObservableProperty]
    private RequestSection _section = RequestSection.Body;

    [ObservableProperty]
    private ResponseSection _responseSection = ResponseSection.Body;

    /// <summary>Opens <paramref name="request"/> in a tab.</summary>
    public GrpcRequestTabViewModel(IRequestHost host, string collectionKey, IReadOnlyList<string>? path, GrpcRequestDefinition request)
        : base(host, collectionKey, path, request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _serverUrl = request.ServerUrl;
        _message = request.Message;
        _validateCertificates = request.Settings.ValidateCertificates;
        _deadlineText = request.Settings.Deadline is { } deadline ? ((int)deadline.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) : string.Empty;
        Metadata = new KeyValueListViewModel(request.Metadata);
        Auth = new AuthEditorViewModel(
            request.Auth,
            allowInherit: true,
            () => RequestRunner.ResolveAuth(Host.Session, CollectionKey, Path ?? [Key], AuthConfig.Inherit),
            new OAuthTokenService(Host.Session, Host.Runner, () => CollectionKey, Host.Dialogs.OpenBrowserAsync));
        Metadata.Changed += (_, _) => OnEdited(nameof(Metadata));
        Auth.Changed += (_, _) => OnEdited(nameof(Auth));

        LoadSchema();
        _selectedService = Services.Contains(request.Service) ? request.Service : (request.Service.Length > 0 ? request.Service : (Services.Count > 0 ? Services[0] : null));
        if (!Services.Contains(_selectedService ?? string.Empty) && _selectedService is not null)
        {
            Services = [.. Services, _selectedService];
        }

        _selectedMethod = Methods.FirstOrDefault(m => m.Name == request.Method) ?? (request.Method.Length > 0 ? new MethodOption(request.Method, true) : (Methods.Count > 0 ? Methods[0] : null));
        ValidateMessage();
        UpdateDirty();
    }

    /// <inheritdoc />
    public override bool IsGrpc => true;

    /// <inheritdoc />
    public override string Badge => "gRPC";

    /// <summary>Services from the collection's proto files.</summary>
    public IReadOnlyList<string> Services { get; private set; } = [];

    /// <summary>Methods of the selected service.</summary>
    public IReadOnlyList<MethodOption> Methods =>
        _schema?.Services.FirstOrDefault(s => s.FullName == SelectedService) is { } service
            ? [.. service.Methods.Select(m => new MethodOption(m.Name, m.IsUnary))]
            : SelectedMethod is { } current ? [current] : [];

    /// <summary>Metadata.</summary>
    public KeyValueListViewModel Metadata { get; }

    /// <summary>Auth editor.</summary>
    public AuthEditorViewModel Auth { get; }

    /// <summary>The response panel.</summary>
    public GrpcResponseViewModel Response { get; } = new();

    /// <summary>True when the server URL is plaintext (h2c).</summary>
    public bool IsPlaintext => RequestRunner.CreateResolver(Host.Session, CollectionKey).Resolve(ServerUrl).Value.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    /// <summary>The server address with its variables filled in.</summary>
    public AddressPreview UrlPreview => BuildPreview(ServerUrl);

    /// <summary>Proto loaded without errors.</summary>
    public bool IsProtoLoaded => _schema is not null;

    /// <summary>Proto files have errors.</summary>
    public bool HasProtoErrors => _protoErrors.Count > 0;

    /// <summary>The collection has no proto files configured.</summary>
    public bool HasNoProto => _schema is null && _protoErrors.Count == 0;

    /// <summary>Proto errors as "file:line:col message".</summary>
    public IReadOnlyList<string> ProtoErrors => _protoErrors;

    /// <summary>Loaded proto files, comma separated.</summary>
    public string ProtoFilesText => string.Join(", ", Host.Session.Workspace.FindCollection(CollectionKey)?.Proto.Files ?? []);

    /// <summary>"3 services · 14 messages · …".</summary>
    public string ProtoSummary => _schema is null
        ? string.Empty
        : Loc.Format("Grpc_ProtoSummary", _schema.Services.Count, _schema.Messages.Values.Count(m => !m.IsMapEntry && !m.FullName.StartsWith("google.protobuf.", StringComparison.Ordinal)));

    /// <summary>"Request message: users.v1.GetUserRequest".</summary>
    public string InputTypeText => FindMethod()?.InputType.FullName is { } name ? Loc.Format("Grpc_RequestType", name) : string.Empty;

    /// <summary>"Response message: users.v1.User".</summary>
    public string OutputTypeText => FindMethod()?.OutputType.FullName is { } name ? Loc.Format("Grpc_ResponseType", name) : string.Empty;

    /// <summary>The request message type; the message editor suggests its fields.</summary>
    public ProtoMessageType? RequestType => FindMethod()?.InputType;

    /// <summary>Fields of the request message, for the field list.</summary>
    public IReadOnlyList<FieldSuggestion> Fields => RequestType is { } type ? [.. type.Fields.Select(FieldSuggestion.From)] : [];

    /// <summary>A request message is known, so its fields can be listed.</summary>
    public bool HasFields => RequestType is not null;

    /// <summary>The number of fields, on the "Fields" button.</summary>
    public string FieldsBadge => (RequestType?.Fields.Count ?? 0).ToString(CultureInfo.InvariantCulture);

    /// <summary>Metadata tab badge.</summary>
    public string MetadataBadge => Metadata.Badge;

    /// <summary>Auth tab badge.</summary>
    public string AuthBadge => Auth.IsInherit ? Loc.Get("Auth_InheritBadge") : string.Empty;

    /// <summary>Replaces the message with an example of the request type.</summary>
    [RelayCommand]
    private void FillTemplate()
    {
        if (FindMethod() is { } method)
        {
            Message = ProtoJsonTemplate.Create(method.InputType);
        }
    }

    /// <summary>Indents the message JSON.</summary>
    [RelayCommand]
    private void FormatMessage()
    {
        if (Formatting.TryFormatJson(Message, out var formatted))
        {
            Message = formatted;
        }
    }

    /// <summary>Reloads the proto files.</summary>
    [RelayCommand]
    public void ReloadProto()
    {
        Host.Runner.ProtoSchemas.Clear();
        LoadSchema();
        OnPropertyChanged(string.Empty);
        ValidateMessage();
    }

    /// <summary>Picks <c>.proto</c> files on disk, adds them to the collection, saves it and loads them.</summary>
    [RelayCommand]
    private async Task AddProtoFilesAsync()
    {
        var files = await Host.Dialogs.PickProtoFilesAsync(Loc.Get("Proto_PickTitle")).ConfigureAwait(true);
        if (files.Count == 0 || Host.Session.Workspace.FindCollection(CollectionKey) is not { } collection)
        {
            return;
        }

        var result = ProtoPaths.AddFiles(collection.Proto, Host.Session.Root, files);
        try
        {
            await Host.Session.SaveCollectionAsync(collection with { Proto = result.Settings }).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await Host.Dialogs.ShowErrorAsync(Loc.Format("Error_Save", exception.Message)).ConfigureAwait(true);
            return;
        }

        ProtoNote = result.OutsideWorkspace ? Loc.Get("Proto_OutsideWorkspace") : null;
        ReloadProto();
    }

    /// <summary>A note after adding proto files (e.g. they lie outside the workspace).</summary>
    [ObservableProperty]
    public partial string? ProtoNote { get; private set; }

    /// <summary>Opens the collection's proto settings.</summary>
    [RelayCommand]
    private void OpenProtoSettings() => Host.OpenCollectionSettings(CollectionKey);

    /// <summary>Selects a request section.</summary>
    [RelayCommand]
    private void ShowSection(RequestSection section) => Section = section;

    /// <summary>Selects a response section.</summary>
    [RelayCommand]
    private void ShowResponseSection(ResponseSection section) => ResponseSection = section;

    /// <summary>Copies the response JSON.</summary>
    [RelayCommand]
    private Task CopyResponseAsync() => Host.Dialogs.CopyToClipboardAsync(Response.ResponseJson);

    /// <inheritdoc />
    public override RequestNode BuildRequest() => new GrpcRequestDefinition
    {
        Key = Key,
        Name = Name,
        Description = Description,
        ServerUrl = ServerUrl,
        Service = SelectedService ?? string.Empty,
        Method = SelectedMethod?.Name ?? string.Empty,
        Message = Message,
        Metadata = Metadata.ToEntries(),
        Auth = Auth.ToAuth(),
        Settings = new GrpcRequestSettings(
            ValidateCertificates,
            int.TryParse(DeadlineText, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) && ms > 0 ? TimeSpan.FromMilliseconds(ms) : null),
        Checks = Tests?.ToChecks() ?? [],
        Extractions = Tests?.ToExtractions() ?? [],
        Scripts = Scripts,
    };

    /// <inheritdoc />
    protected override void ShowTestsSection() => Section = RequestSection.Tests;

    /// <inheritdoc />
    public override void OnContextChanged()
    {
        base.OnContextChanged();
        var service = SelectedService;
        var method = SelectedMethod?.Name;
        LoadSchema();
        OnPropertyChanged(string.Empty);
        if (method is not null && SelectedService == service && Methods.FirstOrDefault(m => m.Name == method) is { } match)
        {
            SelectedMethod = match;
        }

        Auth.RefreshInherited();
        ValidateMessage();
    }

    /// <inheritdoc />
    protected override void OnTokenChanged() => Auth.RefreshToken();

    /// <inheritdoc />
    protected override async Task SendCoreAsync(CancellationToken cancellationToken)
    {
        var request = (GrpcRequestDefinition)BuildRequest();
        if (!RequestRunner.SetsAuthorization(request.Metadata) && await EnsureOAuthTokenAsync(request.Auth, cancellationToken).ConfigureAwait(true) is { } tokenError)
        {
            Response.SetPreparationErrors([tokenError]);
            return;
        }

        var outcome = Host.Runner.PrepareGrpc(Host.Session, CollectionKey, Path ?? [Key], request);
        if (!outcome.Preparation.IsSuccess)
        {
            Response.SetPreparationErrors(outcome.Preparation.Errors);
            return;
        }

        if (!await ConfirmVariablesAsync(outcome.Preparation.VariableIssues).ConfigureAwait(true))
        {
            return;
        }

        Response.IsSending = true;
        var result = await Host.Runner.InvokeGrpcAsync(outcome.Preparation.Call!, outcome.Schema!, new ProtoJsonDecodeOptions(EmitDefaults), cancellationToken).ConfigureAwait(true);
        var showTests = ResponseSection == ResponseSection.Tests;
        Response.SetResult(result);
        ResponseSection = ResponseSection.Body;
        await RunTestsAsync(result.Outcome == JetApi.Grpc.GrpcCallOutcome.Completed
            ? new JetApi.Testing.ResponseSnapshot(result.StatusCode, [.. result.ResponseHeaders, .. result.Trailers], result.ResponseJson, result.Duration)
            : JetApi.Testing.ResponseSnapshot.None).ConfigureAwait(true);
        if (showTests && TestRun is not null)
        {
            ResponseSection = ResponseSection.Tests;
        }

        Host.AddHistory(new HistoryEntry(
            DateTimeOffset.Now,
            IsGrpc: true,
            Name,
            $"{request.Service}/{request.Method}",
            result.StatusName,
            (long)result.Duration.TotalMilliseconds,
            CollectionKey,
            Path,
            RequestFiles.Serialize(request)));
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(ServerUrl) or nameof(SelectedService) or nameof(SelectedMethod) or nameof(Message)
            or nameof(ValidateCertificates) or nameof(DeadlineText) or nameof(Name))
        {
            if (Metadata is not null && Auth is not null)
            {
                UpdateDirty();
            }
        }

        if (e.PropertyName == nameof(SelectedService) && Methods.Count > 0 && (SelectedMethod is null || Methods.All(m => m.Name != SelectedMethod.Name)))
        {
            SelectedMethod = Methods.FirstOrDefault(m => m.IsUnary) ?? Methods[0];
        }

        if (e.PropertyName is nameof(Message) or nameof(SelectedMethod))
        {
            ValidateMessage();
        }
    }

    private void OnEdited(string section)
    {
        OnPropertyChanged(section);
        OnPropertyChanged(nameof(MetadataBadge));
        OnPropertyChanged(nameof(AuthBadge));
        UpdateDirty();
    }

    private void LoadSchema()
    {
        var collection = Host.Session.Workspace.FindCollection(CollectionKey);
        if (collection is null || collection.Proto.Files.Count == 0)
        {
            _schema = null;
            _protoErrors = [];
            Services = [];
            return;
        }

        var result = Host.Runner.ProtoSchemas.Get(Host.Session.Root, collection);
        _schema = result.Schema;
        _protoErrors = [.. result.Diagnostics.Where(d => d.IsError).Select(RequestRunner.FormatDiagnostic)];
        Services = _schema is null ? [] : [.. _schema.Services.Select(s => s.FullName)];
    }

    // Live validation: resolves variables, then checks the JSON against the request type.
    private void ValidateMessage()
    {
        if (_schema is null || FindMethod() is not { } method)
        {
            MessageError = null;
            return;
        }

        try
        {
            var resolved = RequestRunner.CreateResolver(Host.Session, CollectionKey).Resolve(Message).Value;
            new ProtoJsonCodec(_schema).Encode(method.InputType, resolved);
            MessageError = null;
        }
        catch (ProtoJsonException exception)
        {
            MessageError = exception.Message;
        }
    }

    private ProtoMethod? FindMethod() =>
        _schema is null || SelectedService is null || SelectedMethod is null ? null : _schema.FindMethod(SelectedService, SelectedMethod.Name);
}
