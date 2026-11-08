using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using JetApi.Core.Model;

namespace JetApi.Import;

public static partial class OpenApiImporter
{
    private const int MaxSampleDepth = 12;

    private enum BodyKind
    {
        Json,
        Form,
        Multipart,
        Xml,
        Text,
        File,
    }

    private static BodyKind KindOf(string mediaType)
    {
        var type = mediaType.Split(';')[0].Trim();
        if (type.Contains("json", StringComparison.OrdinalIgnoreCase) || type == "*/*")
        {
            return BodyKind.Json;
        }

        if (type.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return BodyKind.Form;
        }

        if (type.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            return BodyKind.Multipart;
        }

        if (type.EndsWith("/xml", StringComparison.OrdinalIgnoreCase) || type.EndsWith("+xml", StringComparison.OrdinalIgnoreCase))
        {
            return BodyKind.Xml;
        }

        return type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ? BodyKind.Text : BodyKind.File;
    }

    // Lower is preferred: JSON, then forms, XML, plain text, other text, anything.
    private static int Rank(string mediaType)
    {
        var type = mediaType.Split(';')[0].Trim();
        if (type.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (type == "*/*")
        {
            return 7;
        }

        if (type.Equals("text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        return KindOf(mediaType) switch
        {
            BodyKind.Json => 1,
            BodyKind.Form => 2,
            BodyKind.Multipart => 3,
            BodyKind.Xml => 4,
            BodyKind.Text => 6,
            _ => 8,
        };
    }

    // The Content-Type JetApi sends for each body mode; anything else needs an explicit header.
    private static string? ExtraContentType(string mediaType, BodyKind kind)
    {
        var implied = kind switch
        {
            BodyKind.Json => "application/json",
            BodyKind.Xml => "application/xml",
            BodyKind.Text => "text/plain",
            BodyKind.File => "application/octet-stream",
            _ => null,
        };
        return implied is null || mediaType.Equals(implied, StringComparison.OrdinalIgnoreCase) || mediaType == "*/*" ? null : mediaType;
    }

    private static string RefName(JsonNode? node) =>
        node is JsonObject obj && Text(obj["$ref"]) is { Length: > 0 } reference
            ? reference[(reference.LastIndexOf('/') + 1)..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)
            : string.Empty;

    private static XElement ToXml(string name, JsonNode? node)
    {
        var element = new XElement(XmlConvert.EncodeLocalName(name.Length > 0 ? name : "item"));
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    // Arrays repeat the element, the usual unwrapped XML form.
                    foreach (var item in value is JsonArray items ? items.ToList() : [value])
                    {
                        element.Add(ToXml(key, item));
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    element.Add(ToXml("item", item));
                }

                break;
            default:
                element.Value = Text(node);
                break;
        }

        return element;
    }

    private sealed record BodyResult(HttpBody Body, string? ContentType)
    {
        public static BodyResult None { get; } = new(HttpBody.Empty, null);
    }

    private sealed partial class Converter
    {
        // Schemas being sampled right now; meeting one again means the schema contains itself.
        private readonly HashSet<JsonNode> _sampling = new(ReferenceEqualityComparer.Instance);

        private BodyResult RequestBody(JsonNode? node, string location)
        {
            if (Deref(node, location) is not JsonObject requestBody || requestBody["content"] is not JsonObject content || content.Count == 0)
            {
                return BodyResult.None;
            }

            var mediaType = Choose(content.Select(c => c.Key).ToList(), location);
            var media = content[mediaType] as JsonObject;
            var hasExample = false;
            JsonNode? example = null;
            if (media is not null && media.TryGetPropertyValue("example", out example))
            {
                hasExample = true;
            }
            else if (FirstExample(media?["examples"], location) is { } first)
            {
                (example, hasExample) = (first, true);
            }

            return Body(mediaType, media?["schema"], hasExample, example, media?["encoding"] as JsonObject, location);
        }

        private string Choose(List<string> mediaTypes, string location)
        {
            var chosen = mediaTypes.OrderBy(Rank).First();
            if (mediaTypes.Count > 1)
            {
                Add(location, Messages.Format("OpenApi_AlsoAccepts", chosen, string.Join(", ", mediaTypes.Where(m => m != chosen))), ImportIssueSeverity.Info);
            }

            return chosen;
        }

        // Swagger 2.0: one "body" parameter, or "formData" parameters sent as a form or multipart.
        private BodyResult SwaggerBody(List<JsonObject> parameters, JsonObject operation, string location)
        {
            var consumes = ((operation["consumes"] ?? root["consumes"]) as JsonArray ?? []).Select(Text).Where(c => c.Length > 0).ToList();
            if (parameters.FirstOrDefault(p => Text(p["in"]) == "body") is { } bodyParameter)
            {
                var mediaType = Choose(consumes.Count > 0 ? consumes : ["application/json"], location);
                var hasExample = bodyParameter.TryGetPropertyValue("x-example", out var example);
                return Body(mediaType, bodyParameter["schema"], hasExample, example, null, location);
            }

            var formParameters = parameters.Where(p => Text(p["in"]) == "formData").ToList();
            if (formParameters.Count == 0)
            {
                return BodyResult.None;
            }

            var formType = consumes.FirstOrDefault(c => KindOf(c) is BodyKind.Form or BodyKind.Multipart);
            var multipart = formParameters.Any(p => Text(p["type"]) == "file") || (formType is not null && KindOf(formType) == BodyKind.Multipart);
            if (!multipart)
            {
                return new BodyResult(HttpBody.Form([.. formParameters.Select(p =>
                    new KeyValueEntry(Text(p["name"]), ValueText(ParameterExample(p, location)), IsTrue(p["required"]), Description(p["description"])))]), null);
            }

            var fields = new List<MultipartField>();
            foreach (var parameter in formParameters)
            {
                var name = Text(parameter["name"]);
                if (Text(parameter["type"]) == "file")
                {
                    Add(location, Messages.Format("OpenApi_FileField", name));
                    fields.Add(new MultipartField(name, string.Empty, MultipartFieldKind.File, IsTrue(parameter["required"])));
                }
                else
                {
                    fields.Add(new MultipartField(name, ValueText(ParameterExample(parameter, location)), MultipartFieldKind.Text, IsTrue(parameter["required"])));
                }
            }

            return new BodyResult(new HttpBody { Mode = HttpBodyMode.Multipart, MultipartFields = fields }, null);
        }

        private BodyResult Body(string mediaType, JsonNode? schema, bool hasExample, JsonNode? example, JsonObject? encoding, string location)
        {
            var kind = KindOf(mediaType);
            var contentType = ExtraContentType(mediaType, kind);
            switch (kind)
            {
                case BodyKind.Json:
                    var value = hasExample ? example : Sample(schema, 0, location);
                    return new BodyResult(HttpBody.Json(hasExample || value is not null ? JsonText(value) : string.Empty), contentType);

                case BodyKind.Form:
                    var entries = Fields(schema, hasExample, example, location)
                        .Select(f => new KeyValueEntry(f.Name, ValueText(f.Value), f.Required, Description(f.Schema?["description"])))
                        .ToList();
                    return new BodyResult(HttpBody.Form(entries), null);

                case BodyKind.Multipart:
                    return new BodyResult(new HttpBody { Mode = HttpBodyMode.Multipart, MultipartFields = MultipartFields(schema, hasExample, example, encoding, location) }, null);

                case BodyKind.Xml:
                    var sample = hasExample ? example : Sample(schema, 0, location);
                    string xml;
                    if (sample is null || sample is JsonValue)
                    {
                        xml = Text(sample);
                    }
                    else
                    {
                        var rootName = Text((Deref(schema, location) as JsonObject)?["xml"]?["name"]) is { Length: > 0 } xmlName ? xmlName : RefName(schema);
                        xml = ToXml(rootName.Length > 0 ? rootName : "root", sample).ToString();
                        Add(location, Messages.Get("OpenApi_XmlBody"), ImportIssueSeverity.Info);
                    }

                    return new BodyResult(HttpBody.Xml(xml), contentType);

                case BodyKind.Text:
                    return new BodyResult(HttpBody.Text(ValueText(hasExample ? example : Sample(schema, 0, location))), contentType);

                default:
                    // A file body needs a path; a placeholder named after the media type stands in until the user picks one.
                    var subtype = mediaType.Split(';')[0].Trim().Split('/').Last();
                    var placeholder = "body." + (subtype.Length is > 0 and <= 5 && subtype.All(char.IsAsciiLetterOrDigit) ? subtype : "bin");
                    Add(location, Messages.Format("OpenApi_BinaryBody", mediaType, placeholder));
                    return new BodyResult(new HttpBody { Mode = HttpBodyMode.File, FilePath = placeholder }, contentType);
            }
        }

        // An example given as a JSON string is used as written; anything else is serialized.
        private static string JsonText(JsonNode? value)
        {
            var text = value is JsonValue ? Text(value).TrimStart() : string.Empty;
            if (text.StartsWith('{') || text.StartsWith('['))
            {
                try
                {
                    using var parsed = JsonDocument.Parse(text);
                    return Text(value);
                }
                catch (JsonException)
                {
                    // Not JSON after all: serialize it as a string below.
                }
            }

            return value?.ToJsonString(IndentedJson) ?? "null";
        }

        private List<MultipartField> MultipartFields(JsonNode? schema, bool hasExample, JsonNode? example, JsonObject? encoding, string location)
        {
            var fields = new List<MultipartField>();
            foreach (var field in Fields(schema, hasExample, example, location))
            {
                var partType = Text((encoding?[field.Name] as JsonObject)?["contentType"]);
                var fieldSchema = Deref(field.Schema, location) as JsonObject;
                if (IsBinary(fieldSchema, location))
                {
                    Add(location, Messages.Format("OpenApi_FileField", field.Name));
                    var fileType = partType.Length > 0 ? partType : Text(fieldSchema?["contentMediaType"]);
                    fields.Add(new MultipartField(field.Name, string.Empty, MultipartFieldKind.File, field.Required, fileType.Length > 0 ? fileType : null));
                }
                else if (field.Value is JsonObject || (field.Value is JsonArray array && array.Any(i => i is JsonObject)))
                {
                    // Object parts are sent as JSON, as OpenAPI specifies.
                    fields.Add(new MultipartField(field.Name, field.Value.ToJsonString(CompactJson), MultipartFieldKind.Text, field.Required, partType.Length > 0 ? partType : "application/json"));
                }
                else
                {
                    fields.Add(new MultipartField(field.Name, ValueText(field.Value), MultipartFieldKind.Text, field.Required, partType.Length > 0 ? partType : null));
                }
            }

            return fields;
        }

        private bool IsBinary(JsonObject? schema, string location)
        {
            if (schema is null)
            {
                return false;
            }

            if (TypeOf(schema) == "array")
            {
                return IsBinary(Deref(schema["items"], location) as JsonObject, location);
            }

            var format = Text(schema["format"]);
            var mediaType = Text(schema["contentMediaType"]);
            return format == "binary" || (TypeOf(schema) == "file")
                || (mediaType.Length > 0 && KindOf(mediaType) == BodyKind.File);
        }

        // The fields of a form body: schema properties in order (allOf merged, read-only skipped), with values from the
        // example or a generated sample. Required fields are enabled, optional ones disabled.
        private List<(string Name, JsonNode? Value, bool Required, JsonNode? Schema)> Fields(JsonNode? schema, bool hasExample, JsonNode? example, string location)
        {
            var properties = new List<(string Name, JsonNode? Schema)>();
            var required = new HashSet<string>(StringComparer.Ordinal);
            CollectProperties(schema, properties, required, location, 0);
            var values = (hasExample ? example : Sample(schema, 0, location)) as JsonObject;
            if (properties.Count == 0 && values is not null)
            {
                return [.. values.Select(v => (v.Key, v.Value, true, (JsonNode?)null))];
            }

            return [.. properties.Select(p => (p.Name, values?[p.Name], required.Contains(p.Name), p.Schema))];
        }

        private void CollectProperties(JsonNode? node, List<(string Name, JsonNode? Schema)> properties, HashSet<string> required, string location, int depth)
        {
            if (depth > MaxSampleDepth || Deref(node, location) is not JsonObject schema)
            {
                return;
            }

            foreach (var part in schema["allOf"] as JsonArray ?? [])
            {
                CollectProperties(part, properties, required, location, depth + 1);
            }

            if ((schema["oneOf"] ?? schema["anyOf"]) is JsonArray { Count: > 0 } choices)
            {
                CollectProperties(choices[0], properties, required, location, depth + 1);
            }

            foreach (var name in (schema["required"] as JsonArray ?? []).Select(Text))
            {
                required.Add(name);
            }

            foreach (var (name, property) in schema["properties"] as JsonObject ?? [])
            {
                if (!IsReadOnly(property, location) && !properties.Any(p => p.Name == name))
                {
                    properties.Add((name, property));
                }
            }
        }

        private bool IsReadOnly(JsonNode? property, string location) =>
            IsTrue((property as JsonObject)?["readOnly"]) || IsTrue((Deref(property, location) as JsonObject)?["readOnly"]);

        /// <summary>
        /// An example value for a schema: its own example, default or first enum value, otherwise built from its type.
        /// Objects get every writable property, arrays one item. Self-containing schemas stop where they repeat.
        /// </summary>
        private JsonNode? Sample(JsonNode? node, int depth, string location)
        {
            if (depth > MaxSampleDepth || Deref(node, location) is not JsonObject schema)
            {
                return null;
            }

            if (!_sampling.Add(schema))
            {
                Add(location, Messages.Format("OpenApi_RecursiveSchema", RefName(node) is { Length: > 0 } name ? name : "?"), ImportIssueSeverity.Info);
                return null;
            }

            try
            {
                return SampleOf(schema, depth, location);
            }
            finally
            {
                _sampling.Remove(schema);
            }
        }

        private JsonNode? SampleOf(JsonObject schema, int depth, string location)
        {
            if (schema.TryGetPropertyValue("example", out var example) || schema.TryGetPropertyValue("const", out example)
                || schema.TryGetPropertyValue("default", out example))
            {
                return example?.DeepClone();
            }

            if (schema["examples"] is JsonArray { Count: > 0 } examples)
            {
                return examples[0]?.DeepClone();
            }

            if (schema["enum"] is JsonArray { Count: > 0 } values)
            {
                return (values.FirstOrDefault(v => v is not null) ?? values[0])?.DeepClone();
            }

            if (schema["allOf"] is JsonArray { Count: > 0 } allOf)
            {
                JsonNode? merged = null;
                foreach (var part in allOf)
                {
                    merged = Merge(merged, Sample(part, depth + 1, location));
                }

                return schema["properties"] is JsonObject ? Merge(merged, SampleObject(schema, depth, location)) : merged ?? new JsonObject();
            }

            if ((schema["oneOf"] ?? schema["anyOf"]) is JsonArray { Count: > 0 } choices)
            {
                var choice = choices.FirstOrDefault(c => TypeOf(Deref(c, location) as JsonObject) != "null") ?? choices[0];
                var chosen = Sample(choice, depth + 1, location);
                return schema["properties"] is JsonObject ? Merge(chosen, SampleObject(schema, depth, location)) : chosen;
            }

            switch (TypeOf(schema))
            {
                case "object":
                    return SampleObject(schema, depth, location);
                case "array":
                    var item = Sample(schema["items"], depth + 1, location);
                    return item is null ? [] : new JsonArray(item);
                case "string":
                    return JsonValue.Create(StringSample(Text(schema["format"])));
                case "integer" or "number":
                    return schema["minimum"] is JsonValue minimum && minimum.GetValueKind() == JsonValueKind.Number ? minimum.DeepClone() : JsonValue.Create(0);
                case "boolean":
                    return JsonValue.Create(true);
                case "null":
                    return null;
                default:
                    return new JsonObject();
            }
        }

        private JsonObject SampleObject(JsonObject schema, int depth, string location)
        {
            var sample = new JsonObject();
            foreach (var (name, property) in schema["properties"] as JsonObject ?? [])
            {
                if (!IsReadOnly(property, location) && Sample(property, depth + 1, location) is { } value)
                {
                    sample[name] = value;
                }
            }

            return sample;
        }

        // Objects combine their properties; otherwise the first non-empty sample stays.
        private static JsonNode? Merge(JsonNode? target, JsonNode? addition)
        {
            if (target is JsonObject targetObject && addition is JsonObject additionObject)
            {
                foreach (var (key, value) in additionObject.ToList())
                {
                    additionObject.Remove(key);
                    targetObject[key] = value;
                }

                return targetObject;
            }

            return target ?? addition;
        }

        // OpenAPI 3.1 allows a list of types ("string", "null"); a missing type is inferred from the keywords used.
        private static string TypeOf(JsonObject? schema)
        {
            if (schema is null)
            {
                return string.Empty;
            }

            var type = schema["type"] is JsonArray types ? types.Select(Text).FirstOrDefault(t => t != "null") ?? "null" : Text(schema["type"]);
            if (type.Length > 0)
            {
                return type;
            }

            return schema.ContainsKey("properties") || schema.ContainsKey("additionalProperties") ? "object"
                : schema.ContainsKey("items") ? "array"
                : string.Empty;
        }

        private static string StringSample(string format) => format switch
        {
            "date-time" => "2025-01-01T12:00:00Z",
            "date" => "2025-01-01",
            "time" => "12:00:00",
            "uuid" => "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            "email" or "idn-email" => "user@example.com",
            "uri" or "url" or "uri-reference" or "iri" or "iri-reference" => "https://example.com",
            "hostname" or "idn-hostname" => "example.com",
            "ipv4" => "192.0.2.1",
            "ipv6" => "2001:db8::1",
            "byte" => "c3RyaW5n",
            "binary" => string.Empty,
            _ => "string",
        };
    }
}
