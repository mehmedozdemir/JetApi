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

/// <summary>A field of the request message, for the field list next to the editor.</summary>
/// <param name="Name">JSON name.</param>
/// <param name="Type">Type description.</param>
public sealed record FieldInfo(string Name, string Type);

/// <summary>An open gRPC request.</summary>
public sealed partial class GrpcRequestTabViewModel : RequestTabViewModel
{
    private ProtoSchema? _schema;
    private IReadOnlyList<string> _protoErrors = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UrlHints), nameof(IsPlaintext))]
    private string _serverUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Methods))]
    private string? _selectedService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputTypeText), nameof(OutputTypeText), nameof(Fields))]
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
        Auth = new AuthEditorViewModel(request.Auth, allowInherit: true, () => RequestRunner.ResolveAuth(Host.Session, CollectionKey, Path ?? [Key], AuthConfig.Inherit));
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

    /// <summary>Variables in the server URL.</summary>
    public IReadOnlyList<VariableHint> UrlHints => BuildHints(ServerUrl);

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

    /// <summary>Fields of the request message.</summary>
    public IReadOnlyList<FieldInfo> Fields => FindMethod()?.InputType is { } type
        ? [.. type.Fields.Select(f => new FieldInfo(f.Name, DescribeType(f)))]
        : [];

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
    };

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
    protected override async Task SendCoreAsync(CancellationToken cancellationToken)
    {
        var request = (GrpcRequestDefinition)BuildRequest();
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
        Response.SetResult(result);
        ResponseSection = ResponseSection.Body;

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

    private static string DescribeType(ProtoField field)
    {
        var type = field.Type switch
        {
            ProtoFieldType.Message => field.MessageType!.FullName.StartsWith("google.protobuf.", StringComparison.Ordinal)
                ? field.MessageType.FullName["google.protobuf.".Length..]
                : field.MessageType.FullName[(field.MessageType.FullName.LastIndexOf('.') + 1)..],
            ProtoFieldType.Enum => field.EnumType!.FullName[(field.EnumType.FullName.LastIndexOf('.') + 1)..] + " (enum)",
            _ => field.Type.ToString().ToLowerInvariant(),
        };
        if (field.IsMap)
        {
            var entry = field.MessageType!;
            return $"map<{entry.FindField(1)!.Type.ToString().ToLowerInvariant()}, {DescribeType(entry.FindField(2)!)}>";
        }

        return field.IsRepeated ? $"repeated {type}" : type;
    }
}
