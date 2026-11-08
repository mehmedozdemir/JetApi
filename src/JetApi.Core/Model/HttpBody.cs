namespace JetApi.Core.Model;

/// <summary>How the body of an HTTP request is encoded.</summary>
public enum HttpBodyMode
{
    /// <summary>No body.</summary>
    None,

    /// <summary>Raw JSON text, sent as <c>application/json</c>.</summary>
    Json,

    /// <summary>Raw XML text, sent as <c>application/xml</c>.</summary>
    Xml,

    /// <summary>Raw text, sent as <c>text/plain</c>.</summary>
    Text,

    /// <summary><c>application/x-www-form-urlencoded</c> fields.</summary>
    FormUrlEncoded,

    /// <summary><c>multipart/form-data</c> fields and files.</summary>
    Multipart,

    /// <summary>The contents of a file, sent as-is.</summary>
    File,
}

/// <summary>Kind of a multipart field.</summary>
public enum MultipartFieldKind
{
    /// <summary>A text value.</summary>
    Text,

    /// <summary>A file; the value is the file path.</summary>
    File,
}

/// <summary>A multipart/form-data field.</summary>
/// <param name="Name">Field name.</param>
/// <param name="Value">Text value, or file path for <see cref="MultipartFieldKind.File"/>.</param>
/// <param name="Kind">Field kind.</param>
/// <param name="IsEnabled">Disabled fields are not sent.</param>
/// <param name="ContentType">Optional explicit content type of the part.</param>
public sealed record MultipartField(
    string Name,
    string Value,
    MultipartFieldKind Kind = MultipartFieldKind.Text,
    bool IsEnabled = true,
    string? ContentType = null);

/// <summary>The body of an HTTP request. Only the members relevant to <see cref="Mode"/> are used.</summary>
public sealed record HttpBody
{
    /// <summary>No body.</summary>
    public static HttpBody Empty { get; } = new();

    /// <summary>Body encoding.</summary>
    public HttpBodyMode Mode { get; init; } = HttpBodyMode.None;

    /// <summary>Raw content for <see cref="HttpBodyMode.Json"/>, <see cref="HttpBodyMode.Xml"/> and <see cref="HttpBodyMode.Text"/>.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Fields for <see cref="HttpBodyMode.FormUrlEncoded"/>.</summary>
    public IReadOnlyList<KeyValueEntry> FormFields { get; init; } = [];

    /// <summary>Fields for <see cref="HttpBodyMode.Multipart"/>.</summary>
    public IReadOnlyList<MultipartField> MultipartFields { get; init; } = [];

    /// <summary>File path for <see cref="HttpBodyMode.File"/>, relative paths are relative to the workspace root.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>Creates a JSON body.</summary>
    public static HttpBody Json(string content) => new() { Mode = HttpBodyMode.Json, Content = content };

    /// <summary>Creates a plain text body.</summary>
    public static HttpBody Text(string content) => new() { Mode = HttpBodyMode.Text, Content = content };

    /// <summary>Creates an XML body.</summary>
    public static HttpBody Xml(string content) => new() { Mode = HttpBodyMode.Xml, Content = content };

    /// <summary>Creates a URL-encoded form body.</summary>
    public static HttpBody Form(IReadOnlyList<KeyValueEntry> fields) => new() { Mode = HttpBodyMode.FormUrlEncoded, FormFields = fields };
}
