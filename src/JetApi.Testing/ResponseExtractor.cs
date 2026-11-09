using System.Globalization;
using JetApi.Core.Model;
using JetApi.Testing.JsonPath;

namespace JetApi.Testing;

/// <summary>The outcome of one extraction.</summary>
/// <param name="Extraction">The extraction, as written.</param>
/// <param name="Value">The value to store; <c>null</c> when nothing was found.</param>
/// <param name="Error">Why nothing was found; <c>null</c> on success.</param>
public sealed record ExtractionResult(Extraction Extraction, string? Value, string? Error)
{
    /// <summary>A value was found.</summary>
    public bool IsSuccess => Error is null;
}

/// <summary>Takes values from a response for variables (ADR-0005). Storing them is up to the caller.</summary>
public static class ResponseExtractor
{
    /// <summary>Runs the enabled extractions.</summary>
    /// <param name="response">The response.</param>
    /// <param name="extractions">The extractions of the request.</param>
    /// <param name="resolve">Fills <c>{{variables}}</c> in paths and header names; none when <c>null</c>.</param>
    public static IReadOnlyList<ExtractionResult> Run(ResponseSnapshot response, IEnumerable<Extraction> extractions, Func<string, string>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(extractions);
        return [.. extractions.Where(e => e.IsEnabled).Select(e => Extract(response, e, resolve))];
    }

    /// <summary>Runs one extraction. JSON strings are taken without quotes; other JSON values as JSON text.</summary>
    public static ExtractionResult Extract(ResponseSnapshot response, Extraction extraction, Func<string, string>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(extraction);
        if (response.StatusCode is null)
        {
            return new ExtractionResult(extraction, null, Messages.Get("Check_NoResponse"));
        }

        var target = (resolve ?? (static s => s))(extraction.Target);
        switch (extraction.Source)
        {
            case ExtractionSource.Status:
                return new ExtractionResult(extraction, response.StatusCode.Value.ToString(CultureInfo.InvariantCulture), null);
            case ExtractionSource.Body:
                return new ExtractionResult(extraction, response.Body ?? string.Empty, null);
            case ExtractionSource.Header:
                return ResponseChecker.HeaderValue(response, target) is { } header
                    ? new ExtractionResult(extraction, header, null)
                    : new ExtractionResult(extraction, null, Messages.Format("Check_NoHeader", target));
        }

        var query = JsonPathQuery.Parse(target);
        if (query.Query is null)
        {
            return new ExtractionResult(extraction, null, query.Error);
        }

        using var document = ResponseChecker.ParseJson(response.Body ?? string.Empty, out var error);
        if (document is null)
        {
            return new ExtractionResult(extraction, null, error);
        }

        var nodes = query.Query.Select(document.RootElement);
        return nodes.Count == 0
            ? new ExtractionResult(extraction, null, Messages.Format("Check_NoJsonValue", target))
            : new ExtractionResult(extraction, JsonValues.Display(nodes[0]), null);
    }
}
