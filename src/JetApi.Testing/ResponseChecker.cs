using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using JetApi.Core.Model;
using JetApi.Testing.JsonPath;
using LateApexEarlySpeed.Json.Schema;
using LateApexEarlySpeed.Json.Schema.Common;

namespace JetApi.Testing;

/// <summary>What checks and extractions look at: the parts of an HTTP or gRPC response.</summary>
/// <param name="StatusCode">HTTP status, or the gRPC status code number; <c>null</c> when there is no response.</param>
/// <param name="Headers">Response headers (and gRPC trailers).</param>
/// <param name="Body">The body as text (gRPC: the message as JSON); <c>null</c> when there is no response.</param>
/// <param name="Duration">How long the call took.</param>
public sealed record ResponseSnapshot(int? StatusCode, IReadOnlyList<KeyValuePair<string, string>> Headers, string? Body, TimeSpan? Duration)
{
    /// <summary>No response (e.g. the connection failed).</summary>
    public static ResponseSnapshot None { get; } = new(null, [], null, null);
}

/// <summary>The outcome of one check.</summary>
/// <param name="Check">The check, as written.</param>
/// <param name="Passed">It held.</param>
/// <param name="Actual">The actual value (shortened), when there is one.</param>
/// <param name="Message">Why it failed; <c>null</c> when it passed.</param>
public sealed record CheckResult(ResponseCheck Check, bool Passed, string? Actual, string? Message);

/// <summary>Runs no-code checks (ADR-0005) on a response.</summary>
public static class ResponseChecker
{
    private const int MaxShown = 300;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Runs the enabled checks.</summary>
    /// <param name="response">The response.</param>
    /// <param name="checks">The checks of the request.</param>
    /// <param name="resolve">Fills <c>{{variables}}</c> in targets and expected values; none when <c>null</c>.</param>
    public static IReadOnlyList<CheckResult> Run(ResponseSnapshot response, IEnumerable<ResponseCheck> checks, Func<string, string>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(checks);
        return [.. checks.Where(c => c.IsEnabled).Select(c => Evaluate(response, c, resolve))];
    }

