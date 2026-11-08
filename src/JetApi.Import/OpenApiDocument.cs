using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace JetApi.Import;

/// <summary>
/// Reads an OpenAPI or Swagger document, JSON or YAML, into one <see cref="JsonNode"/> tree, so the importer works on a
/// single model whatever the file format. Limits on depth and size keep hostile input (YAML alias bombs) harmless.
/// </summary>
internal static partial class OpenApiDocument
{
    private const int MaxDepth = 256;

    // YAML aliases are expanded into copies, so a small file can describe a huge tree.
    private const int MaxNodes = 2_000_000;

    /// <summary>The document tree, or <c>null</c> with a message in <paramref name="error"/>.</summary>
    public static JsonNode? Read(string text, out string? error)
    {
        error = null;
        var start = text.AsSpan().TrimStart().TrimStart('﻿');
        try
        {
            var reader = new TreeBuilder();
            var root = start.Length > 0 && start[0] is '{' or '[' ? reader.FromJson(text) : reader.FromYaml(text);
            if (reader.TooComplex)
            {
                error = Messages.Get("OpenApi_TooComplex");
                return null;
            }

            return root;
        }
        catch (JsonException exception)
        {
            error = Messages.Format("OpenApi_InvalidDocument", exception.Message);
        }
        catch (YamlException exception)
        {
            error = Messages.Format("OpenApi_InvalidDocument", exception.Message);
        }
        catch (ArgumentException exception)
        {
            // YamlDotNet reports some malformed mappings (duplicate keys) this way.
            error = Messages.Format("OpenApi_InvalidDocument", exception.Message);
        }

        return null;
    }

    [GeneratedRegex(@"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][-+]?[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex JsonNumber();

    private sealed class TreeBuilder
    {
        private int _nodes;

        public bool TooComplex { get; private set; }

        public JsonNode? FromJson(string text)
        {
            var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = MaxDepth };
            using var document = JsonDocument.Parse(text, options);
            return Convert(document.RootElement);
        }

        public JsonNode? FromYaml(string text)
        {
            var stream = new YamlStream();
            using (var reader = new StringReader(text))
            {
                stream.Load(reader);
            }

            return stream.Documents.Count == 0 ? null : Convert(stream.Documents[0].RootNode, 0);
        }

        private bool Count(int depth)
        {
            if (++_nodes > MaxNodes || depth > MaxDepth)
            {
                TooComplex = true;
            }

            return !TooComplex;
        }

        private JsonNode? Convert(JsonElement element)
        {
            if (!Count(0))
            {
                return null;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var obj = new JsonObject();
                    foreach (var property in element.EnumerateObject())
                    {
                        // The last of duplicate keys wins, as in most JSON parsers.
                        obj[property.Name] = Convert(property.Value);
                    }

                    return obj;
                case JsonValueKind.Array:
                    var array = new JsonArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        array.Add(Convert(item));
                    }

                    return array;
                case JsonValueKind.String:
                    return JsonValue.Create(element.GetString());
                case JsonValueKind.Number:
                    return JsonValue.Create(element.Clone());
                case JsonValueKind.True:
                    return JsonValue.Create(true);
                case JsonValueKind.False:
                    return JsonValue.Create(false);
                default:
                    return null;
            }
        }

        private JsonNode? Convert(YamlNode node, int depth)
        {
            if (!Count(depth))
            {
                return null;
            }

            switch (node)
            {
                case YamlMappingNode mapping:
                    var obj = new JsonObject();
                    foreach (var (keyNode, valueNode) in mapping.Children)
                    {
                        if (keyNode is not YamlScalarNode { Value: { } key })
                        {
                            continue;
                        }

                        if (key == "<<" && keyNode is YamlScalarNode { Style: ScalarStyle.Plain })
                        {
                            Merge(obj, Convert(valueNode, depth + 1));
                            continue;
                        }

                        obj[key] = Convert(valueNode, depth + 1);
                    }

                    return obj;
                case YamlSequenceNode sequence:
                    var array = new JsonArray();
                    foreach (var item in sequence.Children)
                    {
                        array.Add(Convert(item, depth + 1));
                    }

                    return array;
                case YamlScalarNode scalar:
                    return Scalar(scalar);
                default:
                    return null;
            }
        }

        // YAML merge keys ("<<: *defaults") add the keys the mapping does not set itself.
        private static void Merge(JsonObject target, JsonNode? source)
        {
            var sources = source is JsonArray list ? list.OfType<JsonObject>() : source is JsonObject single ? [single] : [];
            foreach (var item in sources)
            {
                foreach (var (key, value) in item)
                {
                    if (!target.ContainsKey(key))
                    {
                        target[key] = value?.DeepClone();
                    }
                }
            }
        }

        // Plain scalars follow the YAML 1.2 core schema; quoted ones are always strings. Numbers keep their text,
        // so "version: 1.0" stays "1.0".
        private static JsonNode? Scalar(YamlScalarNode scalar)
        {
            var value = scalar.Value ?? string.Empty;
            var tag = scalar.Tag.IsEmpty ? string.Empty : scalar.Tag.Value;
            if (scalar.Style != ScalarStyle.Plain || tag is "!" or "tag:yaml.org,2002:str")
            {
                return JsonValue.Create(value);
            }

            return value switch
            {
                "" or "~" or "null" or "Null" or "NULL" => null,
                "true" or "True" or "TRUE" => JsonValue.Create(true),
                "false" or "False" or "FALSE" => JsonValue.Create(false),
                _ when JsonNumber().IsMatch(value) => JsonNode.Parse(value),
                _ => JsonValue.Create(value),
            };
        }
    }
}
