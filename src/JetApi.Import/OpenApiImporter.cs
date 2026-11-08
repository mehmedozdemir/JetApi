using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using JetApi.Core.Model;

namespace JetApi.Import;

/// <summary>
/// Converts OpenAPI 3.0/3.1 and Swagger 2.0 documents (JSON or YAML) into a JetApi collection: one request per
/// operation, a folder per tag, the server address in a <c>baseUrl</c> variable and example bodies generated from the
/// schemas. Pure: no file or network access. Whatever cannot be carried over is listed in the result's issues.
/// </summary>
public static partial class OpenApiImporter
{
    private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonSerializerOptions CompactJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Tells whether <paramref name="text"/> is an OpenAPI or Swagger document (any version).</summary>
    public static bool LooksLikeOpenApi(string text)
    {
        // Cheap check first, so other files are not parsed as YAML.
        if (string.IsNullOrEmpty(text)
            || (!text.Contains("openapi", StringComparison.Ordinal) && !text.Contains("swagger", StringComparison.Ordinal)))
        {
            return false;
        }

        return OpenApiDocument.Read(text, out _) is JsonObject root && (root.ContainsKey("openapi") || root.ContainsKey("swagger"));
    }

    /// <summary>Converts an OpenAPI or Swagger document; its key does not clash with <paramref name="existingCollectionKeys"/>.</summary>
    public static ImportResult<CollectionImport> Import(string text, IEnumerable<string> existingCollectionKeys)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(existingCollectionKeys);

        var node = OpenApiDocument.Read(text, out var error);
        if (error is not null)
        {
            return ImportResult.Failure<CollectionImport>(error);
        }

        if (node is not JsonObject root || (!root.ContainsKey("openapi") && !root.ContainsKey("swagger")))
        {
            return ImportResult.Failure<CollectionImport>(Messages.Get("OpenApi_NotOpenApi"));
        }

        var openApiVersion = Text(root["openapi"]);
        var swaggerVersion = Text(root["swagger"]);
        if (openApiVersion.Length > 0 ? !openApiVersion.StartsWith("3.", StringComparison.Ordinal) : swaggerVersion != "2.0")
        {
            return ImportResult.Failure<CollectionImport>(Messages.Format("OpenApi_UnsupportedVersion", openApiVersion.Length > 0 ? openApiVersion : swaggerVersion));
        }

