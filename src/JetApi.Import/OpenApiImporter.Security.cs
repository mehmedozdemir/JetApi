using System.Text.Json.Nodes;
using JetApi.Core.Model;

namespace JetApi.Import;

public static partial class OpenApiImporter
{
    private sealed partial class Converter
    {
        /// <summary>
        /// Maps a security requirement list to one auth setting. The requirements are alternatives: the first one JetApi
        /// can send wins. An empty list or an empty requirement means no auth. Credentials become secret variables.
        /// </summary>
        private AuthConfig Security(JsonNode? requirements, string location)
        {
            if (requirements is not JsonArray alternatives || alternatives.Count == 0)
            {
                return AuthConfig.None;
            }

            foreach (var requirement in alternatives.OfType<JsonObject>())
            {
                if (requirement.Count == 0)
                {
                    return AuthConfig.None;
                }

                var names = requirement.Select(r => r.Key).ToList();
                var applied = names.FirstOrDefault(n => SchemeAuth(n, location, report: false) is not null);
                if (applied is null)
                {
                    continue;
                }

                if (names.Count > 1)
                {
                    Add(location, Messages.Format("OpenApi_CombinedSecurity", string.Join(", ", names), applied));
                }

                return SchemeAuth(applied, location, report: true)!;
            }

            // Nothing usable: say why for each scheme of the first requirement.
            foreach (var name in alternatives.OfType<JsonObject>().FirstOrDefault()?.Select(r => r.Key) ?? [])
            {
                SchemeAuth(name, location, report: true);
            }

            return AuthConfig.None;
        }

        // The auth for a named scheme, or null when JetApi cannot send it. With report, unusable schemes are explained
        // once and the variables of usable ones are added.
        private AuthConfig? SchemeAuth(string name, string location, bool report)
        {
            var schemes = root[isSwagger ? "securityDefinitions" : "components"] is JsonObject container
                ? isSwagger ? container : container["securitySchemes"] as JsonObject
                : null;
            if (Deref(schemes?[name], location) is not JsonObject scheme)
            {
                Explain(name, location, report, Messages.Format("OpenApi_UnknownSecurity", name));
                return null;
            }

            var type = Text(scheme["type"]);
            var httpScheme = Text(scheme["scheme"]);
            if (type == "http" && httpScheme.Equals("bearer", StringComparison.OrdinalIgnoreCase))
            {
                if (report)
                {
                    AddVariable("token", string.Empty, isSecret: true);
                }

                return new BearerAuth("{{token}}");
            }

            if ((type == "http" && httpScheme.Equals("basic", StringComparison.OrdinalIgnoreCase)) || (isSwagger && type == "basic"))
            {
                if (report)
                {
                    AddVariable("username", string.Empty);
                    AddVariable("password", string.Empty, isSecret: true);
                }

                return new BasicAuth("{{username}}", "{{password}}");
            }

            if (type == "apiKey")
            {
                var keyName = Text(scheme["name"]);
                var variable = ApiKeyVariable(name);
                switch (Text(scheme["in"]))
                {
                    case "header" or "query" when keyName.Length > 0:
                        if (report)
                        {
                            AddVariable(variable, string.Empty, isSecret: true);
                        }

                        return new ApiKeyAuth(keyName, "{{" + variable + "}}", Text(scheme["in"]) == "query" ? ApiKeyLocation.Query : ApiKeyLocation.Header);
                    case "cookie" when keyName.Length > 0:
                        if (report && _reportedSchemes.Add(name))
                        {
                            AddVariable(variable, string.Empty, isSecret: true);
                            Add(location, Messages.Format("OpenApi_CookieApiKey", keyName, variable));
                        }

                        return null;
                }
            }

            var description = type == "http" && httpScheme.Length > 0 ? $"{type} {httpScheme}" : type;
            Explain(name, location, report, Messages.Format("OpenApi_SecurityNotSupported", name, description));
            return null;
        }

        private void Explain(string scheme, string location, bool report, string message)
        {
            if (report && _reportedSchemes.Add(scheme))
            {
                Add(location, message);
            }
        }

        // The first API key scheme uses {{apiKey}}; others are named after their scheme, so each keeps its own value.
        private string ApiKeyVariable(string scheme)
        {
            if (!_apiKeyVariables.TryGetValue(scheme, out var variable))
            {
                variable = _apiKeyVariables.Count == 0 ? "apiKey" : VariableName(scheme);
                _apiKeyVariables[scheme] = variable;
            }

            return variable;
        }
    }
}
