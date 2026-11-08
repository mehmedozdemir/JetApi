using System.Globalization;
using System.Text;

namespace JetApi.Core.Model;

/// <summary>
/// Stable, file-system-safe identifier of a node (collection, folder, request, environment)
/// among its siblings. Keys double as file and directory names, so they are lowercase
/// kebab-case ASCII and never change when the display name changes unless the user renames
/// the node explicitly.
/// </summary>
public static class NodeKey
{
    /// <summary>Maximum key length, keeps paths short on Windows.</summary>
    public const int MaxLength = 64;

    private const string Fallback = "item";

    /// <summary>Creates a key from a display name, e.g. <c>"Get User (v2)"</c> becomes <c>"get-user-v2"</c>.</summary>
    public static string FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var builder = new StringBuilder(name.Length);
        var lastWasSeparator = true;
        foreach (var character in TransliterateToAscii(name))
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        var key = builder.ToString().Trim('-');
        if (key.Length > MaxLength)
        {
            key = key[..MaxLength].TrimEnd('-');
        }

        return key.Length == 0 ? Fallback : key;
    }

    /// <summary>
    /// Creates a key from <paramref name="name"/> that does not collide with
    /// <paramref name="existingKeys"/> by appending <c>-2</c>, <c>-3</c>, ...
    /// </summary>
    public static string CreateUnique(string name, IEnumerable<string> existingKeys)
    {
        ArgumentNullException.ThrowIfNull(existingKeys);

        var taken = new HashSet<string>(existingKeys, StringComparer.OrdinalIgnoreCase);
        var baseKey = FromName(name);
        if (!taken.Contains(baseKey))
        {
            return baseKey;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{baseKey}-{suffix}");
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Returns true when <paramref name="key"/> is a valid node key.</summary>
    public static bool IsValid(string? key) =>
        !string.IsNullOrEmpty(key)
        && key.Length <= MaxLength
        && key[0] != '-'
        && key[^1] != '-'
        && key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    // Turkish and other Latin letters map to their closest ASCII form so that
    // "Kullanıcı Güncelle" becomes "kullanici-guncelle" rather than "kullan-c-g-ncelle".
    private static IEnumerable<char> TransliterateToAscii(string value)
    {
        foreach (var character in value)
        {
            switch (character)
            {
                case 'ı': yield return 'i'; continue;
                case 'İ': yield return 'I'; continue;
                case 'ş': yield return 's'; continue;
                case 'Ş': yield return 'S'; continue;
                case 'ğ': yield return 'g'; continue;
                case 'Ğ': yield return 'G'; continue;
                case 'ß': yield return 's'; yield return 's'; continue;
            }

            var decomposed = character.ToString().Normalize(NormalizationForm.FormD);
            foreach (var part in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(part) != UnicodeCategory.NonSpacingMark)
                {
                    yield return part;
                }
            }
        }
    }
}
