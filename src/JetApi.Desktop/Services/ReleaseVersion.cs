using System.Globalization;

namespace JetApi.Desktop.Services;

/// <summary>
/// A semantic version (<c>1.2.3</c>, <c>1.2.3-beta.1</c>) as used by release tags; build metadata after <c>+</c> is ignored.
/// Ordering follows SemVer 2.0 precedence, so <c>0.4.0-beta.1 &lt; 0.4.0</c>.
/// </summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private ReleaseVersion(int major, int minor, int patch, string preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    /// <summary>Major version.</summary>
    public int Major { get; }

    /// <summary>Minor version.</summary>
    public int Minor { get; }

    /// <summary>Patch version.</summary>
    public int Patch { get; }

    /// <summary>Pre-release label (<c>beta.1</c>), empty for a release.</summary>
    public string PreRelease { get; }

    /// <summary>The version is a pre-release.</summary>
    public bool IsPreRelease => PreRelease.Length > 0;

    /// <summary>Parses <c>1.2.3</c>, <c>v1.2.3-rc.1</c> or <c>1.2.3+abc</c>; <c>null</c> when the text is not a version.</summary>
    public static ReleaseVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var preRelease = string.Empty;
        var dash = value.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            preRelease = value[(dash + 1)..];
            value = value[..dash];
            if (preRelease.Length == 0 || preRelease.Split('.').Any(p => p.Length == 0))
            {
                return null;
            }
        }

        var parts = value.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit) || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return new ReleaseVersion(numbers[0], numbers[1], numbers[2], preRelease);
    }

    /// <inheritdoc />
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
        {
            return core;
        }

        // A release ranks above its pre-releases.
        if (!IsPreRelease || !other.IsPreRelease)
        {
            return other.IsPreRelease.CompareTo(IsPreRelease);
        }

        var mine = PreRelease.Split('.');
        var theirs = other.PreRelease.Split('.');
        for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            var compared = ComparePart(mine[i], theirs[i]);
            if (compared != 0)
            {
                return compared;
            }
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    /// <inheritdoc />
    public bool Equals(ReleaseVersion? other) => CompareTo(other) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    /// <inheritdoc />
    public override string ToString() =>
        IsPreRelease ? FormattableString.Invariant($"{Major}.{Minor}.{Patch}-{PreRelease}") : FormattableString.Invariant($"{Major}.{Minor}.{Patch}");

    /// <summary>Less than.</summary>
    public static bool operator <(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) < 0;

    /// <summary>Greater than.</summary>
    public static bool operator >(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) > 0;

    /// <summary>Less than or equal.</summary>
    public static bool operator <=(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) <= 0;

    /// <summary>Greater than or equal.</summary>
    public static bool operator >=(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) >= 0;

    /// <summary>Equal.</summary>
    public static bool operator ==(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) == 0;

    /// <summary>Not equal.</summary>
    public static bool operator !=(ReleaseVersion? left, ReleaseVersion? right) => Compare(left, right) != 0;

    private static int Compare(ReleaseVersion? left, ReleaseVersion? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);

    // Numeric identifiers compare as numbers and rank below alphanumeric ones.
    private static int ComparePart(string left, string right)
    {
        var leftNumeric = left.All(char.IsAsciiDigit);
        var rightNumeric = right.All(char.IsAsciiDigit);
        if (leftNumeric && rightNumeric)
        {
            var byLength = left.TrimStart('0').Length.CompareTo(right.TrimStart('0').Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(left.TrimStart('0'), right.TrimStart('0'));
        }

        return leftNumeric != rightNumeric ? (leftNumeric ? -1 : 1) : string.CompareOrdinal(left, right);
    }
}
