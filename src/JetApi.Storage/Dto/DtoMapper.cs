using System.Globalization;
using JetApi.Core.Model;

namespace JetApi.Storage.Dto;

/// <summary>
/// Converts between the domain model and file DTOs. Reading validates and throws
/// <see cref="InvalidDataException"/> with a user-facing message; writing omits defaults.
/// </summary>
internal static class DtoMapper
{
    // ---- Variables and key/value entries -------------------------------------------------

    public static List<VariableDto>? ToDto(IReadOnlyList<Variable> variables) =>
        variables.Count == 0
            ? null
            : [.. variables.Select(v => new VariableDto
            {
                Name = v.Name,
                Value = v.Value,
                Enabled = v.IsEnabled ? null : false,
                Secret = v.IsSecret ? true : null,
            })];

    public static IReadOnlyList<Variable> FromDto(List<VariableDto>? variables, string section) =>
        variables is null
            ? []
            : [.. variables.Select((v, i) => new Variable(
                Required(v.Name, $"{section}[{i}].name"),
                v.Value ?? string.Empty,
                v.Enabled ?? true,
                v.Secret ?? false))];

    public static List<KeyValueDto>? ToDto(IReadOnlyList<KeyValueEntry> entries) =>
        entries.Count == 0
            ? null
            : [.. entries.Select(e => new KeyValueDto
            {
                Name = e.Key,
                Value = e.Value,
                Enabled = e.IsEnabled ? null : false,
                Description = e.Description,
            })];

    public static IReadOnlyList<KeyValueEntry> FromDto(List<KeyValueDto>? entries, string section) =>
        entries is null
            ? []
            : [.. entries.Select((e, i) => new KeyValueEntry(
                Required(e.Name, $"{section}[{i}].name"),
                e.Value ?? string.Empty,
                e.Enabled ?? true,
                e.Description))];

    // ---- Auth ----------------------------------------------------------------------------

    public static AuthDto? ToDto(AuthConfig auth, AuthConfig omitWhen) =>
        auth == omitWhen
            ? null
            : auth switch
            {
                InheritAuth => new AuthDto { Type = "inherit" },
                NoAuth => new AuthDto { Type = "none" },
                BearerAuth bearer => new AuthDto { Type = "bearer", Token = bearer.Token },
                BasicAuth basic => new AuthDto { Type = "basic", Username = basic.Username, Password = basic.Password },
                ApiKeyAuth apiKey => new AuthDto
                {
                    Type = "apikey",
                    Key = apiKey.Key,
                    Value = apiKey.Value,
                    In = apiKey.Location == ApiKeyLocation.Query ? "query" : null,
                },
                OAuth2Auth oauth => ToDto(oauth),
                _ => throw new NotSupportedException($"Unsupported auth type {auth.GetType().Name}."),
            };

    public static AuthConfig FromDto(AuthDto? auth, AuthConfig whenMissing)
    {
        if (auth is null)
        {
            return whenMissing;
        }

        return Required(auth.Type, "auth.type") switch
        {
            "inherit" => AuthConfig.Inherit,
            "none" => AuthConfig.None,
            "bearer" => new BearerAuth(auth.Token ?? string.Empty),
            "basic" => new BasicAuth(auth.Username ?? string.Empty, auth.Password ?? string.Empty),
            "apikey" => new ApiKeyAuth(
                Required(auth.Key, "auth.key"),
                auth.Value ?? string.Empty,
                auth.In switch
                {
                    null or "header" => ApiKeyLocation.Header,
                    "query" => ApiKeyLocation.Query,
                    var other => throw Invalid(Messages.Format("Storage_InvalidApiKeyLocation", other)),
                }),
            "oauth2" => FromOAuth2Dto(auth),
            var other => throw Invalid(Messages.Format("Storage_UnknownAuthType", other)),
        };
    }

    private static AuthDto ToDto(OAuth2Auth oauth) => new()
    {
        Type = "oauth2",
        Grant = oauth.GrantType switch
        {
            OAuth2GrantType.ClientCredentials => "client_credentials",
            OAuth2GrantType.AuthorizationCode => "authorization_code",
            OAuth2GrantType.Password => "password",
            _ => throw new NotSupportedException($"Unsupported grant type {oauth.GrantType}."),
        },
        TokenUrl = EmptyAsNull(oauth.TokenUrl),
        AuthUrl = EmptyAsNull(oauth.AuthorizationUrl),
        ClientId = EmptyAsNull(oauth.ClientId),
        ClientSecret = EmptyAsNull(oauth.ClientSecret),
        Scope = EmptyAsNull(oauth.Scope),
        Audience = EmptyAsNull(oauth.Audience),
        Username = EmptyAsNull(oauth.Username),
        Password = EmptyAsNull(oauth.Password),
        RedirectUri = oauth.RedirectUri == OAuth2Auth.DefaultRedirectUri ? null : EmptyAsNull(oauth.RedirectUri),
        Pkce = oauth.UsePkce ? null : false,
        ClientAuth = oauth.ClientAuthentication == OAuth2ClientAuthentication.Body ? "body" : null,
    };

