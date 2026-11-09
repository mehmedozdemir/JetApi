using System.Text.Json;

namespace JetApi.Testing.JsonPath;

/// <summary>The result of parsing a JSONPath: the query, or an error message with its position.</summary>
/// <param name="Query">The parsed query, or <c>null</c>.</param>
/// <param name="Error">What is wrong, e.g. "Invalid JSONPath at character 5: ']' expected"; <c>null</c> when parsed.</param>
public sealed record JsonPathParseResult(JsonPathQuery? Query, string? Error)
{
    /// <summary>The path was parsed.</summary>
    public bool IsSuccess => Query is not null;
}

/// <summary>
/// A JSONPath query (RFC 9535) over <see cref="JsonElement"/>: names, wildcards, indexes, slices, unions, descendants and
/// filters with comparisons, existence tests, <c>!</c>, <c>&amp;&amp;</c>, <c>||</c> and the functions <c>length</c>,
/// <c>count</c>, <c>match</c>, <c>search</c> and <c>value</c>.
/// </summary>
public sealed class JsonPathQuery
{
    private readonly IReadOnlyList<Segment> _segments;

    internal JsonPathQuery(string text, IReadOnlyList<Segment> segments)
    {
        Text = text;
        _segments = segments;
    }

    /// <summary>The path as written.</summary>
    public string Text { get; }

    /// <summary>Parses <paramref name="path"/>; problems come back as <see cref="JsonPathParseResult.Error"/>.</summary>
    public static JsonPathParseResult Parse(string? path)
    {
        try
        {
            return new JsonPathParseResult(new JsonPathParser(path ?? string.Empty).ParseQuery(), null);
        }
        catch (JsonPathSyntaxException exception)
        {
            return new JsonPathParseResult(null, Messages.Format("Path_Error", exception.Position + 1, exception.Message));
        }
    }

    /// <summary>The nodes the query selects from <paramref name="root"/>, in document order.</summary>
    public IReadOnlyList<JsonElement> Select(JsonElement root) => Evaluate(_segments, root, root);

    internal static List<JsonElement> Evaluate(IReadOnlyList<Segment> segments, JsonElement start, JsonElement root)
    {
        var nodes = new List<JsonElement> { start };
        foreach (var segment in segments)
        {
            var next = new List<JsonElement>();
            foreach (var node in nodes)
            {
                if (segment.IsDescendant)
                {
                    foreach (var descendant in DescendantsAndSelf(node))
                    {
                        Apply(segment.Selectors, descendant, root, next);
                    }
                }
                else
                {
                    Apply(segment.Selectors, node, root, next);
                }
            }

            nodes = next;
        }

        return nodes;
    }

    private static void Apply(IReadOnlyList<Selector> selectors, JsonElement node, JsonElement root, List<JsonElement> output)
    {
        foreach (var selector in selectors)
        {
            selector.Select(node, root, output);
        }
    }

    private static IEnumerable<JsonElement> DescendantsAndSelf(JsonElement node)
    {
        yield return node;
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                foreach (var descendant in DescendantsAndSelf(property.Value))
                {
                    yield return descendant;
                }
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            {
                foreach (var descendant in DescendantsAndSelf(item))
                {
                    yield return descendant;
                }
            }
        }
    }
}
