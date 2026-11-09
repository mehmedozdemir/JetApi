using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JetApi.Testing.JsonPath;

/// <summary>A value found at a position in a JSON text.</summary>
/// <param name="Path">Its JSONPath, e.g. <c>$.items[0].email</c>.</param>
/// <param name="Value">The value as a user sees it (strings without quotes); <c>null</c> for an object or array.</param>
/// <param name="Name">The member name or array index it is under, for naming a variable (e.g. <c>email</c>); "value" at the root.</param>
public sealed record JsonPathHit(string Path, string? Value, string Name)
{
    /// <summary>An object or array rather than a single value.</summary>
    public bool IsContainer => Value is null;
}

/// <summary>Finds the JSONPath of the value under a caret in a JSON text (for "add a check for this field").</summary>
public static partial class JsonPathLocator
{
    /// <summary>
    /// The value at <paramref name="offset"/> (a character index): the scalar or member name under it, else the innermost
    /// object or array around it. <c>null</c> when the offset is outside the JSON or the text is not JSON up to there.
    /// </summary>
    public static JsonPathHit? Find(string? json, int offset)
    {
        if (string.IsNullOrEmpty(json) || offset < 0 || offset > json.Length)
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        var target = Encoding.UTF8.GetByteCount(json.AsSpan(0, offset));
        var frames = new List<Frame>();
        JsonPathHit? container = null;
        var onName = false;
        string? namedContainer = null;
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        try
        {
            while (reader.Read())
            {
                var start = (int)reader.TokenStartIndex;
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        frames[^1].Name = reader.GetString();
                        onName = Contains(start, reader.ValueSpan.Length + 2, target);
                        break;
                    case JsonTokenType.StartObject or JsonTokenType.StartArray:
                        var (path, name) = NextValue(frames);
                        frames.Add(new Frame(reader.TokenType == JsonTokenType.StartArray, path, name, start));
                        if (onName)
                        {
                            namedContainer = path;
                            onName = false;
                        }

                        break;
                    case JsonTokenType.EndObject or JsonTokenType.EndArray:
                        var frame = frames[^1];
                        frames.RemoveAt(frames.Count - 1);
                        var hit = new JsonPathHit(frame.Path, null, frame.ValueName);
                        if (namedContainer == frame.Path)
                        {
                            return hit;
                        }

                        if (container is null && target >= frame.Start && target <= start + 1)
                        {
                            container = hit;
                        }

                        AfterValue(frames);
                        break;
                    default:
                        var (valuePath, valueName) = NextValue(frames);
                        var length = reader.TokenType == JsonTokenType.String ? reader.ValueSpan.Length + 2 : reader.ValueSpan.Length;
                        if (onName || Contains(start, length, target))
                        {
                            return new JsonPathHit(valuePath, reader.TokenType == JsonTokenType.String ? reader.GetString() : Encoding.UTF8.GetString(reader.ValueSpan), valueName);
                        }

                        AfterValue(frames);
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // Text after the caret that is not JSON does not matter; before it, there is no answer.
        }

        return container;
    }

    private static bool Contains(int start, int length, int target) => target >= start && target <= start + length;

    private static (string Path, string Name) NextValue(List<Frame> frames)
    {
        if (frames.Count == 0)
        {
            return ("$", "value");
        }

        var parent = frames[^1];
        return parent.IsArray
            ? ($"{parent.Path}[{parent.Index}]", parent.ValueName)
            : (parent.Path + Segment(parent.Name ?? string.Empty), parent.Name ?? "value");
    }

    private static void AfterValue(List<Frame> frames)
    {
        if (frames.Count == 0)
        {
            return;
        }

        if (frames[^1].IsArray)
        {
            frames[^1].Index++;
        }
        else
        {
            frames[^1].Name = null;
        }
    }

    // ".name" when it is a plain name, otherwise "['na me']".
    private static string Segment(string name) =>
        PlainName().IsMatch(name) ? "." + name : "['" + name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "']";

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PlainName();

    private sealed class Frame(bool isArray, string path, string valueName, int start)
    {
        public bool IsArray { get; } = isArray;

        public string Path { get; } = path;

        // What the container itself is called (its member name), for naming a variable.
        public string ValueName { get; } = valueName;

        public int Start { get; } = start;

        public int Index { get; set; }

        public string? Name { get; set; }
    }
}
