// File-format DTOs. They mirror docs/file-format.md exactly; property declaration order is the
// order keys are written in, so keep it stable to avoid noisy diffs. Defaults are represented
// as null so they are omitted from the files.
namespace JetApi.Storage.Dto;

internal sealed class WorkspaceFile
{
    public int SchemaVersion { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<VariableDto>? Globals { get; set; }
    public List<string>? Collections { get; set; }
    public List<string>? Environments { get; set; }
}

internal sealed class EnvironmentFile
{
    public int SchemaVersion { get; set; }
    public string? Name { get; set; }
    public List<VariableDto>? Variables { get; set; }
}

internal sealed class CollectionFile
{
    public int SchemaVersion { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public AuthDto? Auth { get; set; }
    public List<VariableDto>? Variables { get; set; }
    public ProtoDto? Proto { get; set; }
    public List<string>? Order { get; set; }
}

internal sealed class FolderFile
{
    public int SchemaVersion { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public AuthDto? Auth { get; set; }
    public List<string>? Order { get; set; }
}

internal sealed class HttpRequestFile
{
    public int SchemaVersion { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Method { get; set; }
    public string? Url { get; set; }
    public List<KeyValueDto>? Params { get; set; }
    public List<KeyValueDto>? Headers { get; set; }
    public AuthDto? Auth { get; set; }
    public BodyDto? Body { get; set; }
    public HttpSettingsDto? Settings { get; set; }
}

internal sealed class GrpcRequestFile
{
    public int SchemaVersion { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? Service { get; set; }
    public string? Method { get; set; }
    public List<KeyValueDto>? Metadata { get; set; }
    public AuthDto? Auth { get; set; }
    public string? Message { get; set; }
    public GrpcSettingsDto? Settings { get; set; }
}

internal sealed class VariableDto
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public bool? Enabled { get; set; }
    public bool? Secret { get; set; }
}

internal sealed class KeyValueDto
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public bool? Enabled { get; set; }
    public string? Description { get; set; }
}

// Property order is the key order in files: shared keys first, then type-specific ones.
internal sealed class AuthDto
{
    public string? Type { get; set; }
    public string? Grant { get; set; }
    public string? TokenUrl { get; set; }
    public string? AuthUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scope { get; set; }
    public string? Audience { get; set; }
    public string? Token { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Key { get; set; }
    public string? Value { get; set; }
    public string? In { get; set; }
    public string? RedirectUri { get; set; }
    public bool? Pkce { get; set; }
    public string? ClientAuth { get; set; }
}

internal sealed class BodyDto
{
    public string? Type { get; set; }
    public string? Content { get; set; }
    public List<KeyValueDto>? Fields { get; set; }
    public List<MultipartFieldDto>? Parts { get; set; }
    public string? File { get; set; }
}

internal sealed class MultipartFieldDto
{
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? File { get; set; }
    public string? ContentType { get; set; }
    public bool? Enabled { get; set; }
}

internal sealed class HttpSettingsDto
{
    public bool? FollowRedirects { get; set; }
    public bool? ValidateCertificates { get; set; }
    public int? TimeoutMs { get; set; }
}

internal sealed class GrpcSettingsDto
{
    public bool? ValidateCertificates { get; set; }
    public int? DeadlineMs { get; set; }
}

internal sealed class ProtoDto
{
    public List<string>? ImportPaths { get; set; }
    public List<string>? Files { get; set; }
}