    /// <summary>Runs one check.</summary>
    public static CheckResult Evaluate(ResponseSnapshot response, ResponseCheck check, Func<string, string>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(check);
        resolve ??= static s => s;
        var target = resolve(check.Target);
        var expected = resolve(check.Expected);
        return check.Source switch
        {
            CheckSource.Status => response.StatusCode is { } status
                ? Compare(check, status.ToString(CultureInfo.InvariantCulture), expected, string.Empty)
                : Fail(check, null, Messages.Get("Check_NoResponse")),
            CheckSource.Header => response.StatusCode is null
                ? Fail(check, null, Messages.Get("Check_NoResponse"))
                : Compare(check, HeaderValue(response, target), expected, Messages.Format("Check_NoHeader", target)),
            CheckSource.Body => response.Body is null
                ? Fail(check, null, Messages.Get("Check_NoResponse"))
                : Compare(check, response.Body.Length == 0 ? null : response.Body, expected, Messages.Get("Check_EmptyBody")),
            CheckSource.Time => response.Duration is { } duration
                ? Compare(check, ((long)Math.Round(duration.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture), expected, string.Empty)
                : Fail(check, null, Messages.Get("Check_NoResponse")),
            CheckSource.Json => EvaluateJson(response, check, target, expected),
            CheckSource.Schema => EvaluateSchema(response, check, expected),
            _ => Fail(check, null, Messages.Get("Check_OperatorNotSupported")),
        };
    }

    /// <summary>The value of a header (several are joined with ", "); <c>null</c> when it is missing.</summary>
    internal static string? HeaderValue(ResponseSnapshot response, string name)
    {
        var values = response.Headers.Where(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).ToList();
        return values.Count == 0 ? null : string.Join(", ", values);
    }

    /// <summary>Parses the body as JSON; <c>null</c> with an error message when it is not JSON.</summary>
    internal static JsonDocument? ParseJson(string body, out string? error)
    {
        try
        {
            error = null;
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            error = Messages.Get("Check_BodyNotJson");
            return null;
        }
    }

    // Checks on a text value (status, header, body, time); actual is null when it is missing.
    private static CheckResult Compare(ResponseCheck check, string? actual, string expected, string missing)
    {
        var shown = Shorten(actual);
        switch (check.Operator)
        {
            case CheckOperator.Exists:
                return actual is not null ? Pass(check, shown) : Fail(check, null, missing);
            case CheckOperator.NotExists:
                return actual is null ? Pass(check, null) : Fail(check, shown, Messages.Format("Check_ExpectedMissing", shown));
        }

        if (actual is null)
        {
            return Fail(check, null, missing);
        }

        return check.Operator switch
        {
            CheckOperator.Equals => TextEquals(actual, expected) ? Pass(check, shown) : Fail(check, shown, Messages.Format("Check_ExpectedValue", expected, shown)),
            CheckOperator.NotEquals => !TextEquals(actual, expected) ? Pass(check, shown) : Fail(check, shown, Messages.Format("Check_ExpectedOther", expected)),
            CheckOperator.GreaterThan or CheckOperator.LessThan => CompareNumbers(check, actual, expected, shown),
            CheckOperator.Contains => actual.Contains(expected, StringComparison.Ordinal) ? Pass(check, shown) : Fail(check, shown, Messages.Format("Check_ExpectedContains", expected, shown)),
            CheckOperator.NotContains => !actual.Contains(expected, StringComparison.Ordinal) ? Pass(check, shown) : Fail(check, shown, Messages.Format("Check_ExpectedNotContains", expected, shown)),
            CheckOperator.Matches => Match(check, actual, expected, shown),
            _ => Fail(check, shown, Messages.Get("Check_OperatorNotSupported")),
        };
    }

    private static CheckResult EvaluateJson(ResponseSnapshot response, ResponseCheck check, string path, string expected)
    {
        if (response.Body is null)
        {
            return Fail(check, null, Messages.Get("Check_NoResponse"));
        }

        var query = JsonPathQuery.Parse(path);
        if (query.Query is null)
        {
            return Fail(check, null, query.Error);
        }

        using var document = ParseJson(response.Body, out var error);
        if (document is null)
        {
            return Fail(check, null, error);
        }

        var nodes = query.Query.Select(document.RootElement);
        var missing = Messages.Format("Check_NoJsonValue", path);
        switch (check.Operator)
        {
            case CheckOperator.Exists:
                return nodes.Count > 0 ? Pass(check, Shorten(Display(nodes))) : Fail(check, null, missing);
            case CheckOperator.NotExists:
                return nodes.Count == 0 ? Pass(check, null) : Fail(check, Shorten(Display(nodes)), Messages.Format("Check_ExpectedMissing", Shorten(Display(nodes))));
        }

        if (nodes.Count == 0)
        {
            return Fail(check, null, missing);
        }

        // Several nodes (e.g. $.items[*].id) are checked as one array.
        using var several = nodes.Count > 1 ? JsonDocument.Parse(JsonSerializer.Serialize(nodes)) : null;
        var actual = several?.RootElement ?? nodes[0];
        var shown = Shorten(JsonValues.Display(actual));
        return check.Operator switch
        {
            CheckOperator.Equals => JsonEquals(actual, expected) ? Pass(check, shown) : Fail(check, shown, Messages.Format("Check_ExpectedValue", expected, shown)),
            CheckOperator.NotEquals => !JsonEquals(actual, expected) ? Pass(check, shown) : Fail(check, shown, Messages.Format("Check_ExpectedOther", expected)),
            CheckOperator.GreaterThan or CheckOperator.LessThan => CompareNumbers(check, JsonValues.Display(actual), expected, shown),
            CheckOperator.Contains => JsonContains(actual, expected) ? Pass(check, shown) : Fail(check, shown, Messages.Format("Check_ExpectedContains", expected, shown)),
            CheckOperator.NotContains => !JsonContains(actual, expected) ? Pass(check, shown) : Fail(check, shown, Messages.Format("Check_ExpectedNotContains", expected, shown)),
            CheckOperator.Matches => Match(check, JsonValues.Display(actual), expected, shown),
            _ => Fail(check, shown, Messages.Get("Check_OperatorNotSupported")),
        };
    }

    private static CheckResult EvaluateSchema(ResponseSnapshot response, ResponseCheck check, string schema)
    {
        if (response.Body is null)
        {
            return Fail(check, null, Messages.Get("Check_NoResponse"));
        }

        JsonValidator validator;
        try
        {
            validator = new JsonValidator(schema);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException)
        {
            return Fail(check, null, Messages.Format("Check_SchemaInvalid", exception.Message));
        }

        using var document = ParseJson(response.Body, out var error);
        if (document is null)
        {
            return Fail(check, null, error);
        }

        var result = validator.Validate(document.RootElement, new JsonSchemaOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid)
        {
            return Pass(check, null);
        }

        var problems = result.ValidationErrors.Take(3).Select(e => $"{(string.IsNullOrEmpty(e.InstanceLocation?.ToString()) ? "$" : e.InstanceLocation)}: {e.ErrorMessage}");
        return Fail(check, null, Messages.Format("Check_SchemaFailed", string.Join("; ", problems)));
    }

    // Numbers compare by value ("200" == "200.0"); other text exactly.
    private static bool TextEquals(string actual, string expected) =>
        TryNumber(actual, out var a) && TryNumber(expected, out var b) ? a == b : string.Equals(actual, expected, StringComparison.Ordinal);

    // A string value equals the expected text as written or as a quoted JSON string; other values compare as JSON.
    private static bool JsonEquals(JsonElement actual, string expected)
    {
        var expectedJson = TryJson(expected);
        if (actual.ValueKind == JsonValueKind.String)
        {
            return string.Equals(actual.GetString(), expected, StringComparison.Ordinal)
                || (expectedJson is { ValueKind: JsonValueKind.String } s && string.Equals(actual.GetString(), s.GetString(), StringComparison.Ordinal));
        }

        return expectedJson is { } json ? JsonValues.DeepEquals(actual, json) : string.Equals(actual.GetRawText(), expected, StringComparison.Ordinal);
    }

    // Arrays contain an equal element; strings contain the text; objects contain the member name.
    private static bool JsonContains(JsonElement actual, string expected) => actual.ValueKind switch
    {
        JsonValueKind.Array => actual.EnumerateArray().Any(item => JsonEquals(item, expected)),
        JsonValueKind.String => actual.GetString()!.Contains(expected, StringComparison.Ordinal),
        JsonValueKind.Object => actual.TryGetProperty(expected, out _),
        _ => actual.GetRawText().Contains(expected, StringComparison.Ordinal),
    };

    private static CheckResult CompareNumbers(ResponseCheck check, string actual, string expected, string? shown)
    {
        if (!TryNumber(actual, out var a))
        {
            return Fail(check, shown, Messages.Format("Check_NotNumber", shown));
        }

        if (!TryNumber(expected, out var b))
        {
            return Fail(check, shown, Messages.Format("Check_NotNumber", expected));
        }

        return check.Operator == CheckOperator.GreaterThan
            ? a > b ? Pass(check, shown) : Fail(check, shown, Messages.Format("Check_ExpectedMore", expected, shown))
            : a < b ? Pass(check, shown) : Fail(check, shown, Messages.Format("Check_ExpectedLess", expected, shown));
    }

    private static CheckResult Match(ResponseCheck check, string actual, string pattern, string? shown)
    {
        try
        {
            return Regex.IsMatch(actual, pattern, RegexOptions.CultureInvariant, RegexTimeout)
                ? Pass(check, shown)
                : Fail(check, shown, Messages.Format("Check_ExpectedMatch", pattern, shown));
        }
        catch (RegexMatchTimeoutException)
        {
            return Fail(check, shown, Messages.Get("Check_RegexTimeout"));
        }
        catch (ArgumentException exception)
        {
            return Fail(check, shown, Messages.Format("Check_InvalidRegex", exception.Message));
        }
    }

    private static bool TryNumber(string text, out decimal value) =>
        decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static JsonElement? TryJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Display(IReadOnlyList<JsonElement> nodes) =>
        nodes.Count == 1 ? JsonValues.Display(nodes[0]) : JsonSerializer.Serialize(nodes);

    internal static string? Shorten(string? text) =>
        text is null || text.Length <= MaxShown ? text : string.Concat(text.AsSpan(0, MaxShown), "…");

    private static CheckResult Pass(ResponseCheck check, string? actual) => new(check, true, actual, null);

    private static CheckResult Fail(ResponseCheck check, string? actual, string? message) => new(check, false, actual, message);
}
