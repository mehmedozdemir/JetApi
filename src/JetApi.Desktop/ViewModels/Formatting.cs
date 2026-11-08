using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;
using JetApi.Desktop.Localization;

namespace JetApi.Desktop.ViewModels;

/// <summary>Formats sizes, durations and response bodies for display.</summary>
public static class Formatting
{
    private const int PrettyPrintLimit = 5 * 1024 * 1024;
    private const int HexDumpLimit = 64 * 1024;
    private const int BytesPerHexLine = 16;

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>148 ms, 1.24 s.</summary>
    public static string Duration(TimeSpan duration)
    {
        var culture = Loc.Instance.Culture;
        return duration.TotalMilliseconds < 1000
            ? string.Create(culture, $"{duration.TotalMilliseconds:0} ms")
            : string.Create(culture, $"{duration.TotalSeconds:0.00} s");
    }

    /// <summary>"127.0.0.1:5000" for "[::ffff:127.0.0.1]:5000"; other addresses and unparsable text are returned as given.</summary>
    public static string EndPoint(string? endPoint)
    {
        if (string.IsNullOrEmpty(endPoint))
        {
            return string.Empty;
        }

        return System.Net.IPEndPoint.TryParse(endPoint, out var parsed) && parsed.Address.IsIPv4MappedToIPv6
            ? new System.Net.IPEndPoint(parsed.Address.MapToIPv4(), parsed.Port).ToString()
            : endPoint;
    }

    /// <summary>312 B, 4.2 KB, 1.3 MB.</summary>
    public static string Size(long bytes)
    {
        var culture = Loc.Instance.Culture;
        return bytes switch
        {
            < 1024 => string.Create(culture, $"{bytes} B"),
            < 1024 * 1024 => string.Create(culture, $"{bytes / 1024.0:0.#} KB"),
            _ => string.Create(culture, $"{bytes / (1024.0 * 1024):0.#} MB"),
        };
    }

    /// <summary>True when <paramref name="contentType"/> or the content looks like JSON.</summary>
    public static bool LooksLikeJson(string? contentType, string text) =>
        (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false)
        || text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('[');

    /// <summary>True when <paramref name="contentType"/> or the content looks like XML or HTML.</summary>
    public static bool LooksLikeXml(string? contentType, string text) =>
        (contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) ?? false)
        || (contentType?.Contains("html", StringComparison.OrdinalIgnoreCase) ?? false)
        || text.TrimStart().StartsWith('<');

    /// <summary>Pretty-prints JSON or XML; returns the text unchanged when it cannot be parsed or is very large.</summary>
    public static string Pretty(string? contentType, string text)
    {
        if (text.Length > PrettyPrintLimit)
        {
            return text;
        }

        if (LooksLikeJson(contentType, text) && TryFormatJson(text, out var json))
        {
            return json;
        }

        if ((contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) ?? false) || text.TrimStart().StartsWith("<?xml", StringComparison.Ordinal))
        {
            try
            {
                return XDocument.Parse(text).ToString();
            }
            catch (System.Xml.XmlException)
            {
                return text;
            }
        }

        return text;
    }

    /// <summary>Indents JSON, keeping numbers and Unicode text exactly as received.</summary>
    public static bool TryFormatJson(string text, out string formatted)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            formatted = JsonSerializer.Serialize(document.RootElement, IndentedJson);
            return true;
        }
        catch (JsonException)
        {
            formatted = text;
            return false;
        }
    }

    /// <summary>A classic hex dump of the first 64 KB.</summary>
    public static string HexDump(ReadOnlySpan<byte> data)
    {
        var length = Math.Min(data.Length, HexDumpLimit);
        var builder = new StringBuilder((length / BytesPerHexLine + 1) * 78);
        for (var offset = 0; offset < length; offset += BytesPerHexLine)
        {
            var line = data.Slice(offset, Math.Min(BytesPerHexLine, length - offset));
            builder.Append(offset.ToString("x8", CultureInfo.InvariantCulture)).Append("  ");
            for (var i = 0; i < BytesPerHexLine; i++)
            {
                builder.Append(i < line.Length ? line[i].ToString("x2", CultureInfo.InvariantCulture) + " " : "   ");
                if (i == 7)
                {
                    builder.Append(' ');
                }
            }

            builder.Append(' ');
            foreach (var value in line)
            {
                builder.Append(value is >= 32 and < 127 ? (char)value : '.');
            }

            builder.Append('\n');
        }

        if (data.Length > length)
        {
            builder.Append(CultureInfo.InvariantCulture, $"… {data.Length - length} more bytes\n");
        }

        return builder.ToString();
    }

    /// <summary>Decodes a body using the charset from <paramref name="contentType"/>, defaulting to UTF-8.</summary>
    public static string DecodeText(ReadOnlySpan<byte> data, string? contentType)
    {
        var encoding = Encoding.UTF8;
        var charset = contentType?.Split(';').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("charset=", StringComparison.OrdinalIgnoreCase));
        if (charset is not null)
        {
            try
            {
                encoding = Encoding.GetEncoding(charset["charset=".Length..].Trim('"'));
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
            }
        }

        return encoding.GetString(data);
    }
}
