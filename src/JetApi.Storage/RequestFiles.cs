using JetApi.Core.Model;
using JetApi.Storage.Dto;
using JetApi.Storage.Yaml;
using YamlDotNet.Core;

namespace JetApi.Storage;

/// <summary>
/// Converts a single request to and from the same YAML used in request files. Used for history
/// snapshots, duplicating and copy/paste, so every copy of a request has one canonical format.
/// </summary>
public static class RequestFiles
{
    /// <summary>Serializes <paramref name="request"/> as the content of its <c>.http.yaml</c> or <c>.grpc.yaml</c> file.</summary>
    public static string Serialize(RequestNode request) => request switch
    {
        HttpRequestDefinition http => YamlConverter.Serialize(DtoMapper.ToDto(http)),
        GrpcRequestDefinition grpc => YamlConverter.Serialize(DtoMapper.ToDto(grpc)),
        null => throw new ArgumentNullException(nameof(request)),
        _ => throw new NotSupportedException($"Unsupported request type {request.GetType().Name}."),
    };

    /// <summary>Reads a request serialized by <see cref="Serialize"/>.</summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="key">Key to give the request.</param>
    /// <param name="isGrpc">True for a gRPC request.</param>
    /// <exception cref="InvalidDataException">The YAML is invalid.</exception>
    public static RequestNode Deserialize(string yaml, string key, bool isGrpc)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        try
        {
            if (isGrpc)
            {
                var file = YamlConverter.Deserialize<GrpcRequestFile>(yaml) ?? throw new InvalidDataException(Messages.Get("Storage_EmptyYaml"));
                DtoMapper.EnsureSchemaVersion(file.SchemaVersion);
                return DtoMapper.FromDto(file, key);
            }
            else
            {
                var file = YamlConverter.Deserialize<HttpRequestFile>(yaml) ?? throw new InvalidDataException(Messages.Get("Storage_EmptyYaml"));
                DtoMapper.EnsureSchemaVersion(file.SchemaVersion);
                return DtoMapper.FromDto(file, key);
            }
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }
    }
}
