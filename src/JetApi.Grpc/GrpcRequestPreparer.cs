using System.Text;
using JetApi.Core.Model;
using JetApi.Core.Variables;
using JetApi.Grpc.Codec;
using JetApi.Grpc.Schema;

namespace JetApi.Grpc;

/// <summary>The outcome of preparing a gRPC call.</summary>
/// <param name="Call">The prepared call, or <c>null</c> when <see cref="Errors"/> is not empty.</param>
/// <param name="Errors">Problems that prevent the call, in user-facing language.</param>
/// <param name="VariableIssues">Placeholders that could not be resolved.</param>
public sealed record GrpcPreparationResult(PreparedGrpcCall? Call, IReadOnlyList<string> Errors, IReadOnlyList<VariableIssue> VariableIssues)
{
    /// <summary>True when the call can be made.</summary>
    public bool IsSuccess => Call is not null;
}

/// <summary>Resolves variables and auth, finds the method in the schema and encodes the JSON message.</summary>
public static class GrpcRequestPreparer
{
    /// <summary>Prepares <paramref name="request"/>.</summary>
    /// <param name="request">The saved request.</param>
    /// <param name="effectiveAuth">The auth that applies, typically from <see cref="AuthResolver"/>.</param>
    /// <param name="variables">Variables in scope.</param>
    /// <param name="schema">The collection's proto schema.</param>
    public static GrpcPreparationResult Prepare(GrpcRequestDefinition request, AuthConfig effectiveAuth, VariableResolver variables, ProtoSchema schema)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(effectiveAuth);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(schema);

        var errors = new List<string>();
        var issues = new List<VariableIssue>();
        string Resolve(string template)
        {
            var result = variables.Resolve(template);
            issues.AddRange(result.Issues.Where(issue => !issues.Contains(issue)));
            return result.Value;
        }

        var url = ParseServerUrl(Resolve(request.ServerUrl), errors);
        var method = FindMethod(schema, request.Service, request.Method, errors);

        var metadata = request.Metadata
            .Where(entry => entry.IsEnabled && entry.Key.Length > 0)
            .Select(entry => new KeyValuePair<string, string>(Resolve(entry.Key).ToLowerInvariant(), Resolve(entry.Value)))
            .ToList();
        ApplyAuth(effectiveAuth, Resolve, metadata, errors);

        var message = Resolve(request.Message);
        byte[]? encoded = null;
        if (method is not null)
        {
            try
            {
                encoded = new ProtoJsonCodec(schema).Encode(method.InputType, message);
            }
            catch (ProtoJsonException exception)
            {
                errors.Add(Messages.Format("Grpc_RequestMessage", exception.Message));
            }
        }

        return errors.Count > 0 || url is null || method is null || encoded is null
            ? new GrpcPreparationResult(null, errors, issues)
            : new GrpcPreparationResult(new PreparedGrpcCall(url, method, encoded, metadata, request.Settings), [], issues);
    }

    private static Uri? ParseServerUrl(string value, List<string> errors)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            errors.Add(Messages.Get("Grpc_EmptyServerUrl"));
            return null;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps) || url.AbsolutePath != "/")
        {
            errors.Add(Messages.Format("Grpc_InvalidServerUrl", text));
            return null;
        }

        return url;
    }

    private static ProtoMethod? FindMethod(ProtoSchema schema, string serviceName, string methodName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(methodName))
        {
            errors.Add(Messages.Get("Grpc_SelectMethod"));
            return null;
        }

        var service = schema.Services.FirstOrDefault(s => s.FullName == serviceName);
        if (service is null)
        {
            errors.Add(Messages.Format("Grpc_UnknownService", serviceName));
            return null;
        }

        var method = service.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method is null)
        {
            errors.Add(Messages.Format("Grpc_UnknownMethod", methodName, serviceName));
            return null;
        }

        if (!method.IsUnary)
        {
            errors.Add(Messages.Format("Grpc_StreamingNotSupported", serviceName, methodName));
            return null;
        }

        return method;
    }

    private static void ApplyAuth(AuthConfig auth, Func<string, string> resolve, List<KeyValuePair<string, string>> metadata, List<string> errors)
    {
        var hasAuthorization = metadata.Any(entry => entry.Key == "authorization");
        switch (auth)
        {
            case BearerAuth bearer when !hasAuthorization:
                metadata.Add(new("authorization", "Bearer " + resolve(bearer.Token)));
                break;
            case BasicAuth basic when !hasAuthorization:
                var credentials = resolve(basic.Username) + ":" + resolve(basic.Password);
                metadata.Add(new("authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials))));
                break;
            case ApiKeyAuth { Location: ApiKeyLocation.Query }:
                errors.Add(Messages.Get("Grpc_ApiKeyInQuery"));
                break;
            case ApiKeyAuth apiKey:
                metadata.Add(new(resolve(apiKey.Key).ToLowerInvariant(), resolve(apiKey.Value)));
                break;
        }
    }
}
