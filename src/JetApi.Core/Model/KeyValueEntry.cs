namespace JetApi.Core.Model;

/// <summary>
/// A named value that can be toggled on or off, used for query parameters, headers,
/// form fields and gRPC metadata. Values may contain <c>{{variable}}</c> placeholders.
/// </summary>
/// <param name="Key">The name (header name, parameter name, ...).</param>
/// <param name="Value">The raw, unresolved value.</param>
/// <param name="IsEnabled">Disabled entries are kept in the file but never sent.</param>
/// <param name="Description">Optional human-readable note.</param>
public sealed record KeyValueEntry(string Key, string Value, bool IsEnabled = true, string? Description = null);