        return new Converter(root, isSwagger: openApiVersion.Length == 0).Convert(existingCollectionKeys);
    }

    // Text of a scalar: strings as they are, numbers and booleans as written, anything else empty.
    private static string Text(JsonNode? node) => node switch
    {
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        JsonValue value when value.GetValueKind() is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToJsonString(),
        _ => string.Empty,
    };

    // Text of an example value: scalars as text, arrays comma-separated, objects as compact JSON.
    private static string ValueText(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue => Text(node),
        JsonArray array => string.Join(",", array.Select(ValueText)),
        _ => node.ToJsonString(CompactJson),
    };

    private static bool IsTrue(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.True;

    private static string? Description(JsonNode? node) => Text(node) is { Length: > 0 } text ? text : null;

    // Variable names may hold letters, digits, '_', '-', '.' and '$' (VariableResolver).
    private static string VariableName(string name)
    {
        var cleaned = InvalidVariableCharacter().Replace(name.Trim(), "_");
        return cleaned.Length == 0 ? "value" : cleaned;
    }

    [GeneratedRegex(@"[^A-Za-z0-9_\-.$]", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidVariableCharacter();

    [GeneratedRegex(@"\{([^{}/]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PathTemplate();

    /// <summary>One conversion run: the document, the issues found so far and the collection variables collected.</summary>
    private sealed partial class Converter(JsonObject root, bool isSwagger)
    {
        private readonly List<ImportIssue> _issues = [];
        private readonly HashSet<(string Location, string Message)> _reported = [];
        private readonly List<Variable> _variables = [];
        private readonly Dictionary<string, string> _apiKeyVariables = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reportedSchemes = new(StringComparer.Ordinal);
        private readonly string _collectionLocation = Messages.Get("Import_CollectionLocation");

        public ImportResult<CollectionImport> Convert(IEnumerable<string> existingCollectionKeys)
        {
            var info = root["info"] as JsonObject;
            var name = Text(info?["title"]) is { Length: > 0 } title ? title : Messages.Get("Import_UnnamedCollection");
            // baseUrl comes first; server variables without a default follow it.
            _variables.Insert(0, new Variable("baseUrl", BaseUrl()));
            var collectionAuth = root.ContainsKey("security") ? Security(root["security"], _collectionLocation) : AuthConfig.None;

            var (children, requestCount, folderCount) = Operations(collectionAuth);
            if (root["webhooks"] is JsonObject { Count: > 0 } webhooks)
            {
                Add(_collectionLocation, Messages.Format("OpenApi_Webhooks", webhooks.Count), ImportIssueSeverity.Info);
            }

            var collection = new ApiCollection
            {
                Key = NodeKey.CreateUnique(name, existingCollectionKeys),
                Name = name,
                Description = CollectionDescription(info),
                Children = children,
                Auth = collectionAuth,
                Variables = _variables,
            };

            return new ImportResult<CollectionImport>
            {
                Value = new CollectionImport(collection, requestCount, folderCount),
                Issues = _issues,
            };
        }

        private static string? CollectionDescription(JsonObject? info)
        {
            var description = Description(info?["description"]);
            var version = Text(info?["version"]);
            if (version.Length == 0)
            {
                return description;
            }

            var versionLine = Messages.Format("OpenApi_Version", version);
            return description is null ? versionLine : description + "\n\n" + versionLine;
        }

        private void Add(string location, string message, ImportIssueSeverity severity = ImportIssueSeverity.Warning)
        {
            if (_reported.Add((location, message)))
            {
                _issues.Add(new ImportIssue(location, message, severity));
            }
        }

        // The first value seen wins; an empty value is filled in by a later operation that has one.
        private void AddVariable(string name, string value, bool isSecret = false)
        {
            var index = _variables.FindIndex(v => v.Name == name);
            if (index < 0)
            {
                _variables.Add(new Variable(name, value, IsSecret: isSecret));
            }
            else if (_variables[index].Value.Length == 0 && value.Length > 0)
            {
                _variables[index] = _variables[index] with { Value = value };
            }
        }

        private string BaseUrl()
        {
            string url;
            if (isSwagger)
            {
                var host = Text(root["host"]);
                var basePath = Text(root["basePath"]).TrimEnd('/');
                var scheme = root["schemes"] is JsonArray { Count: > 0 } schemes ? Text(schemes[0]) : "https";
                url = host.Length > 0 ? $"{scheme}://{host}{basePath}" : basePath;
            }
            else
            {
                var servers = root["servers"] as JsonArray ?? [];
                var urls = servers.Select(s => ServerUrl(s as JsonObject)).Where(u => u.Length > 0).ToList();
                url = urls.Count > 0 ? urls[0] : string.Empty;
                if (urls.Count > 1)
                {
                    Add(_collectionLocation, Messages.Format("OpenApi_OtherServers", string.Join(", ", urls.Skip(1))), ImportIssueSeverity.Info);
                }
            }

            url = url.TrimEnd('/');
            if (url.Length == 0)
            {
                Add(_collectionLocation, Messages.Get("OpenApi_MissingServer"));
            }
            else if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("{{", StringComparison.Ordinal))
            {
                Add(_collectionLocation, Messages.Format("OpenApi_RelativeServer", url));
            }

            return url;
        }

        // Server variables take their default; one without a default becomes a collection variable.
        private string ServerUrl(JsonObject? server)
        {
            var url = Text(server?["url"]);
            var variables = server?["variables"] as JsonObject;
            return PathTemplate().Replace(url, match =>
            {
                var name = match.Groups[1].Value;
                var variable = variables?[name] as JsonObject;
                var value = Text(variable?["default"]);
                if (value.Length == 0 && variable?["enum"] is JsonArray { Count: > 0 } values)
                {
                    value = Text(values[0]);
                }

                if (value.Length > 0)
                {
                    return value;
                }

                AddVariable(VariableName(name), string.Empty);
                return "{{" + VariableName(name) + "}}";
            });
        }

        private (List<CollectionNode> Children, int Requests, int Folders) Operations(AuthConfig collectionAuth)
        {
            var folders = new Dictionary<string, List<CollectionNode>>(StringComparer.Ordinal);
            var rootRequests = new List<CollectionNode>();
            var requestCount = 0;

            foreach (var (path, pathItemNode) in root["paths"] as JsonObject ?? [])
            {
                if (Deref(pathItemNode, path) is not JsonObject pathItem)
                {
                    continue;
                }

                foreach (var method in Methods)
                {
                    if (pathItem[method] is not JsonObject operation)
                    {
                        continue;
                    }

                    var tag = operation["tags"] is JsonArray { Count: > 0 } tags ? Text(tags[0]) : string.Empty;
                    var name = Text(operation["summary"]) is { Length: > 0 } summary ? summary.Trim()
                        : Text(operation["operationId"]) is { Length: > 0 } operationId ? operationId
                        : $"{method.ToUpperInvariant()} {path}";
                    var location = tag.Length > 0 ? $"{tag} / {name}" : name;
                    var siblings = tag.Length > 0 ? folders.TryGetValue(tag, out var list) ? list : folders[tag] = [] : rootRequests;

                    var request = Operation(path, method, pathItem, operation, name, location, collectionAuth);
                    siblings.Add(request with { Key = NodeKey.CreateUnique(name, siblings.Select(n => n.Key)) });
                    requestCount++;
                }
            }

            var children = new List<CollectionNode>();
            foreach (var tag in TagOrder(folders.Keys))
            {
                children.Add(new Folder
                {
                    Key = NodeKey.CreateUnique(tag, children.Select(n => n.Key)),
                    Name = tag,
                    Description = TagDescription(tag),
                    Children = folders[tag],
                });
            }

            var folderCount = children.Count;
            foreach (var request in rootRequests)
            {
                children.Add(request with { Key = NodeKey.CreateUnique(request.Name, children.Select(n => n.Key)) });
            }

            return (children, requestCount, folderCount);
        }

        // Tags declared at the top level come first, in their order; the rest in order of first use.
        private IEnumerable<string> TagOrder(IEnumerable<string> used)
        {
            var usedTags = used.ToList();
            var declared = (root["tags"] as JsonArray ?? []).Select(t => Text((t as JsonObject)?["name"])).Where(usedTags.Contains).Distinct(StringComparer.Ordinal).ToList();
            return declared.Concat(usedTags.Except(declared, StringComparer.Ordinal));
        }

        private string? TagDescription(string tag) =>
            (root["tags"] as JsonArray ?? []).OfType<JsonObject>().Where(t => Text(t["name"]) == tag).Select(t => Description(t["description"])).FirstOrDefault();

        private HttpRequestDefinition Operation(string path, string method, JsonObject pathItem, JsonObject operation, string name, string location, AuthConfig collectionAuth)
        {
            var parameters = Parameters(pathItem["parameters"], operation["parameters"], location);
            var url = "{{baseUrl}}" + PathTemplate().Replace(path, match =>
            {
                var parameterName = match.Groups[1].Value;
                var parameter = parameters.FirstOrDefault(p => Text(p["in"]) == "path" && Text(p["name"]) == parameterName);
                AddVariable(VariableName(parameterName), parameter is null ? string.Empty : ValueText(ParameterExample(parameter, location)));
                return "{{" + VariableName(parameterName) + "}}";
            });

            var query = new List<KeyValueEntry>();
            var headers = new List<KeyValueEntry>();
            var cookies = new List<string>();
            var cookieRequired = false;
            foreach (var parameter in parameters)
            {
                var parameterName = Text(parameter["name"]);
                var required = IsTrue(parameter["required"]);
                var description = Description(parameter["description"]);
                switch (Text(parameter["in"]))
                {
                    case "query":
                        query.AddRange(QueryEntries(parameter, parameterName, required, description, location));
                        break;
                    case "header" when !IsIgnoredHeader(parameterName):
                        headers.Add(new KeyValueEntry(parameterName, ValueText(ParameterExample(parameter, location)), required, description));
                        break;
                    case "cookie":
                        cookies.Add(parameterName + "=" + ValueText(ParameterExample(parameter, location)));
                        cookieRequired |= required;
                        break;
                }
            }

            if (cookies.Count > 0)
            {
                headers.Add(new KeyValueEntry("Cookie", string.Join("; ", cookies), cookieRequired));
            }

            var body = isSwagger ? SwaggerBody(parameters, operation, location) : RequestBody(operation["requestBody"], location);
            if (body.ContentType is { } contentType && !headers.Any(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
            {
                headers.Add(new KeyValueEntry("Content-Type", contentType));
            }

            ReportUnsupported(pathItem, operation, location);
            var auth = AuthConfig.Inherit;
            if (operation.ContainsKey("security"))
            {
                var operationAuth = Security(operation["security"], location);
                auth = operationAuth == collectionAuth ? AuthConfig.Inherit : operationAuth;
            }

            var operationDescription = Description(operation["description"]);
            if (IsTrue(operation["deprecated"]))
            {
                operationDescription = operationDescription is null ? Messages.Get("OpenApi_Deprecated") : Messages.Get("OpenApi_Deprecated") + "\n\n" + operationDescription;
            }

            return new HttpRequestDefinition
            {
                Key = string.Empty,
                Name = name,
                Description = operationDescription,
                Method = method.ToUpperInvariant(),
                Url = url,
                QueryParameters = query,
                Headers = headers,
                Body = body.Body,
                Auth = auth,
            };
        }

        // OpenAPI ignores header parameters with these names; the body and auth settings send them.
        private static bool IsIgnoredHeader(string name) =>
            name.Equals("Accept", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Authorization", StringComparison.OrdinalIgnoreCase);

        private void ReportUnsupported(JsonObject pathItem, JsonObject operation, string location)
        {
            if ((operation["servers"] ?? pathItem["servers"]) is JsonArray { Count: > 0 } servers)
            {
                Add(location, Messages.Format("OpenApi_OperationServers", Text((servers[0] as JsonObject)?["url"])), ImportIssueSeverity.Info);
            }

            if (operation["callbacks"] is JsonObject { Count: > 0 })
            {
                Add(location, Messages.Get("OpenApi_Callbacks"), ImportIssueSeverity.Info);
            }
        }

        // Path-level parameters apply to every operation; an operation parameter with the same name and location wins.
        private List<JsonObject> Parameters(JsonNode? pathLevel, JsonNode? operationLevel, string location)
        {
            var merged = new List<JsonObject>();
            foreach (var list in new[] { pathLevel, operationLevel })
            {
                foreach (var item in list as JsonArray ?? [])
                {
                    if (Deref(item, location) is not JsonObject parameter)
                    {
                        continue;
                    }

                    merged.RemoveAll(p => Text(p["name"]) == Text(parameter["name"]) && Text(p["in"]) == Text(parameter["in"]));
                    merged.Add(parameter);
                }
            }

            return merged;
        }

        // Optional query parameters are kept but disabled, so the first send uses only what the API requires.
        private List<KeyValueEntry> QueryEntries(JsonObject parameter, string name, bool required, string? description, string location)
        {
            var value = ParameterExample(parameter, location);
            if (value is not JsonArray array || array.Count == 0)
            {
                return [new KeyValueEntry(name, ValueText(value), required, description)];
            }

            string? separator;
            if (isSwagger)
            {
                separator = (Text(parameter["collectionFormat"]) is { Length: > 0 } format ? format : "csv") switch
                {
                    "multi" => null,
                    "ssv" => " ",
                    "tsv" => "\t",
                    "pipes" => "|",
                    _ => ",",
                };
            }
            else
            {
                var style = Text(parameter["style"]) is { Length: > 0 } s ? s : "form";
                var explode = parameter["explode"] is JsonValue ? IsTrue(parameter["explode"]) : style == "form";
                separator = explode && style == "form" ? null : style switch
                {
                    "spaceDelimited" => " ",
                    "pipeDelimited" => "|",
                    _ => ",",
                };
            }

            return separator is null
                ? array.Select(item => new KeyValueEntry(name, ValueText(item), required, description)).ToList()
                : [new KeyValueEntry(name, string.Join(separator, array.Select(ValueText)), required, description)];
        }

        // An example, then a schema example, default or first enum value; nothing is invented for parameters.
        private JsonNode? ParameterExample(JsonObject parameter, string location)
        {
            if (parameter.TryGetPropertyValue("example", out var example))
            {
                return example;
            }

            if (FirstExample(parameter["examples"], location) is { } fromExamples)
            {
                return fromExamples;
            }

            if (parameter.TryGetPropertyValue("x-example", out var swaggerExample))
            {
                return swaggerExample;
            }

            var schemaNode = parameter["schema"];
            if (parameter["content"] is JsonObject content && content.Select(c => c.Value).FirstOrDefault() is JsonObject media)
            {
                if (media.TryGetPropertyValue("example", out var mediaExample))
                {
                    return mediaExample;
                }

                schemaNode = media["schema"];
            }

            // Swagger 2.0 puts the schema keywords on the parameter itself.
            var schema = Deref(schemaNode, location) as JsonObject ?? (isSwagger ? parameter : null);
            if (schema is null)
            {
                return null;
            }

            if (schema.TryGetPropertyValue("example", out var schemaExample))
            {
                return schemaExample;
            }

            if (schema["examples"] is JsonArray { Count: > 0 } schemaExamples)
            {
                return schemaExamples[0];
            }

            if (schema.TryGetPropertyValue("default", out var defaultValue))
            {
                return defaultValue;
            }

            return schema["enum"] is JsonArray { Count: > 0 } values ? values[0] : null;
        }

        // The value of the first entry of an OpenAPI "examples" map.
        private JsonNode? FirstExample(JsonNode? examples, string location)
        {
            foreach (var (exampleName, exampleNode) in examples as JsonObject ?? [])
            {
                if (Deref(exampleNode, location) is not JsonObject entry)
                {
                    continue;
                }

                if (entry.TryGetPropertyValue("value", out var value))
                {
                    return value;
                }

                if (Text(entry["externalValue"]) is { Length: > 0 } external)
                {
                    Add(location, Messages.Format("OpenApi_ExternalValue", exampleName, external), ImportIssueSeverity.Info);
                }
            }

            return null;
        }

        /// <summary>Follows <c>$ref</c> chains inside the document; external, missing and circular references are reported.</summary>
        private JsonNode? Deref(JsonNode? node, string location)
        {
            HashSet<string>? seen = null;
            while (node is JsonObject obj && Text(obj["$ref"]) is { Length: > 0 } reference)
            {
                seen ??= new(StringComparer.Ordinal);
                if (!seen.Add(reference))
                {
                    Add(location, Messages.Format("OpenApi_RefCycle", reference));
                    return null;
                }

                node = Lookup(reference, location);
            }

            return node;
        }

        private JsonNode? Lookup(string reference, string location)
        {
            if (!reference.StartsWith('#'))
            {
                Add(location, Messages.Format("OpenApi_ExternalRef", reference));
                return null;
            }

            JsonNode? node = root;
            var pointer = Uri.UnescapeDataString(reference[1..]);
            foreach (var segment in pointer.Split('/').Skip(1))
            {
                var key = segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                node = node switch
                {
                    JsonObject obj => obj.TryGetPropertyValue(key, out var child) ? child : null,
                    JsonArray array when int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) && index < array.Count => array[index],
                    _ => null,
                };
                if (node is null)
                {
                    break;
                }
            }

            if (node is null || (pointer.Length > 0 && !pointer.StartsWith('/')))
            {
                Add(location, Messages.Format("OpenApi_MissingRef", reference));
                return null;
            }

            return node;
        }
    }
}
