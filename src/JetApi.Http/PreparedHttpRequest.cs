using JetApi.Core.Model;

namespace JetApi.Http;

/// <summary>
/// A request with every variable substituted and auth applied, ready to send.
/// This is exactly what goes on the wire (plus headers added by the transport, which are reported after sending).
/// </summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Url">Absolute http(s) URL including query parameters.</param>
/// <param name="Headers">Request headers in order; duplicates are allowed.</param>
/// <param name="Body">Request body.</param>
/// <param name="Settings">Transport options.</param>
public sealed record PreparedHttpRequest(
    string Method,
    Uri Url,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    PreparedBody Body,
    HttpRequestSettings Settings);

/// <summary>A fully resolved request body.</summary>
public abstract record PreparedBody
{
    /// <summary>No body.</summary>
    public static PreparedBody None { get; } = new NoBody();
}

/// <summary>No body.</summary>
public sealed record NoBody : PreparedBody;

/// <summary>Raw bytes with a content type.</summary>
/// <param name="Content">The bytes to send.</param>
/// <param name="ContentType">The <c>Content-Type</c> header value.</param>
public sealed record BytesBody(ReadOnlyMemory<byte> Content, string ContentType) : PreparedBody;

/// <summary>URL-encoded form fields.</summary>
/// <param name="Fields">Enabled fields in order.</param>
public sealed record FormBody(IReadOnlyList<KeyValuePair<string, string>> Fields) : PreparedBody;

/// <summary>A file streamed as the body.</summary>
/// <param name="FilePath">Absolute path of an existing file.</param>
/// <param name="ContentType">The <c>Content-Type</c> header value.</param>
public sealed record FileBody(string FilePath, string ContentType) : PreparedBody;

/// <summary>A multipart/form-data body.</summary>
/// <param name="Parts">Enabled parts in order.</param>
public sealed record MultipartBody(IReadOnlyList<PreparedMultipartPart> Parts) : PreparedBody;

/// <summary>One part of a multipart body.</summary>
/// <param name="Name">Field name.</param>
/// <param name="Value">Text value, or the absolute file path when <paramref name="IsFile"/> is true.</param>
/// <param name="IsFile">Whether the part is a file upload.</param>
/// <param name="ContentType">Optional part content type.</param>
public sealed record PreparedMultipartPart(string Name, string Value, bool IsFile, string? ContentType);
