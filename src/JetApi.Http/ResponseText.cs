using System.Text;

namespace JetApi.Http;

/// <summary>Turns a response body into text.</summary>
public static class ResponseText
{
    /// <summary>Decodes with the <c>charset</c> of <paramref name="contentType"/>; UTF-8 when there is none or it is unknown.</summary>
    public static string Decode(ReadOnlySpan<byte> data, string? contentType)
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
