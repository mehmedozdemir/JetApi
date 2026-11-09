using System.Globalization;
using System.Resources;

namespace JetApi.Cli;

/// <summary>
/// User-facing CLI messages from <c>Messages.resx</c> (English) and <c>Messages.tr.resx</c> (Turkish), in
/// <see cref="CultureInfo.CurrentUICulture"/> (ADR-0006).
/// </summary>
internal static class Messages
{
    private static readonly ResourceManager Resources = new("JetApi.Cli.Messages", typeof(Messages).Assembly);

    /// <summary>The message for <paramref name="key"/>; <c>[key]</c> when it is missing.</summary>
    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";

    /// <summary>Formats the message for <paramref name="key"/>; numbers and dates use <see cref="CultureInfo.CurrentCulture"/>.</summary>
    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
