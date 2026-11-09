namespace JetApi.Core.Model;

/// <summary>What part of the response a check looks at.</summary>
public enum CheckSource
{
    /// <summary>The status code (HTTP, or the gRPC status code number).</summary>
    Status,

    /// <summary>A response header (or gRPC header/trailer) named by <see cref="ResponseCheck.Target"/>.</summary>
    Header,

    /// <summary>A value in a JSON body selected by the JSONPath in <see cref="ResponseCheck.Target"/>.</summary>
    Json,

    /// <summary>The whole body as text.</summary>
    Body,

    /// <summary>The response time in milliseconds.</summary>
    Time,

    /// <summary>The JSON body validated against the JSON Schema in <see cref="ResponseCheck.Expected"/>.</summary>
    Schema,
}

/// <summary>How a check compares the actual value with the expected one.</summary>
public enum CheckOperator
{
    /// <summary>Equal (JSON values compare by value, so <c>1</c> equals <c>1.0</c>).</summary>
    Equals,

    /// <summary>Not equal.</summary>
    NotEquals,

    /// <summary>A number greater than the expected one.</summary>
    GreaterThan,

    /// <summary>A number less than the expected one.</summary>
    LessThan,

    /// <summary>Text contains the expected text; an array contains an element equal to it.</summary>
    Contains,

    /// <summary>The opposite of <see cref="Contains"/>.</summary>
    NotContains,

    /// <summary>Matches the regular expression.</summary>
    Matches,

    /// <summary>Is present (a header, a JSON value, a non-empty body).</summary>
    Exists,

    /// <summary>Is missing.</summary>
    NotExists,
}

/// <summary>
/// A no-code assertion on a response (ADR-0005), e.g. "status equals 200" or "<c>$.items[0].id</c> exists".
/// <see cref="Target"/> and <see cref="Expected"/> may contain <c>{{variables}}</c>.
/// </summary>
/// <param name="Source">What is checked.</param>
/// <param name="Operator">How it is compared; ignored for <see cref="CheckSource.Schema"/>.</param>
/// <param name="Target">Header name or JSONPath; empty for other sources.</param>
/// <param name="Expected">Expected value, regular expression or JSON Schema; empty for exists checks.</param>
/// <param name="IsEnabled">Disabled checks are kept but not run.</param>
public sealed record ResponseCheck(CheckSource Source, CheckOperator Operator, string Target = "", string Expected = "", bool IsEnabled = true);

/// <summary>Where an extracted value comes from.</summary>
public enum ExtractionSource
{
    /// <summary>A JSON value selected by a JSONPath.</summary>
    Json,

    /// <summary>A response header.</summary>
    Header,

    /// <summary>The status code.</summary>
    Status,

    /// <summary>The whole body.</summary>
    Body,
}

/// <summary>Where an extracted value is stored.</summary>
public enum ExtractionTarget
{
    /// <summary>A runtime variable: kept while the app or a run lasts, never written to a file.</summary>
    Runtime,

    /// <summary>The active environment (saved to its file).</summary>
    Environment,

    /// <summary>The collection's variables (saved to its file).</summary>
    Collection,

    /// <summary>The workspace globals (saved to the manifest).</summary>
    Global,
}

/// <summary>
/// Takes a value from a response into a variable for later requests (ADR-0005), e.g. <c>$.access_token</c> → <c>token</c>.
/// </summary>
/// <param name="Source">Where the value comes from.</param>
/// <param name="Target">JSONPath or header name; empty for status and body. May contain <c>{{variables}}</c>.</param>
/// <param name="Variable">The variable to set.</param>
/// <param name="To">Where the variable is stored.</param>
/// <param name="IsEnabled">Disabled extractions are kept but not run.</param>
public sealed record Extraction(ExtractionSource Source, string Target, string Variable, ExtractionTarget To = ExtractionTarget.Runtime, bool IsEnabled = true);
