using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Google.Protobuf;
using JetApi.Grpc.Schema;

namespace JetApi.Grpc.Codec;

public sealed partial class ProtoJsonCodec
{
    /// <summary>Decodes a binary message of type <paramref name="type"/> to JSON.</summary>
    /// <exception cref="ProtoJsonException">The bytes are not a valid message of this type.</exception>
    public string Decode(ProtoMessageType type, ReadOnlyMemory<byte> data, ProtoJsonDecodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        options ??= ProtoJsonDecodeOptions.Default;

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = options.Indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            MaxDepth = MaxDepth * 2,
        }))
        {
            WriteMessageJson(writer, type, data, options, "$", 0);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private void WriteMessageJson(Utf8JsonWriter writer, ProtoMessageType type, ReadOnlyMemory<byte> data, ProtoJsonDecodeOptions options, string path, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new ProtoJsonException(path, Messages.Get("Json_TooDeep"));
        }

        var values = ReadFields(type, data, path);
        if (HasSpecialJsonForm(type))
        {
            WriteWellKnownJson(writer, type, values, options, path, depth);
            return;
        }

        writer.WriteStartObject();
        WriteFieldsJson(writer, type, values, options, path, depth);
        writer.WriteEndObject();
    }

    private void WriteFieldsJson(Utf8JsonWriter writer, ProtoMessageType type, Dictionary<ProtoField, List<object>> values, ProtoJsonDecodeOptions options, string path, int depth)
    {
        foreach (var field in type.Fields)
        {
            var fieldPath = path + "." + field.JsonName;
            if (values.TryGetValue(field, out var fieldValues))
            {
                writer.WritePropertyName(field.JsonName);
                WriteFieldValueJson(writer, field, fieldValues, options, fieldPath, depth);
            }
            else if (options.EmitDefaultValues && !field.HasPresence)
            {
                writer.WritePropertyName(field.JsonName);
                WriteDefaultJson(writer, field);
            }
        }
    }

    private void WriteFieldValueJson(Utf8JsonWriter writer, ProtoField field, List<object> values, ProtoJsonDecodeOptions options, string path, int depth)
    {
        if (field.IsMap)
        {
            WriteMapJson(writer, field, values, options, path, depth);
        }
        else if (field.IsRepeated)
        {
            writer.WriteStartArray();
            for (var i = 0; i < values.Count; i++)
            {
                WriteValueJson(writer, field, values[i], options, $"{path}[{i}]", depth);
            }

            writer.WriteEndArray();
        }
        else
        {
            WriteValueJson(writer, field, values[^1], options, path, depth);
        }
    }

    private void WriteMapJson(Utf8JsonWriter writer, ProtoField field, List<object> entries, ProtoJsonDecodeOptions options, string path, int depth)
    {
        var entryType = field.MessageType!;
        var keyField = entryType.FindField(1)!;
        var valueField = entryType.FindField(2)!;

        // Later entries with the same key replace earlier ones, as in every protobuf runtime.
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (MessageBytes entry in entries)
        {
            var entryValues = ReadFields(entryType, entry.Data, path);
            var key = entryValues.TryGetValue(keyField, out var keys) ? keys[^1] : DefaultScalar(keyField);
            map[FormatMapKey(key)] = entryValues.TryGetValue(valueField, out var mapValues) ? mapValues[^1] : null;
        }

        writer.WriteStartObject();
        foreach (var (key, value) in map)
        {
            writer.WritePropertyName(key);
            if (value is null)
            {
                WriteDefaultValueJson(writer, valueField, options, $"{path}[\"{key}\"]", depth);
            }
            else
            {
                WriteValueJson(writer, valueField, value, options, $"{path}[\"{key}\"]", depth);
            }
        }

        writer.WriteEndObject();
    }

    private void WriteDefaultValueJson(Utf8JsonWriter writer, ProtoField field, ProtoJsonDecodeOptions options, string path, int depth)
    {
        if (field.Type == ProtoFieldType.Message)
        {
            WriteMessageJson(writer, field.MessageType!, ReadOnlyMemory<byte>.Empty, options, path, depth + 1);
        }
        else
        {
            WriteScalarJson(writer, field, DefaultScalar(field));
        }
    }

    private void WriteValueJson(Utf8JsonWriter writer, ProtoField field, object value, ProtoJsonDecodeOptions options, string path, int depth)
    {
        if (value is MessageBytes message)
        {
            WriteMessageJson(writer, field.MessageType!, message.Data, options, path, depth + 1);
        }
        else
        {
            WriteScalarJson(writer, field, value);
        }
    }

    private static void WriteDefaultJson(Utf8JsonWriter writer, ProtoField field)
    {
        if (field.IsMap)
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
        else if (field.IsRepeated)
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
        }
        else
        {
            WriteScalarJson(writer, field, DefaultScalar(field));
        }
    }

    private static void WriteScalarJson(Utf8JsonWriter writer, ProtoField field, object value)
    {
        switch (value)
        {
            case double d:
                WriteDoubleJson(writer, d);
                break;
            case float f:
                if (float.IsFinite(f))
                {
                    writer.WriteNumberValue(f);
                }
                else
                {
                    WriteDoubleJson(writer, f);
                }

                break;
            case long l:
                writer.WriteStringValue(l.ToString(CultureInfo.InvariantCulture));
                break;
            case ulong ul:
                writer.WriteStringValue(ul.ToString(CultureInfo.InvariantCulture));
                break;
            case int i when field.Type == ProtoFieldType.Enum:
                if (field.EnumType!.FullName == NullValueEnum)
                {
                    writer.WriteNullValue();
                }
                else if (field.EnumType.GetName(i) is { } name)
                {
                    writer.WriteStringValue(name);
                }
                else
                {
                    writer.WriteNumberValue(i);
                }

                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case uint ui:
                writer.WriteNumberValue(ui);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case ByteString bytes:
                writer.WriteBase64StringValue(bytes.Span);
                break;
            default:
                throw new InvalidOperationException($"Unexpected scalar {value.GetType().Name}.");
        }
    }

    private static void WriteDoubleJson(Utf8JsonWriter writer, double value)
    {
        if (double.IsNaN(value))
        {
            writer.WriteStringValue("NaN");
        }
        else if (double.IsPositiveInfinity(value))
        {
            writer.WriteStringValue("Infinity");
        }
        else if (double.IsNegativeInfinity(value))
        {
            writer.WriteStringValue("-Infinity");
        }
        else
        {
            writer.WriteNumberValue(value);
        }
    }

    // ---- Well-known types ------------------------------------------------------------------

    private void WriteWellKnownJson(Utf8JsonWriter writer, ProtoMessageType type, Dictionary<ProtoField, List<object>> values, ProtoJsonDecodeOptions options, string path, int depth)
    {
        object? Single(int number) => values.TryGetValue(type.FindField(number)!, out var list) ? list[^1] : null;

        if (WrapperTypes.Contains(type.FullName))
        {
            var field = type.FindField(1)!;
            WriteScalarJson(writer, field, Single(1) ?? DefaultScalar(field));
            return;
        }

        switch (type.FullName)
        {
            case TimestampType:
                writer.WriteStringValue(FormatTimestamp((long)(Single(1) ?? 0L), (int)(Single(2) ?? 0), path));
                break;
            case DurationType:
                writer.WriteStringValue(FormatDuration((long)(Single(1) ?? 0L), (int)(Single(2) ?? 0), path));
                break;
            case FieldMaskType:
                var paths = values.TryGetValue(type.FindField(1)!, out var list) ? list.Cast<string>() : [];
                writer.WriteStringValue(string.Join(',', paths.Select(SnakeToCamel)));
                break;
            case StructType:
                var fieldsField = type.FindField(1)!;
                if (values.TryGetValue(fieldsField, out var entries))
                {
                    WriteMapJson(writer, fieldsField, entries, options, path, depth);
                }
                else
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                }

                break;
            case ListValueType:
                var valuesField = type.FindField(1)!;
                writer.WriteStartArray();
                foreach (var (item, index) in (values.TryGetValue(valuesField, out var items) ? items : []).Select((item, index) => (item, index)))
                {
                    WriteValueJson(writer, valuesField, item, options, $"{path}[{index}]", depth);
                }

                writer.WriteEndArray();
                break;
            case ValueType:
                var kind = type.Fields.LastOrDefault(values.ContainsKey);
                if (kind is null || kind.Number == 1)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    WriteValueJson(writer, kind, values[kind][^1], options, path, depth);
                }

                break;
            case AnyType:
                WriteAnyJson(writer, (string?)Single(1) ?? string.Empty, ((ByteString?)Single(2))?.Memory ?? ReadOnlyMemory<byte>.Empty, options, path, depth);
                break;
            default:
                throw new InvalidOperationException($"{type.FullName} has no special JSON form.");
        }
    }

    private void WriteAnyJson(Utf8JsonWriter writer, string typeUrl, ReadOnlyMemory<byte> payload, ProtoJsonDecodeOptions options, string path, int depth)
    {
        writer.WriteStartObject();
        if (typeUrl.Length == 0)
        {
            writer.WriteEndObject();
            return;
        }

        writer.WriteString("@type", typeUrl);
        var slash = typeUrl.LastIndexOf('/');
        if (!_schema.Messages.TryGetValue(slash >= 0 ? typeUrl[(slash + 1)..] : typeUrl, out var payloadType))
        {
            // Unknown payload type: show the raw bytes instead of failing the whole response.
            writer.WriteBase64String("@unresolvedValue", payload.Span);
        }
        else if (HasSpecialJsonForm(payloadType))
        {
            writer.WritePropertyName("value");
            WriteMessageJson(writer, payloadType, payload, options, path + ".value", depth + 1);
        }
        else
        {
            WriteFieldsJson(writer, payloadType, ReadFields(payloadType, payload, path), options, path, depth + 1);
        }

        writer.WriteEndObject();
    }

    internal static string FormatTimestamp(long seconds, int nanos, string path)
    {
        DateTimeOffset value;
        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ProtoJsonException(path, Messages.Format("Json_TimestampOutOfRange", seconds.ToString(CultureInfo.InvariantCulture)));
        }

        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + FormatNanos(Math.Abs(nanos)) + "Z";
    }

    internal static string FormatDuration(long seconds, int nanos, string path)
    {
        if ((seconds < 0 && nanos > 0) || (seconds > 0 && nanos < 0) || Math.Abs(nanos) >= NanosPerSecond)
        {
            throw new ProtoJsonException(path, Messages.Format("Json_InvalidDurationValue", seconds.ToString(CultureInfo.InvariantCulture), nanos.ToString(CultureInfo.InvariantCulture)));
        }

        var sign = seconds < 0 || nanos < 0 ? "-" : string.Empty;
        return sign + Math.Abs(seconds).ToString(CultureInfo.InvariantCulture) + FormatNanos(Math.Abs(nanos)) + "s";
    }

    // 0, 3, 6 or 9 fractional digits, like the reference implementations.
    private static string FormatNanos(int nanos)
    {
        if (nanos == 0)
        {
            return string.Empty;
        }

        var digits = nanos.ToString("D9", CultureInfo.InvariantCulture);
        return "." + (nanos % 1_000_000 == 0 ? digits[..3] : nanos % 1_000 == 0 ? digits[..6] : digits);
    }

    private static string SnakeToCamel(string path)
    {
        var builder = new StringBuilder(path.Length);
        var upperNext = false;
        foreach (var character in path)
        {
            if (character == '_')
            {
                upperNext = true;
            }
            else
            {
                builder.Append(upperNext ? char.ToUpperInvariant(character) : character);
                upperNext = false;
            }
        }

        return builder.ToString();
    }

    private static string FormatMapKey(object key) => key switch
    {
        bool b => b ? "true" : "false",
        string s => s,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => key.ToString() ?? string.Empty,
    };

    // ---- Binary reading ------------------------------------------------------------------------

    /// <summary>A length-delimited submessage, kept as bytes until it is written.</summary>
    private sealed record MessageBytes(ReadOnlyMemory<byte> Data);

    private static Dictionary<ProtoField, List<object>> ReadFields(ProtoMessageType type, ReadOnlyMemory<byte> data, string path)
    {
        var values = new Dictionary<ProtoField, List<object>>();
        try
        {
            var input = new CodedInputStream(data.ToArray());
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                var field = type.FindField(WireFormat.GetTagFieldNumber(tag));
                if (field is null)
                {
                    // Unknown fields (e.g. added in a newer server version) are skipped.
                    input.SkipLastField();
                    continue;
                }

                ReadField(input, field, WireFormat.GetTagWireType(tag), values, path);
            }
        }
        catch (InvalidProtocolBufferException exception)
        {
            throw new ProtoJsonException(path, Messages.Format("Json_InvalidMessageData", type.FullName, exception.Message));
        }

        return values;
    }

    private static void ReadField(CodedInputStream input, ProtoField field, WireFormat.WireType wireType, Dictionary<ProtoField, List<object>> values, string path)
    {
        if (!values.TryGetValue(field, out var list))
        {
            list = [];
            values[field] = list;
        }

        var expected = WireTypeOf(field.Type);
        if (field.Type == ProtoFieldType.Message && wireType == WireFormat.WireType.LengthDelimited)
        {
            var bytes = input.ReadBytes().Memory;
            if (!field.IsRepeated && list.Count == 1 && list[0] is MessageBytes previous)
            {
                // A singular message that appears twice is merged, which for the wire format means concatenated.
                list[0] = new MessageBytes(Concat(previous.Data, bytes));
            }
            else
            {
                list.Add(new MessageBytes(bytes));
            }
        }
        else if (wireType == expected && field.Type is not (ProtoFieldType.Message or ProtoFieldType.Group))
        {
            list.Add(ReadScalar(input, field.Type));
        }
        else if (field.IsRepeated && wireType == WireFormat.WireType.LengthDelimited && expected != WireFormat.WireType.LengthDelimited)
        {
            var packed = new CodedInputStream(input.ReadBytes().ToByteArray());
            while (!packed.IsAtEnd)
            {
                list.Add(ReadScalar(packed, field.Type));
            }
        }
        else
        {
            throw new ProtoJsonException(path + "." + field.JsonName, Messages.Format("Json_WireTypeMismatch", field.Name, field.Number.ToString(CultureInfo.InvariantCulture), wireType, field.Type));
        }
    }

    private static ReadOnlyMemory<byte> Concat(ReadOnlyMemory<byte> first, ReadOnlyMemory<byte> second)
    {
        var result = new byte[first.Length + second.Length];
        first.CopyTo(result);
        second.CopyTo(result.AsMemory(first.Length));
        return result;
    }

    private static object ReadScalar(CodedInputStream input, ProtoFieldType type) => type switch
    {
        ProtoFieldType.Double => input.ReadDouble(),
        ProtoFieldType.Float => input.ReadFloat(),
        ProtoFieldType.Int64 => input.ReadInt64(),
        ProtoFieldType.SInt64 => input.ReadSInt64(),
        ProtoFieldType.SFixed64 => input.ReadSFixed64(),
        ProtoFieldType.UInt64 => input.ReadUInt64(),
        ProtoFieldType.Fixed64 => input.ReadFixed64(),
        ProtoFieldType.Int32 => input.ReadInt32(),
        ProtoFieldType.SInt32 => input.ReadSInt32(),
        ProtoFieldType.SFixed32 => input.ReadSFixed32(),
        ProtoFieldType.UInt32 => input.ReadUInt32(),
        ProtoFieldType.Fixed32 => input.ReadFixed32(),
        ProtoFieldType.Bool => input.ReadBool(),
        ProtoFieldType.String => input.ReadString(),
        ProtoFieldType.Bytes => input.ReadBytes(),
        ProtoFieldType.Enum => input.ReadEnum(),
        _ => throw new InvalidOperationException($"{type} is not a scalar type."),
    };

    private static object DefaultScalar(ProtoField field) => field.Type switch
    {
        ProtoFieldType.Double => 0d,
        ProtoFieldType.Float => 0f,
        ProtoFieldType.Int64 or ProtoFieldType.SInt64 or ProtoFieldType.SFixed64 => 0L,
        ProtoFieldType.UInt64 or ProtoFieldType.Fixed64 => 0UL,
        ProtoFieldType.Int32 or ProtoFieldType.SInt32 or ProtoFieldType.SFixed32 => 0,
        ProtoFieldType.UInt32 or ProtoFieldType.Fixed32 => 0U,
        ProtoFieldType.Bool => false,
        ProtoFieldType.String => string.Empty,
        ProtoFieldType.Bytes => ByteString.Empty,
        ProtoFieldType.Enum => field.EnumType!.DefaultValue.Value,
        _ => throw new InvalidOperationException($"{field.Type} has no scalar default."),
    };
}
