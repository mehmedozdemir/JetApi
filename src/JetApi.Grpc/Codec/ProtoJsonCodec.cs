using JetApi.Grpc.Schema;

namespace JetApi.Grpc.Codec;

/// <summary>Options for converting binary messages to JSON.</summary>
/// <param name="EmitDefaultValues">
/// Write fields that are not set with their default value (<c>0</c>, <c>""</c>, <c>false</c>, <c>[]</c>, <c>{}</c>).
/// Fields with explicit presence (messages, oneofs, <c>optional</c>) are still omitted when not set.
/// </param>
/// <param name="Indented">Pretty-print the JSON.</param>
public sealed record ProtoJsonDecodeOptions(bool EmitDefaultValues = true, bool Indented = true)
{
    /// <summary>The defaults.</summary>
    public static ProtoJsonDecodeOptions Default { get; } = new();
}

/// <summary>
/// Converts between JSON and the protobuf binary format using a runtime <see cref="ProtoSchema"/>,
/// following the proto3 JSON mapping (https://protobuf.dev/programming-guides/json/):
/// lowerCamelCase or original field names, 64-bit integers as strings, enums by name,
/// bytes as base64 and the special JSON forms of Google's well-known types.
/// </summary>
public sealed partial class ProtoJsonCodec
{
    private const int MaxDepth = 100;

    private const string TimestampType = "google.protobuf.Timestamp";
    private const string DurationType = "google.protobuf.Duration";
    private const string FieldMaskType = "google.protobuf.FieldMask";
    private const string StructType = "google.protobuf.Struct";
    private const string ValueType = "google.protobuf.Value";
    private const string ListValueType = "google.protobuf.ListValue";
    private const string AnyType = "google.protobuf.Any";
    private const string NullValueEnum = "google.protobuf.NullValue";

    private static readonly HashSet<string> WrapperTypes = new(StringComparer.Ordinal)
    {
        "google.protobuf.DoubleValue", "google.protobuf.FloatValue", "google.protobuf.Int64Value",
        "google.protobuf.UInt64Value", "google.protobuf.Int32Value", "google.protobuf.UInt32Value",
        "google.protobuf.BoolValue", "google.protobuf.StringValue", "google.protobuf.BytesValue",
    };

    private readonly ProtoSchema _schema;

    /// <summary>Creates a codec; the schema is needed to resolve <c>google.protobuf.Any</c> payloads.</summary>
    public ProtoJsonCodec(ProtoSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _schema = schema;
    }

    private static bool HasSpecialJsonForm(ProtoMessageType type) =>
        type.FullName is TimestampType or DurationType or FieldMaskType or StructType or ValueType or ListValueType or AnyType
        || WrapperTypes.Contains(type.FullName);

    private ProtoMessageType ResolveAnyType(string typeUrl, string path)
    {
        var slash = typeUrl.LastIndexOf('/');
        var typeName = slash >= 0 ? typeUrl[(slash + 1)..] : typeUrl;
        return _schema.Messages.GetValueOrDefault(typeName)
            ?? throw new ProtoJsonException(path, Messages.Format("Json_UnknownAnyType", typeName));
    }
}
