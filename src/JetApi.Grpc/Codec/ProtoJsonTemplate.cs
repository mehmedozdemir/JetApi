using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using JetApi.Grpc.Schema;

namespace JetApi.Grpc.Codec;

/// <summary>Generates an example JSON body for a message type, used to pre-fill new gRPC requests.</summary>
public static class ProtoJsonTemplate
{
    private const int MaxDepth = 8;

    /// <summary>
    /// Creates JSON containing every field with a type-appropriate placeholder value.
    /// For a oneof only its first field is included; recursive types stop at an empty object.
    /// </summary>
    public static string Create(ProtoMessageType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            WriteMessage(writer, type, new Stack<string>());
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static void WriteMessage(Utf8JsonWriter writer, ProtoMessageType type, Stack<string> path)
    {
        if (TryWriteWellKnown(writer, type))
        {
            return;
        }

        writer.WriteStartObject();
        if (!path.Contains(type.FullName) && path.Count < MaxDepth)
        {
            path.Push(type.FullName);
            var writtenOneofs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in type.Fields)
            {
                if (field.OneofName is { } oneof && !writtenOneofs.Add(oneof))
                {
                    continue;
                }

                writer.WritePropertyName(field.JsonName);
                WriteField(writer, field, path);
            }

            path.Pop();
        }

        writer.WriteEndObject();
    }

    private static void WriteField(Utf8JsonWriter writer, ProtoField field, Stack<string> path)
    {
        if (field.IsMap)
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
        else if (field.IsRepeated)
        {
            writer.WriteStartArray();
            WriteValue(writer, field, path);
            writer.WriteEndArray();
        }
        else
        {
            WriteValue(writer, field, path);
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, ProtoField field, Stack<string> path)
    {
        switch (field.Type)
        {
            case ProtoFieldType.Message or ProtoFieldType.Group:
                WriteMessage(writer, field.MessageType!, path);
                break;
            case ProtoFieldType.Enum:
                writer.WriteStringValue(field.EnumType!.DefaultValue.Key);
                break;
            case ProtoFieldType.Bool:
                writer.WriteBooleanValue(false);
                break;
            case ProtoFieldType.String or ProtoFieldType.Bytes:
                writer.WriteStringValue(string.Empty);
                break;
            case ProtoFieldType.Int64 or ProtoFieldType.UInt64 or ProtoFieldType.SInt64 or ProtoFieldType.Fixed64 or ProtoFieldType.SFixed64:
                writer.WriteStringValue("0");
                break;
            default:
                writer.WriteNumberValue(0);
                break;
        }
    }

    private static bool TryWriteWellKnown(Utf8JsonWriter writer, ProtoMessageType type)
    {
        switch (type.FullName)
        {
            case "google.protobuf.Timestamp":
                writer.WriteStringValue("1970-01-01T00:00:00Z");
                return true;
            case "google.protobuf.Duration":
                writer.WriteStringValue("0s");
                return true;
            case "google.protobuf.FieldMask":
                writer.WriteStringValue(string.Empty);
                return true;
            case "google.protobuf.Value":
                writer.WriteNullValue();
                return true;
            case "google.protobuf.ListValue":
                writer.WriteStartArray();
                writer.WriteEndArray();
                return true;
            case "google.protobuf.Struct" or "google.protobuf.Any" or "google.protobuf.Empty":
                writer.WriteStartObject();
                writer.WriteEndObject();
                return true;
            case var name when name.StartsWith("google.protobuf.", StringComparison.Ordinal) && name.EndsWith("Value", StringComparison.Ordinal):
                WriteValue(writer, type.FindField(1)!, new Stack<string>());
                return true;
            default:
                return false;
        }
    }
}
