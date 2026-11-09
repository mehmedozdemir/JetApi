using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JetApi.Testing.JsonPath;

/// <summary>A child (<c>.a</c>, <c>[...]</c>) or descendant (<c>..a</c>) segment.</summary>
internal sealed record Segment(bool IsDescendant, IReadOnlyList<Selector> Selectors);

/// <summary>Selects children of a node into an output list.</summary>
internal abstract class Selector
{
    public abstract void Select(JsonElement node, JsonElement root, List<JsonElement> output);
}

/// <summary><c>.name</c> or <c>['name']</c>.</summary>
internal sealed class NameSelector(string name) : Selector
{
    public override void Select(JsonElement node, JsonElement root, List<JsonElement> output)
    {
        if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value))
        {
            output.Add(value);
        }
    }
}

/// <summary><c>*</c>: every member value or array element.</summary>
internal sealed class WildcardSelector : Selector
{
    public override void Select(JsonElement node, JsonElement root, List<JsonElement> output)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            output.AddRange(node.EnumerateObject().Select(p => p.Value));
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            output.AddRange(node.EnumerateArray());
        }
    }
}

/// <summary><c>[i]</c>; negative indexes count from the end.</summary>
internal sealed class IndexSelector(long index) : Selector
{
    public override void Select(JsonElement node, JsonElement root, List<JsonElement> output)
    {
        if (node.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var length = node.GetArrayLength();
        var at = index >= 0 ? index : length + index;
        if (at >= 0 && at < length)
        {
            output.Add(node[(int)at]);
        }
    }
}

/// <summary><c>[start:end:step]</c> (RFC 9535 section 2.3.4.2.2).</summary>
internal sealed class SliceSelector(long? start, long? end, long? step) : Selector
{
    public override void Select(JsonElement node, JsonElement root, List<JsonElement> output)
    {
        if (node.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var length = (long)node.GetArrayLength();
        var by = step ?? 1;
        if (by == 0)
        {
            return;
        }

        if (by > 0)
        {
            var lower = Math.Clamp(Normalize(start ?? 0, length), 0, length);
            var upper = Math.Clamp(Normalize(end ?? length, length), 0, length);
            for (var i = lower; i < upper; i += by)
            {
                output.Add(node[(int)i]);
            }
        }
        else
        {
            var upper = Math.Clamp(Normalize(start ?? length - 1, length), -1, length - 1);
            var lower = Math.Clamp(Normalize(end ?? -length - 1, length), -1, length - 1);
            for (var i = upper; lower < i; i += by)
            {
                output.Add(node[(int)i]);
            }
        }

        static long Normalize(long i, long length) => i >= 0 ? i : length + i;
    }
}

/// <summary><c>[?expression]</c>: the children for which the expression is true.</summary>
internal sealed class FilterSelector(FilterExpression expression) : Selector
{
    public override void Select(JsonElement node, JsonElement root, List<JsonElement> output)
    {
        IEnumerable<JsonElement> children = node.ValueKind switch
        {
            JsonValueKind.Object => node.EnumerateObject().Select(p => p.Value),
            JsonValueKind.Array => node.EnumerateArray(),
            _ => [],
        };
        output.AddRange(children.Where(child => expression.IsTrue(child, root)));
    }
}

/// <summary>A logical filter expression.</summary>
internal abstract class FilterExpression
{
    public abstract bool IsTrue(JsonElement current, JsonElement root);
}

internal sealed class OrExpression(IReadOnlyList<FilterExpression> parts) : FilterExpression
{
    public override bool IsTrue(JsonElement current, JsonElement root) => parts.Any(p => p.IsTrue(current, root));
}

internal sealed class AndExpression(IReadOnlyList<FilterExpression> parts) : FilterExpression
{
    public override bool IsTrue(JsonElement current, JsonElement root) => parts.All(p => p.IsTrue(current, root));
}

internal sealed class NotExpression(FilterExpression inner) : FilterExpression
{
    public override bool IsTrue(JsonElement current, JsonElement root) => !inner.IsTrue(current, root);
}

/// <summary>A query or a logical function used as a test: true when the query selects anything.</summary>
internal sealed class TestExpression(Operand operand) : FilterExpression
{
    public override bool IsTrue(JsonElement current, JsonElement root) => operand.Evaluate(current, root) switch
    {
        NodesResult nodes => nodes.Nodes.Count > 0,
        LogicalResult logical => logical.Value,
        _ => false,
    };
}

/// <summary><c>left op right</c> with <c>==, !=, &lt;, &lt;=, &gt;, &gt;=</c>.</summary>
internal sealed class ComparisonExpression(Operand left, string op, Operand right) : FilterExpression
{
    public override bool IsTrue(JsonElement current, JsonElement root)
    {
        var a = left.Evaluate(current, root).AsValue();
        var b = right.Evaluate(current, root).AsValue();
        return op switch
        {
            "==" => JsonValues.AreEqual(a, b),
            "!=" => !JsonValues.AreEqual(a, b),
            "<" => JsonValues.IsLess(a, b),
            "<=" => JsonValues.IsLess(a, b) || JsonValues.AreEqual(a, b),
            ">" => JsonValues.IsLess(b, a),
            ">=" => JsonValues.IsLess(b, a) || JsonValues.AreEqual(a, b),
            _ => false,
        };
    }
}

/// <summary>What an operand evaluates to: nodes (a query), a single value, or true/false.</summary>
internal abstract record OperandResult
{
    /// <summary>The value for a comparison: a single node, a value, or nothing (<c>null</c>).</summary>
    public abstract JsonElement? AsValue();
}

internal sealed record NodesResult(IReadOnlyList<JsonElement> Nodes) : OperandResult
{
    public override JsonElement? AsValue() => Nodes.Count == 1 ? Nodes[0] : null;
}

internal sealed record ValueResult(JsonElement? Value) : OperandResult
{
    public override JsonElement? AsValue() => Value;
}

internal sealed record LogicalResult(bool Value) : OperandResult
{
    public override JsonElement? AsValue() => null;
}

/// <summary>A literal, a query (<c>@...</c> or <c>$...</c>) or a function call.</summary>
internal abstract class Operand
{
    public abstract OperandResult Evaluate(JsonElement current, JsonElement root);
}

internal sealed class LiteralOperand(JsonElement value) : Operand
{
    public override OperandResult Evaluate(JsonElement current, JsonElement root) => new ValueResult(value);
}

internal sealed class QueryOperand(bool isRelative, IReadOnlyList<Segment> segments) : Operand
{
    public override OperandResult Evaluate(JsonElement current, JsonElement root) =>
        new NodesResult(JsonPathQuery.Evaluate(segments, isRelative ? current : root, root));
}

/// <summary>The RFC 9535 functions: length, count, match, search, value.</summary>
internal sealed class FunctionOperand(string name, IReadOnlyList<Operand> arguments) : Operand
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static readonly string[] Names = ["length", "count", "match", "search", "value"];