    private static OAuth2Auth FromOAuth2Dto(AuthDto auth) => new(
        Required(auth.Grant, "auth.grant") switch
        {
            "client_credentials" => OAuth2GrantType.ClientCredentials,
            "authorization_code" => OAuth2GrantType.AuthorizationCode,
            "password" => OAuth2GrantType.Password,
            var other => throw Invalid(Messages.Format("Storage_InvalidOAuth2Grant", other)),
        },
        TokenUrl: auth.TokenUrl ?? string.Empty,
        ClientId: auth.ClientId ?? string.Empty,
        ClientSecret: auth.ClientSecret ?? string.Empty,
        Scope: auth.Scope ?? string.Empty,
        Audience: auth.Audience ?? string.Empty,
        AuthorizationUrl: auth.AuthUrl ?? string.Empty,
        Username: auth.Username ?? string.Empty,
        Password: auth.Password ?? string.Empty,
        RedirectUri: string.IsNullOrWhiteSpace(auth.RedirectUri) ? OAuth2Auth.DefaultRedirectUri : auth.RedirectUri,
        UsePkce: auth.Pkce ?? true,
        ClientAuthentication: auth.ClientAuth switch
        {
            null or "basic" => OAuth2ClientAuthentication.BasicHeader,
            "body" => OAuth2ClientAuthentication.Body,
            var other => throw Invalid(Messages.Format("Storage_InvalidOAuth2ClientAuth", other)),
        });

    private static string? EmptyAsNull(string value) => value.Length == 0 ? null : value;

    // ---- HTTP request --------------------------------------------------------------------

    public static HttpRequestFile ToDto(HttpRequestDefinition request) => new()
    {
        SchemaVersion = WorkspaceLayout.SchemaVersion,
        Name = request.Name,
        Description = request.Description,
        Method = request.Method,
        Url = request.Url,
        Params = ToDto(request.QueryParameters),
        Headers = ToDto(request.Headers),
        Auth = ToDto(request.Auth, AuthConfig.Inherit),
        Body = ToDto(request.Body),
        Settings = ToDto(request.Settings),
    };

    public static HttpRequestDefinition FromDto(HttpRequestFile file, string key) => new()
    {
        Key = key,
        Name = Required(file.Name, "name"),
        Description = file.Description,
        Method = string.IsNullOrWhiteSpace(file.Method) ? "GET" : file.Method.Trim().ToUpperInvariant(),
        Url = file.Url ?? string.Empty,
        QueryParameters = FromDto(file.Params, "params"),
        Headers = FromDto(file.Headers, "headers"),
        Auth = FromDto(file.Auth, AuthConfig.Inherit),
        Body = FromDto(file.Body),
        Settings = FromDto(file.Settings),
    };

    private static BodyDto? ToDto(HttpBody body) => body.Mode switch
    {
        HttpBodyMode.None => null,
        HttpBodyMode.Json => new BodyDto { Type = "json", Content = NormalizeNewLines(body.Content) },
        HttpBodyMode.Xml => new BodyDto { Type = "xml", Content = NormalizeNewLines(body.Content) },
        HttpBodyMode.Text => new BodyDto { Type = "text", Content = NormalizeNewLines(body.Content) },
        HttpBodyMode.FormUrlEncoded => new BodyDto { Type = "form", Fields = ToDto(body.FormFields) ?? [] },
        HttpBodyMode.Multipart => new BodyDto
        {
            Type = "multipart",
            Parts = [.. body.MultipartFields.Select(field => new MultipartFieldDto
            {
                Name = field.Name,
                Value = field.Kind == MultipartFieldKind.Text ? field.Value : null,
                File = field.Kind == MultipartFieldKind.File ? field.Value : null,
                ContentType = field.ContentType,
                Enabled = field.IsEnabled ? null : false,
            })],
        },
        HttpBodyMode.File => new BodyDto { Type = "file", File = body.FilePath },
        _ => throw new NotSupportedException($"Unsupported body mode {body.Mode}."),
    };

    private static HttpBody FromDto(BodyDto? body)
    {
        if (body is null)
        {
            return HttpBody.Empty;
        }

        return Required(body.Type, "body.type") switch
        {
            "none" => HttpBody.Empty,
            "json" => HttpBody.Json(body.Content ?? string.Empty),
            "xml" => HttpBody.Xml(body.Content ?? string.Empty),
            "text" => HttpBody.Text(body.Content ?? string.Empty),
            "form" => HttpBody.Form(FromDto(body.Fields, "body.fields")),
            "multipart" => new HttpBody
            {
                Mode = HttpBodyMode.Multipart,
                MultipartFields = [.. (body.Parts ?? []).Select(FromDto)],
            },
            "file" => new HttpBody { Mode = HttpBodyMode.File, FilePath = Required(body.File, "body.file") },
            var other => throw Invalid(Messages.Format("Storage_UnknownBodyType", other)),
        };
    }

