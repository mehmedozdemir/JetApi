using System.Text.Json;
using System.Text.RegularExpressions;
using JetApi.Core.Model;
using JetApi.Core.Variables;

namespace JetApi.Import;

/// <summary>
/// Converts Postman Collection v2.0/v2.1 and Postman environment exports into the JetApi model.
/// Pure: no file or network access. Whatever cannot be carried over is listed in the result's issues,
/// so an import is either lossless or its losses are visible.
/// </summary>
public static partial class PostmanImporter
{
    /// <summary>Tells a Postman collection from a Postman environment.</summary>
    public static ImportKind Detect(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ImportKind.Unknown;
            }

            if (root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object
                && Text(info, "schema").Contains("collection", StringComparison.OrdinalIgnoreCase))
            {
                return ImportKind.PostmanCollection;
            }

            if (root.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array
                && Text(root, "_postman_variable_scope") is "environment" or "")
            {
                return ImportKind.PostmanEnvironment;
            }

            return ImportKind.Unknown;
        }
        catch (JsonException)
        {
            return ImportKind.Unknown;
        }
    }

    /// <summary>Converts a Postman collection; its key does not clash with <paramref name="existingCollectionKeys"/>.</summary>
    public static ImportResult<CollectionImport> ImportCollection(string json, IEnumerable<string> existingCollectionKeys)
    {
        ArgumentNullException.ThrowIfNull(existingCollectionKeys);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException exception)
        {
            return ImportResult.Failure<CollectionImport>(Messages.Format("Import_InvalidJson", exception.Message));
        }

        using (document)
        {
            var root = document.RootElement;
            if (Detect(json) != ImportKind.PostmanCollection)
            {
                return ImportResult.Failure<CollectionImport>(Messages.Get("Import_NotCollection"));
            }

            var info = root.GetProperty("info");
            var schema = Text(info, "schema");
            var context = new Context();
            if (!schema.Contains("v2.", StringComparison.Ordinal))
            {
                return ImportResult.Failure<CollectionImport>(Messages.Format("Import_UnsupportedSchema", schema));
            }

            var name = Text(info, "name") is { Length: > 0 } infoName ? infoName : Messages.Get("Import_UnnamedCollection");
            var children = root.TryGetProperty("item", out var items) ? ConvertItems(items, [], context) : [];
            var collection = new ApiCollection
            {
                Key = NodeKey.CreateUnique(name, existingCollectionKeys),
                Name = name,
                Description = Description(info),
                Children = children,
                Auth = root.TryGetProperty("auth", out var auth) ? ConvertAuth(auth, string.Empty, context, AuthConfig.None) : AuthConfig.None,
                Variables = root.TryGetProperty("variable", out var variables) ? ConvertVariables(variables) : [],
            };

            context.ReportScripts(root, string.Empty);
            context.ReportDynamicVariables(json);
            return new ImportResult<CollectionImport>
            {
                Value = new CollectionImport(collection, context.Requests, context.Folders),
                Issues = context.Issues,
            };
        }
    }

    /// <summary>Converts a Postman environment; its key does not clash with <paramref name="existingEnvironmentKeys"/>.</summary>
    public static ImportResult<ApiEnvironment> ImportEnvironment(string json, IEnumerable<string> existingEnvironmentKeys)
    {
        ArgumentNullException.ThrowIfNull(existingEnvironmentKeys);
        if (Detect(json) != ImportKind.PostmanEnvironment)
        {
            return ImportResult.Failure<ApiEnvironment>(Messages.Get("Import_NotEnvironment"));
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var name = Text(root, "name") is { Length: > 0 } envName ? envName : Messages.Get("Import_UnnamedEnvironment");
        var variables = new List<Variable>();
        foreach (var value in root.GetProperty("values").EnumerateArray())
        {
            if (Text(value, "key") is { Length: > 0 } key)
            {
                variables.Add(new Variable(key, Text(value, "value"), Bool(value, "enabled", true), Text(value, "type") == "secret"));
            }
        }

        var context = new Context();
        context.ReportDynamicVariables(json);
        return new ImportResult<ApiEnvironment>
        {
            Value = new ApiEnvironment { Key = NodeKey.CreateUnique(name, existingEnvironmentKeys), Name = name, Variables = variables },
            Issues = context.Issues,
        };
    }

    private static List<CollectionNode> ConvertItems(JsonElement items, IReadOnlyList<string> parents, Context context)
    {
        var nodes = new List<CollectionNode>();
        if (items.ValueKind != JsonValueKind.Array)
        {
            return nodes;
        }

        foreach (var item in items.EnumerateArray())
        {
            var name = Text(item, "name") is { Length: > 0 } itemName ? itemName : Messages.Get("Import_Unnamed");
            var key = NodeKey.CreateUnique(name, nodes.Select(n => n.Key));
            var location = string.Join(" / ", [.. parents, name]);
            context.ReportScripts(item, location);

            if (item.TryGetProperty("item", out var children))
            {
                context.Folders++;
                nodes.Add(new Folder
                {
                    Key = key,
                    Name = name,
                    Description = Description(item),
                    Children = ConvertItems(children, [.. parents, name], context),
                    Auth = item.TryGetProperty("auth", out var auth) ? ConvertAuth(auth, location, context, AuthConfig.Inherit) : AuthConfig.Inherit,
                });
            }
            else if (item.TryGetProperty("request", out var request))
            {
                context.Requests++;
                nodes.Add(ConvertRequest(request, key, name, Description(item) ?? Description(request), location, context, item));
                if (item.TryGetProperty("response", out var examples) && examples.ValueKind == JsonValueKind.Array && examples.GetArrayLength() > 0)
                {
                    context.Add(location, Messages.Format("Import_ExamplesSkipped", examples.GetArrayLength()), ImportIssueSeverity.Info);
                }
            }
        }

        return nodes;
    }

    private static HttpRequestDefinition ConvertRequest(JsonElement request, string key, string name, string? description, string location, Context context, JsonElement item)
    {
        // A request may be just its URL.
        if (request.ValueKind == JsonValueKind.String)
        {
            var (shortUrl, shortQuery) = SplitQuery(request.GetString() ?? string.Empty);
            return new HttpRequestDefinition { Key = key, Name = name, Description = description, Url = shortUrl, QueryParameters = shortQuery };
        }

        var (url, query) = ConvertUrl(request.TryGetProperty("url", out var urlElement) ? urlElement : default, location, context);
        var headers = request.TryGetProperty("header", out var headerElement) ? Entries(headerElement) : [];
        var body = request.TryGetProperty("body", out var bodyElement) ? ConvertBody(bodyElement, headers, location, context) : HttpBody.Empty;
        var settings = HttpRequestSettings.Default;
        if (item.TryGetProperty("protocolProfileBehavior", out var behavior) && behavior.ValueKind == JsonValueKind.Object)
        {
            settings = settings with
            {
                FollowRedirects = Bool(behavior, "followRedirects", true),
                ValidateCertificates = Bool(behavior, "strictSSL", true),
            };
        }

        return new HttpRequestDefinition
        {
            Key = key,
            Name = name,
            Description = description,
            Method = Text(request, "method") is { Length: > 0 } method ? method.ToUpperInvariant() : "GET",
            Url = url,
            QueryParameters = query,
            Headers = headers,
            Body = body,
            Settings = settings,
            Auth = request.TryGetProperty("auth", out var auth) ? ConvertAuth(auth, location, context, AuthConfig.Inherit) : AuthConfig.Inherit,
        };
    }

    // JetApi keeps the query in its own list; path variables (":id") are replaced by their Postman value.
    private static (string Url, IReadOnlyList<KeyValueEntry> Query) ConvertUrl(JsonElement url, string location, Context context)
    {
        if (url.ValueKind == JsonValueKind.String)
        {
            return SplitQuery(url.GetString() ?? string.Empty);
        }

        if (url.ValueKind != JsonValueKind.Object)
        {
            return (string.Empty, []);
        }

        var (raw, rawQuery) = SplitQuery(Text(url, "raw"));
        IReadOnlyList<KeyValueEntry> query = url.TryGetProperty("query", out var queryElement) ? Entries(queryElement) : rawQuery;
        if (url.TryGetProperty("variable", out var pathVariables) && pathVariables.ValueKind == JsonValueKind.Array)
        {
            foreach (var variable in pathVariables.EnumerateArray())
            {
                var name = Text(variable, "key");
                if (name.Length == 0)
                {
                    continue;
                }

                var value = Text(variable, "value");
                var pattern = new Regex("(?<=/):" + Regex.Escape(name) + "(?=/|$|#)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (value.Length > 0)
                {
                    raw = pattern.Replace(raw, value.Replace("$", "$$", StringComparison.Ordinal));
                }
                else
                {
                    raw = pattern.Replace(raw, "{{" + name + "}}");
                    context.Add(location, Messages.Format("Import_PathVariable", name));
                }
            }
        }

        return (raw, query);
    }

    private static (string Url, IReadOnlyList<KeyValueEntry> Query) SplitQuery(string raw)
    {
        var fragmentAt = raw.IndexOf('#', StringComparison.Ordinal);
        var withoutFragment = fragmentAt >= 0 ? raw[..fragmentAt] : raw;
        var fragment = fragmentAt >= 0 ? raw[fragmentAt..] : string.Empty;
        var queryAt = withoutFragment.IndexOf('?', StringComparison.Ordinal);
        if (queryAt < 0)
        {
            return (raw, []);
        }

        var query = withoutFragment[(queryAt + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Select(parts => new KeyValueEntry(parts[0], parts.Length > 1 ? parts[1] : string.Empty))
            .ToList();
        return (withoutFragment[..queryAt] + fragment, query);
    }

    private static HttpBody ConvertBody(JsonElement body, IReadOnlyList<KeyValueEntry> headers, string location, Context context)
    {
        if (body.ValueKind != JsonValueKind.Object || Bool(body, "disabled", false))
        {
            return HttpBody.Empty;
        }

        switch (Text(body, "mode"))
        {
            case "raw":
                var content = Text(body, "raw");
                var language = body.TryGetProperty("options", out var options) && options.TryGetProperty("raw", out var rawOptions) ? Text(rawOptions, "language") : string.Empty;
                var contentType = headers.FirstOrDefault(h => h.IsEnabled && h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
                return language switch
                {
                    "json" => HttpBody.Json(content),
                    "xml" => HttpBody.Xml(content),
                    "text" or "javascript" or "html" => HttpBody.Text(content),
                    _ when contentType.Contains("json", StringComparison.OrdinalIgnoreCase) => HttpBody.Json(content),
                    _ when contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) => HttpBody.Xml(content),
                    _ => HttpBody.Text(content),
                };

            case "urlencoded":
                return HttpBody.Form(body.TryGetProperty("urlencoded", out var form) ? Entries(form) : []);

            case "formdata":
                var fields = new List<MultipartField>();
                if (body.TryGetProperty("formdata", out var parts) && parts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        var isFile = Text(part, "type") == "file";
                        var value = isFile ? FileSource(part) : Text(part, "value");
                        if (isFile)
                        {
                            context.Add(location, Messages.Format("Import_FilePath", value.Length > 0 ? value : Text(part, "key")));
                        }

                        fields.Add(new MultipartField(Text(part, "key"), value, isFile ? MultipartFieldKind.File : MultipartFieldKind.Text,
                            !Bool(part, "disabled", false), Text(part, "contentType") is { Length: > 0 } partType ? partType : null));
                    }
                }

                return new HttpBody { Mode = HttpBodyMode.Multipart, MultipartFields = fields };

            case "file":
                var source = body.TryGetProperty("file", out var file) ? FileSource(file) : string.Empty;
                context.Add(location, Messages.Format("Import_FilePath", source));
                return new HttpBody { Mode = HttpBodyMode.File, FilePath = source };

            case "graphql":
                // Postman sends GraphQL as a JSON body; JetApi does the same with a JSON body.
                var graphql = body.TryGetProperty("graphql", out var graphqlElement) ? graphqlElement : default;
                var variablesText = Text(graphql, "variables");
                var payload = new Dictionary<string, object?> { ["query"] = Text(graphql, "query") };
                if (variablesText.Trim().Length > 0)
                {
                    try
                    {
                        payload["variables"] = JsonDocument.Parse(variablesText).RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        payload["variables"] = variablesText;
                    }
                }

                context.Add(location, Messages.Get("Import_GraphQl"), ImportIssueSeverity.Info);
                return HttpBody.Json(JsonSerializer.Serialize(payload, IndentedJson));

            default:
                return HttpBody.Empty;
        }
    }

    private static AuthConfig ConvertAuth(JsonElement auth, string location, Context context, AuthConfig whenMissing)
    {
        if (auth.ValueKind != JsonValueKind.Object)
        {
            return whenMissing;
        }

        var type = Text(auth, "type");
        string Field(string name) =>
            auth.TryGetProperty(type, out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(e => Text(e, "key") == name).Select(e => Text(e, "value")).FirstOrDefault() ?? string.Empty
                // v2.0 stored auth fields as an object.
                : list.ValueKind == JsonValueKind.Object ? Text(list, name) : string.Empty;

        switch (type)
        {
            case "noauth":
                return AuthConfig.None;
            case "inherit":
                return AuthConfig.Inherit;
            case "bearer":
                return new BearerAuth(Field("token"));
            case "basic":
                return new BasicAuth(Field("username"), Field("password"));
            case "apikey":
                var where = Field("in") == "query" ? ApiKeyLocation.Query : ApiKeyLocation.Header;
                return new ApiKeyAuth(Field("key"), Field("value"), where);
            case "oauth2":
                return OAuth2(Field, location.Length == 0 ? Messages.Get("Import_CollectionLocation") : location, context) ?? AuthConfig.None;
            default:
                // Sending no credentials is safer than inheriting ones that were never meant for this request.
                context.Add(location.Length == 0 ? Messages.Get("Import_CollectionLocation") : location, Messages.Format("Import_AuthNotSupported", type));
                return AuthConfig.None;
        }
    }

    // Postman's OAuth 2.0 settings; the token itself is not carried over. The implicit grant is not supported.
    private static OAuth2Auth? OAuth2(Func<string, string> field, string location, Context context)
    {
        var grantName = field("grant_type");
        OAuth2GrantType? grant = grantName switch
        {
            "" or "authorization_code" or "authorization_code_with_pkce" => OAuth2GrantType.AuthorizationCode,
            "client_credentials" => OAuth2GrantType.ClientCredentials,
            "password_credentials" or "password" => OAuth2GrantType.Password,
            _ => null,
        };
        if (grant is not { } grantType)
        {
            context.Add(location, Messages.Format("Import_AuthNotSupported", $"oauth2 ({grantName})"));
            return null;
        }

        var redirect = field("redirect_uri");
        if (!IsLoopback(redirect))
        {
            if (grantType == OAuth2GrantType.AuthorizationCode && redirect.Length > 0)
            {
                context.Add(location, Messages.Format("Import_OAuthRedirect", redirect, OAuth2Auth.DefaultRedirectUri));
            }

            redirect = OAuth2Auth.DefaultRedirectUri;
        }

        context.Add(location, Messages.Get("Import_OAuthToken"), ImportIssueSeverity.Info);
        return new OAuth2Auth(
            grantType,
            TokenUrl: field("accessTokenUrl"),
            ClientId: field("clientId"),
            ClientSecret: field("clientSecret"),
            Scope: field("scope"),
            Audience: field("audience"),
            AuthorizationUrl: grantType == OAuth2GrantType.AuthorizationCode ? field("authUrl") : string.Empty,
            Username: grantType == OAuth2GrantType.Password ? field("username") : string.Empty,
            Password: grantType == OAuth2GrantType.Password ? field("password") : string.Empty,
            RedirectUri: redirect,
            UsePkce: grantName == "authorization_code_with_pkce",
            ClientAuthentication: field("client_authentication") == "body" ? OAuth2ClientAuthentication.Body : OAuth2ClientAuthentication.BasicHeader);
    }

    private static bool IsLoopback(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback;

    private static List<Variable> ConvertVariables(JsonElement variables) =>
        variables.ValueKind != JsonValueKind.Array
            ? []
            : [.. variables.EnumerateArray()
                .Where(v => Text(v, "key").Length > 0)
                .Select(v => new Variable(Text(v, "key"), Text(v, "value"), !Bool(v, "disabled", false), Text(v, "type") == "secret"))];

    private static List<KeyValueEntry> Entries(JsonElement list) =>
        list.ValueKind != JsonValueKind.Array
            ? []
            : [.. list.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object && (Text(e, "key").Length > 0 || Text(e, "value").Length > 0))
                .Select(e => new KeyValueEntry(Text(e, "key"), Text(e, "value"), !Bool(e, "disabled", false), Description(e)))];

    private static string FileSource(JsonElement element) =>
        element.TryGetProperty("src", out var source)
            ? source.ValueKind == JsonValueKind.Array ? source.EnumerateArray().Select(s => s.GetString()).FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? string.Empty : Text(element, "src")
            : string.Empty;

    private static string? Description(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("description", out var description))
        {
            return null;
        }

        var text = description.ValueKind == JsonValueKind.Object ? Text(description, "content") : Text(element, "description");
        return text.Length == 0 ? null : text;
    }

    // Postman stores numbers and booleans in "value" as they were typed; JetApi keeps text.
    private static string Text(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => string.Empty,
        };
    }

    private static bool Bool(JsonElement element, string property, bool whenMissing) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : whenMissing;

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [GeneratedRegex(@"\{\{\s*(\$[A-Za-z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex DynamicVariablePattern();

    private sealed class Context
    {
        private readonly List<ImportIssue> _issues = [];

        public int Requests { get; set; }

        public int Folders { get; set; }

        public IReadOnlyList<ImportIssue> Issues => _issues;

        public void Add(string location, string message, ImportIssueSeverity severity = ImportIssueSeverity.Warning) =>
            _issues.Add(new ImportIssue(location, message, severity));

        // Pre-request and test scripts run JavaScript; JetApi gets them later (M3 assertions, M5 scripts).
        public void ReportScripts(JsonElement element, string location)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("event", out var events) || events.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var script in events.EnumerateArray())
            {
                var hasCode = script.TryGetProperty("script", out var body) && body.TryGetProperty("exec", out var exec)
                    && (exec.ValueKind == JsonValueKind.Array ? exec.EnumerateArray().Any(line => (line.GetString() ?? string.Empty).Trim().Length > 0) : Text(body, "exec").Trim().Length > 0);
                if (hasCode)
                {
                    var kind = Text(script, "listen") == "test" ? Messages.Get("Import_TestScript") : Messages.Get("Import_PreRequestScript");
                    Add(location.Length == 0 ? Messages.Get("Import_CollectionLocation") : location, Messages.Format("Import_ScriptSkipped", kind));
                }
            }
        }

        public void ReportDynamicVariables(string json)
        {
            var dynamicVariables = new DynamicVariables();
            var unsupported = DynamicVariablePattern().Matches(json)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .Where(name => !dynamicVariables.TryEvaluate(name, out _))
                .Order(StringComparer.Ordinal)
                .ToList();
            if (unsupported.Count > 0)
            {
                Add(string.Empty, Messages.Format("Import_DynamicVariables", string.Join(", ", unsupported)));
            }
        }
    }
}