    public static int Arity(string name) => name is "match" or "search" ? 2 : 1;

    public override OperandResult Evaluate(JsonElement current, JsonElement root)
    {
        var first = arguments[0].Evaluate(current, root);
        switch (name)
        {
            case "length":
                return new ValueResult(first.AsValue() is { } value ? Length(value) : null);
            case "count":
                return new ValueResult(JsonValues.Number(first is NodesResult nodes ? nodes.Nodes.Count : 0));
            case "value":
                return new ValueResult(first.AsValue());
            default:
                var text = first.AsValue();
                var pattern = arguments[1].Evaluate(current, root).AsValue();
                if (text is not { ValueKind: JsonValueKind.String } || pattern is not { ValueKind: JsonValueKind.String })
                {
                    return new LogicalResult(false);
                }

                try
                {
                    var regex = name == "match" ? "^(?:" + pattern.Value.GetString() + ")$" : pattern.Value.GetString()!;
                    return new LogicalResult(Regex.IsMatch(text.Value.GetString()!, regex, RegexOptions.CultureInvariant, RegexTimeout));
                }
                catch (Exception exception) when (exception is ArgumentException or RegexMatchTimeoutException)
                {
                    return new LogicalResult(false);
                }
        }
    }

    private static JsonElement? Length(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => JsonValues.Number(value.GetString()!.EnumerateRunes().Count()),
        JsonValueKind.Array => JsonValues.Number(value.GetArrayLength()),
        JsonValueKind.Object => JsonValues.Number(value.EnumerateObject().Count()),
        _ => null,
    };
}

/// <summary>JSON value comparisons used by filters and checks.</summary>
internal static class JsonValues
{
    /// <summary>A number element.</summary>
    public static JsonElement Number(long value) => JsonDocument.Parse(value.ToString(CultureInfo.InvariantCulture)).RootElement.Clone();

    /// <summary>Equality by value; numbers compare numerically; "nothing" equals only "nothing".</summary>
    public static bool AreEqual(JsonElement? a, JsonElement? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return DeepEquals(a.Value, b.Value);
    }

    /// <summary>a &lt; b for two numbers or two strings; false otherwise.</summary>
    public static bool IsLess(JsonElement? a, JsonElement? b)
    {
        if (a is not { } x || b is not { } y)
        {
            return false;
        }

        if (x.ValueKind == JsonValueKind.Number && y.ValueKind == JsonValueKind.Number)
        {
            return CompareNumbers(x, y) < 0;
        }

        return x.ValueKind == JsonValueKind.String && y.ValueKind == JsonValueKind.String
            && string.CompareOrdinal(x.GetString(), y.GetString()) < 0;
    }

    /// <summary>Structural equality of two JSON values.</summary>
    public static bool DeepEquals(JsonElement x, JsonElement y)
    {
        if (x.ValueKind != y.ValueKind)
        {
            return false;
        }

        switch (x.ValueKind)
        {
            case JsonValueKind.Number:
                return CompareNumbers(x, y) == 0;
            case JsonValueKind.String:
                return string.Equals(x.GetString(), y.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Array:
                return x.GetArrayLength() == y.GetArrayLength() && x.EnumerateArray().Zip(y.EnumerateArray()).All(p => DeepEquals(p.First, p.Second));
            case JsonValueKind.Object:
                var left = x.EnumerateObject().ToList();
                var right = y.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                return left.Count == right.Count && left.All(p => right.TryGetValue(p.Name, out var other) && DeepEquals(p.Value, other));
            default:
                return true;
        }
    }

    /// <summary>Compares two number elements exactly when they fit a decimal, otherwise as doubles.</summary>
    public static int CompareNumbers(JsonElement x, JsonElement y) =>
        x.TryGetDecimal(out var a) && y.TryGetDecimal(out var b) ? a.CompareTo(b) : x.GetDouble().CompareTo(y.GetDouble());

    /// <summary>The value as a user sees it: strings without quotes, everything else as compact JSON.</summary>
    public static string Display(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : JsonSerializer.Serialize(value);

    /// <summary>Builds the JSON string element for <paramref name="text"/>.</summary>
    public static JsonElement String(string text) => JsonSerializer.SerializeToElement(text);
}
