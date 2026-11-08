using System.Globalization;

namespace JetApi.Core.Variables;

/// <summary>
/// Built-in variables whose names start with <c>$</c> and produce a fresh value on every use,
/// e.g. <c>{{$guid}}</c>.
/// </summary>
public sealed class DynamicVariables
{
    private const int RandomIntUpperBound = 1000;

    private readonly TimeProvider _timeProvider;
    private readonly Random _random;

    /// <summary>Creates the provider; inject a fake clock and seeded random in tests.</summary>
    public DynamicVariables(TimeProvider? timeProvider = null, Random? random = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _random = random ?? Random.Shared;
    }

    /// <summary>Names of all supported dynamic variables (without the leading <c>$</c>).</summary>
    public static IReadOnlyList<string> SupportedNames { get; } =
        ["guid", "randomUUID", "timestamp", "timestampMs", "isoTimestamp", "randomInt"];

    /// <summary>Produces a value for <paramref name="name"/> (with the leading <c>$</c>).</summary>
    public bool TryEvaluate(string name, out string value)
    {
        ArgumentNullException.ThrowIfNull(name);

        var now = _timeProvider.GetUtcNow();
        string? result = name switch
        {
            "$guid" or "$randomUUID" => Guid.NewGuid().ToString(),
            "$timestamp" => now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            "$timestampMs" => now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            "$isoTimestamp" => now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            "$randomInt" => _random.Next(RandomIntUpperBound).ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

        value = result ?? string.Empty;
        return result is not null;
    }
}