    private static MultipartField FromDto(MultipartFieldDto part, int index)
    {
        var name = Required(part.Name, $"body.parts[{index}].name");
        if (part.File is not null && part.Value is not null)
        {
            throw Invalid(Messages.Format("Storage_MultipartValueAndFile", index));
        }

        return part.File is not null
            ? new MultipartField(name, part.File, MultipartFieldKind.File, part.Enabled ?? true, part.ContentType)
            : new MultipartField(name, part.Value ?? string.Empty, MultipartFieldKind.Text, part.Enabled ?? true, part.ContentType);
    }

    private static HttpSettingsDto? ToDto(HttpRequestSettings settings) =>
        settings == HttpRequestSettings.Default
            ? null
            : new HttpSettingsDto
            {
                FollowRedirects = settings.FollowRedirects ? null : false,
                ValidateCertificates = settings.ValidateCertificates ? null : false,
                TimeoutMs = ToMilliseconds(settings.Timeout),
            };

    private static HttpRequestSettings FromDto(HttpSettingsDto? settings) =>
        settings is null
            ? HttpRequestSettings.Default
            : new HttpRequestSettings(
                settings.FollowRedirects ?? true,
                settings.ValidateCertificates ?? true,
                FromMilliseconds(settings.TimeoutMs, "settings.timeoutMs"));

    // ---- gRPC request --------------------------------------------------------------------

    public static GrpcRequestFile ToDto(GrpcRequestDefinition request) => new()
    {
        SchemaVersion = WorkspaceLayout.SchemaVersion,
        Name = request.Name,
        Description = request.Description,
        Url = request.ServerUrl,
        Service = request.Service,
        Method = request.Method,
        Metadata = ToDto(request.Metadata),
        Auth = ToDto(request.Auth, AuthConfig.Inherit),
        Message = NormalizeNewLines(request.Message),
        Settings = request.Settings == GrpcRequestSettings.Default
            ? null
            : new GrpcSettingsDto
            {
                ValidateCertificates = request.Settings.ValidateCertificates ? null : false,
                DeadlineMs = ToMilliseconds(request.Settings.Deadline),
            },
    };

    public static GrpcRequestDefinition FromDto(GrpcRequestFile file, string key) => new()
    {
        Key = key,
        Name = Required(file.Name, "name"),
        Description = file.Description,
        ServerUrl = file.Url ?? string.Empty,
        Service = file.Service ?? string.Empty,
        Method = file.Method ?? string.Empty,
        Metadata = FromDto(file.Metadata, "metadata"),
        Auth = FromDto(file.Auth, AuthConfig.Inherit),
        Message = file.Message ?? "{}",
        Settings = file.Settings is null
            ? GrpcRequestSettings.Default
            : new GrpcRequestSettings(
                file.Settings.ValidateCertificates ?? true,
                FromMilliseconds(file.Settings.DeadlineMs, "settings.deadlineMs")),
    };

    // ---- Proto settings ------------------------------------------------------------------

    public static ProtoDto? ToDto(ProtoSettings proto) =>
        proto.ImportPaths.Count == 0 && proto.Files.Count == 0
            ? null
            : new ProtoDto
            {
                ImportPaths = proto.ImportPaths.Count == 0 ? null : [.. proto.ImportPaths],
                Files = proto.Files.Count == 0 ? null : [.. proto.Files],
            };

    public static ProtoSettings FromDto(ProtoDto? proto) =>
        proto is null ? ProtoSettings.Empty : new ProtoSettings(proto.ImportPaths ?? [], proto.Files ?? []);

    // ---- Helpers -------------------------------------------------------------------------

    public static void EnsureSchemaVersion(int schemaVersion)
    {
        if (schemaVersion < 1)
        {
            throw Invalid(Messages.Get("Storage_SchemaVersionMissing"));
        }

        if (schemaVersion > WorkspaceLayout.SchemaVersion)
        {
            // Invariant: schemaVersion values are shown exactly as written in the YAML, never with digit grouping.
            throw Invalid(Messages.Format(
                "Storage_SchemaVersionTooNew",
                schemaVersion.ToString(CultureInfo.InvariantCulture),
                WorkspaceLayout.SchemaVersion.ToString(CultureInfo.InvariantCulture)));
        }
    }

    public static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw Invalid(Messages.Format("Storage_Required", field)) : value;

    public static string NormalizeNewLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static int? ToMilliseconds(TimeSpan? value) =>
        value is null ? null : checked((int)value.Value.TotalMilliseconds);

    private static TimeSpan? FromMilliseconds(int? value, string field) =>
        value switch
        {
            null => null,
            <= 0 => throw Invalid(Messages.Format("Storage_PositiveMilliseconds", field)),
            _ => TimeSpan.FromMilliseconds(value.Value),
        };

    private static InvalidDataException Invalid(string message) => new(message);
}
