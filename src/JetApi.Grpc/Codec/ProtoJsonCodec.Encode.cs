using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Protobuf;
using JetApi.Grpc.Schema;

namespace JetApi.Grpc.Codec;

public sealed partial class ProtoJsonCodec
{
    private const long MaxDurationSeconds = 315_576_000_000;
    private const int NanosPerSecond = 1_000_000_000;
    private const int MaxListedNames = 12;

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = MaxDepth,
    };

    private static readonly DateTimeOffset UnixEpoch = DateTimeOffset.UnixEpoch;

    /// <summary>Encodes a JSON document as a binary message of type <paramref name="type"/>.</summary>
    /// <exception cref="ProtoJsonException">The JSON is invalid or does not match the type; the message says where and why.</exception>
    public byte[] Encode(ProtoMessageType type, string json)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json, ParseOptions);
        }
        catch (JsonException exception)
        {
            throw new ProtoJsonException("$", $"Invalid JSON (line {exception.LineNumber + 1}, position {exception.BytePositionInLine + 1}): {exception.Message.Split(" LineNumber", 2)[0]}");
        }

        using (document)
        {
            return EncodeMessage(type, document.RootElement, "$", 0);
        }
    }

    private byte[] EncodeMessage(ProtoMessageType type, JsonElement element, string path, int depth, string? ignoredProperty = null)
    {
        if (depth > MaxDepth)
        {
            throw new ProtoJsonException(path, "The message is nested too deeply.");
        }

        if (ignoredProperty is null && HasSpecialJsonForm(type))
        {
            return EncodeWellKnown(type, element, path, depth);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ProtoJsonException(path, $"Expected an object for message {type.FullName}, got {Describe(element)}.");
        }

        var entries = new List<(ProtoField Field, JsonElement Value, string Path)>();
        var setOneofs = new Dictionary<string, string>(StringComparer.Ordinal);
        var setFields = new HashSet<int>();
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name == ignoredProperty)
            {
                continue;
            }

            var propertyPath = path + "." + property.Name;
            var field = type.FindField(property.Name) ?? throw new ProtoJsonException(
                propertyPath,
                $"Unknown field '{property.Name}' in {type.FullName}. Known fields: {ListNames(type.Fields.Select(f => f.JsonName))}.");

            if (!setFields.Add(field.Number))
            {
                throw new ProtoJsonException(propertyPath, $"Field '{field.Name}' is set more than once.");
            }

            if (property.Value.ValueKind == JsonValueKind.Null && !AcceptsNull(field))
            {
                continue;
            }

            if (field.OneofName is { } oneof)
            {
                if (setOneofs.TryGetValue(oneof, out var other))
                {
                    throw new ProtoJsonException(propertyPath, $"'{other}' and '{field.JsonName}' belong to oneof '{oneof}'; set only one of them.");
                }

                setOneofs[oneof] = field.JsonName;
            }

            entries.Add((field, property.Value, propertyPath));
        }

        return WriteMessage(output =>
        {
            foreach (var (field, value, fieldPath) in entries.OrderBy(entry => entry.Field.Number))
            {
                WriteField(output, field, value, fieldPath, depth);
            }
        });
    }

    private void WriteField(CodedOutputStream output, ProtoField field, JsonElement value, string path, int depth)
    {
        if (field.IsMap)
        {
            WriteMap(output, field, value, path, depth);
        }
        else if (field.IsRepeated)
        {
            WriteRepeated(output, field, value, path, depth);
        }
        else
        {
            WriteSingle(output, field, value, path, depth, force: field.HasPresence);
        }
    }

    private void WriteSingle(CodedOutputStream output, ProtoField field, JsonElement value, string path, int depth, bool force)
    {
        if (field.Type == ProtoFieldType.Group)
        {
            throw new ProtoJsonException(path, $"Field '{field.Name}' is a proto2 group, which is not supported.");
        }

        if (field.Type == ProtoFieldType.Message)
        {
            var bytes = EncodeMessage(field.MessageType!, value, path, depth + 1);
            output.WriteTag(field.Number, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(UnsafeByteOperations.UnsafeWrap(bytes));
            return;
        }

        var scalar = ParseScalar(field, value, path);
        if (!force && IsDefault(scalar))
        {
            return;
        }

        output.WriteTag(field.Number, WireTypeOf(field.Type));
        WriteScalar(output, field.Type, scalar);
    }

    private void WriteRepeated(CodedOutputStream output, ProtoField field, JsonElement value, string path, int depth)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ProtoJsonException(path, $"Expected an array for repeated field '{field.Name}', got {Describe(value)}.");
        }

        if (field.IsPacked)
        {
            var values = value.EnumerateArray().Select((item, i) => ParseScalar(field, item, $"{path}[{i}]")).ToList();
            if (values.Count == 0)
            {
                return;
            }

            var packed = WriteMessage(inner =>
            {
                foreach (var scalar in values)
                {
                    WriteScalar(inner, field.Type, scalar);
                }
            });
            output.WriteTag(field.Number, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(UnsafeByteOperations.UnsafeWrap(packed));
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemPath = $"{path}[{index++}]";
            if (item.ValueKind == JsonValueKind.Null && !AcceptsNull(field))
            {
                throw new ProtoJsonException(itemPath, "null is not allowed in a repeated field.");
            }

            WriteSingle(output, field, item, itemPath, depth, force: true);
        }
    }

    private void WriteMap(CodedOutputStream output, ProtoField field, JsonElement value, string path, int depth)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ProtoJsonException(path, $"Expected an object for map field '{field.Name}', got {Describe(value)}.");
        }

        var entryType = field.MessageType!;
        var keyField = entryType.FindField(1)!;
        var valueField = entryType.FindField(2)!;
        foreach (var property in value.EnumerateObject())
        {
            var entryPath = $"{path}[\"{property.Name}\"]";
            var key = ParseMapKey(keyField, property.Name, entryPath);
            if (property.Value.ValueKind == JsonValueKind.Null && !AcceptsNull(valueField))
            {
                throw new ProtoJsonException(entryPath, "null is not allowed as a map value.");
            }

            var entry = WriteMessage(inner =>
            {
                inner.WriteTag(1, WireTypeOf(keyField.Type));
                WriteScalar(inner, keyField.Type, key);
                WriteSingle(inner, valueField, property.Value, entryPath, depth, force: true);
            });
            output.WriteTag(field.Number, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(UnsafeByteOperations.UnsafeWrap(entry));
        }
    }

    // ---- Well-known types ------------------------------------------------------------------

    private byte[] EncodeWellKnown(ProtoMessageType type, JsonElement element, string path, int depth)
    {
        if (WrapperTypes.Contains(type.FullName))
        {
            return EncodeSingleField(type.FindField(1)!, element, path, depth, force: false);
        }

        switch (type.FullName)
        {
            case TimestampType:
                var (seconds, nanos) = ParseTimestamp(ExpectString(element, path, "an RFC 3339 timestamp such as \"2026-01-31T10:00:00Z\""), path);
                return EncodeSecondsAndNanos(seconds, nanos);
            case DurationType:
                var duration = ParseDuration(ExpectString(element, path, "a duration such as \"1.5s\""), path);
                return EncodeSecondsAndNanos(duration.Seconds, duration.Nanos);
            case FieldMaskType:
                var mask = ExpectString(element, path, "a comma-separated field mask such as \"name,address.city\"");
                var paths = mask.Length == 0 ? [] : mask.Split(',').Select(p => CamelToSnake(p.Trim())).ToList();
                return WriteMessage(output =>
                {
                    foreach (var fieldPath in paths)
                    {
                        output.WriteTag(1, WireFormat.WireType.LengthDelimited);
                        output.WriteString(fieldPath);
                    }
                });
            case StructType:
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw new ProtoJsonException(path, $"Expected an object for google.protobuf.Struct, got {Describe(element)}.");
                }

                return EncodeSingleField(type.FindField(1)!, element, path, depth, force: true);
            case ListValueType:
                if (element.ValueKind != JsonValueKind.Array)
                {
                    throw new ProtoJsonException(path, $"Expected an array for google.protobuf.ListValue, got {Describe(element)}.");
                }

                return EncodeSingleField(type.FindField(1)!, element, path, depth, force: true);
            case ValueType:
                var kind = element.ValueKind switch
                {
                    JsonValueKind.Null => 1,
                    JsonValueKind.Number => 2,
                    JsonValueKind.String => 3,
                    JsonValueKind.True or JsonValueKind.False => 4,
                    JsonValueKind.Object => 5,
                    JsonValueKind.Array => 6,
                    _ => throw new ProtoJsonException(path, $"Unsupported JSON value {Describe(element)}."),
                };
                return EncodeSingleField(type.FindField(kind)!, element, path, depth, force: true);
            case AnyType:
                return EncodeAny(type, element, path, depth);
            default:
                throw new InvalidOperationException($"{type.FullName} has no special JSON form.");
        }
    }

    private byte[] EncodeAny(ProtoMessageType anyType, JsonElement element, string path, int depth)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ProtoJsonException(path, $"Expected an object for google.protobuf.Any, got {Describe(element)}.");
        }

        if (!element.TryGetProperty("@type", out var typeUrlElement))
        {
            return element.EnumerateObject().Any()
                ? throw new ProtoJsonException(path, "A google.protobuf.Any value needs an \"@type\" property.")
                : [];
        }

        var typeUrl = ExpectString(typeUrlElement, path + ".@type", "a type URL such as \"type.googleapis.com/users.v1.User\"");
        var payloadType = ResolveAnyType(typeUrl, path + ".@type");
        byte[] payload;
        if (HasSpecialJsonForm(payloadType))
        {
            if (!element.TryGetProperty("value", out var wrapped))
            {
                throw new ProtoJsonException(path, $"An Any holding {payloadType.FullName} must put the value in a \"value\" property.");
            }

            payload = EncodeMessage(payloadType, wrapped, path + ".value", depth + 1);
        }
        else
        {
            payload = EncodeMessage(payloadType, element, path, depth + 1, ignoredProperty: "@type");
        }

        return WriteMessage(output =>
        {
            output.WriteTag(anyType.FindField(1)!.Number, WireFormat.WireType.LengthDelimited);
            output.WriteString(typeUrl);
            output.WriteTag(anyType.FindField(2)!.Number, WireFormat.WireType.LengthDelimited);
            output.WriteBytes(UnsafeByteOperations.UnsafeWrap(payload));
        });
    }

    private byte[] EncodeSingleField(ProtoField field, JsonElement value, string path, int depth, bool force) =>
        WriteMessage(output =>
        {
            if (field.IsRepeated)
            {
                WriteField(output, field, value, path, depth);
            }
            else
            {
                WriteSingle(output, field, value, path, depth, force);
            }
        });

    private static byte[] EncodeSecondsAndNanos(long seconds, int nanos) =>
        WriteMessage(output =>
        {
            if (seconds != 0)
            {
                output.WriteTag(1, WireFormat.WireType.Varint);
                output.WriteInt64(seconds);
            }

            if (nanos != 0)
            {
                output.WriteTag(2, WireFormat.WireType.Varint);
                output.WriteInt32(nanos);
            }
        });

    internal static (long Seconds, int Nanos) ParseTimestamp(string text, string path)
    {
        var match = TimestampPattern().Match(text);
        if (!match.Success
            || !DateTime.TryParseExact(match.Groups["dt"].Value.ToUpperInvariant(), "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dateTime))
        {
            throw new ProtoJsonException(path, $"'{text}' is not an RFC 3339 timestamp such as \"2026-01-31T10:00:00Z\" or \"2026-01-31T13:00:00.5+03:00\".");
        }

        var offset = TimeSpan.Zero;
        var zone = match.Groups["zone"].Value;
        if (!zone.Equals("Z", StringComparison.OrdinalIgnoreCase))
        {
            var sign = zone[0] == '-' ? -1 : 1;
            offset = sign * new TimeSpan(int.Parse(zone[1..3], CultureInfo.InvariantCulture), int.Parse(zone[4..6], CultureInfo.InvariantCulture), 0);
        }

        var seconds = (long)(new DateTimeOffset(dateTime, TimeSpan.Zero) - offset - UnixEpoch).TotalSeconds;
        return (seconds, ParseNanos(match.Groups["frac"].Value));
    }

    internal static (long Seconds, int Nanos) ParseDuration(string text, string path)
    {
        var match = DurationPattern().Match(text);
        if (!match.Success || !long.TryParse(match.Groups["sec"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds > MaxDurationSeconds)
        {
            throw new ProtoJsonException(path, $"'{text}' is not a duration such as \"30s\", \"1.5s\" or \"-0.001s\" (at most {MaxDurationSeconds} seconds).");
        }

        var nanos = ParseNanos(match.Groups["frac"].Value);
        return match.Groups["neg"].Success ? (-seconds, -nanos) : (seconds, nanos);
    }

    private static int ParseNanos(string fraction) =>
        fraction.Length == 0 ? 0 : int.Parse(fraction[1..].PadRight(9, '0'), CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<dt>\d{4}-\d{2}-\d{2}[Tt]\d{2}:\d{2}:\d{2})(?<frac>\.\d{1,9})?(?<zone>[Zz]|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"^(?<neg>-)?(?<sec>\d+)(?<frac>\.\d{1,9})?s$", RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();

    private static string CamelToSnake(string path)
    {
        var builder = new StringBuilder(path.Length + 4);
        foreach (var character in path)
        {
            if (char.IsAsciiLetterUpper(character))
            {
                builder.Append('_').Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    // ---- Scalars -----------------------------------------------------------------------------

    private static bool AcceptsNull(ProtoField field) =>
        field.MessageType?.FullName == ValueType || field.EnumType?.FullName == NullValueEnum;

    private static object ParseScalar(ProtoField field, JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Null && field.EnumType?.FullName == NullValueEnum)
        {
            return 0;
        }

        return field.Type switch
        {
            ProtoFieldType.Double => ParseFloatingPoint(value, path, isFloat: false),
            ProtoFieldType.Float => (float)ParseFloatingPoint(value, path, isFloat: true),
            ProtoFieldType.Int64 or ProtoFieldType.SInt64 or ProtoFieldType.SFixed64 => (long)ParseIntegral(value, path, long.MinValue, long.MaxValue, "int64"),
            ProtoFieldType.UInt64 or ProtoFieldType.Fixed64 => (ulong)ParseIntegral(value, path, ulong.MinValue, ulong.MaxValue, "uint64"),
            ProtoFieldType.Int32 or ProtoFieldType.SInt32 or ProtoFieldType.SFixed32 => (int)ParseIntegral(value, path, int.MinValue, int.MaxValue, "int32"),
            ProtoFieldType.UInt32 or ProtoFieldType.Fixed32 => (uint)ParseIntegral(value, path, uint.MinValue, uint.MaxValue, "uint32"),
            ProtoFieldType.Bool => value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new ProtoJsonException(path, $"Expected true or false, got {Describe(value)}."),
            },
            ProtoFieldType.String => ExpectString(value, path, "a string"),
            ProtoFieldType.Bytes => ParseBytes(ExpectString(value, path, "a base64 string"), path),
            ProtoFieldType.Enum => ParseEnum(field.EnumType!, value, path),
            _ => throw new ProtoJsonException(path, $"Field '{field.Name}' of type {field.Type} is not a scalar."),
        };
    }

    private static object ParseMapKey(ProtoField keyField, string key, string path) => keyField.Type switch
    {
        ProtoFieldType.String => key,
        ProtoFieldType.Bool => key switch
        {
            "true" => true,
            "false" => false,
            _ => throw new ProtoJsonException(path, $"Map key '{key}' must be \"true\" or \"false\"."),
        },
        ProtoFieldType.Int64 or ProtoFieldType.SInt64 or ProtoFieldType.SFixed64 => (long)ParseIntegralText(key, path, long.MinValue, long.MaxValue, "int64"),
        ProtoFieldType.UInt64 or ProtoFieldType.Fixed64 => (ulong)ParseIntegralText(key, path, ulong.MinValue, ulong.MaxValue, "uint64"),
        ProtoFieldType.Int32 or ProtoFieldType.SInt32 or ProtoFieldType.SFixed32 => (int)ParseIntegralText(key, path, int.MinValue, int.MaxValue, "int32"),
        ProtoFieldType.UInt32 or ProtoFieldType.Fixed32 => (uint)ParseIntegralText(key, path, uint.MinValue, uint.MaxValue, "uint32"),
        _ => throw new ProtoJsonException(path, $"Map keys of type {keyField.Type} are not supported."),
    };

    private static decimal ParseIntegral(JsonElement value, string path, decimal min, decimal max, string typeName)
    {
        decimal number;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetDecimal(out number))
            {
                throw new ProtoJsonException(path, $"{value.GetRawText()} is out of range for {typeName}.");
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            return ParseIntegralText(value.GetString()!, path, min, max, typeName);
        }
        else
        {
            throw new ProtoJsonException(path, $"Expected a number for {typeName}, got {Describe(value)}.");
        }

        return CheckIntegral(number, path, min, max, typeName);
    }

    private static decimal ParseIntegralText(string text, string path, decimal min, decimal max, string typeName)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number))
        {
            throw new ProtoJsonException(path, $"\"{text}\" is not a valid {typeName}.");
        }

        return CheckIntegral(number, path, min, max, typeName);
    }

    private static decimal CheckIntegral(decimal number, string path, decimal min, decimal max, string typeName)
    {
        if (number != decimal.Truncate(number))
        {
            throw new ProtoJsonException(path, $"{number.ToString(CultureInfo.InvariantCulture)} is not a whole number ({typeName}).");
        }

        if (number < min || number > max)
        {
            throw new ProtoJsonException(path, $"{number.ToString(CultureInfo.InvariantCulture)} is out of range for {typeName} ({min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}).");
        }

        return number;
    }

    private static double ParseFloatingPoint(JsonElement value, string path, bool isFloat)
    {
        double number;
        if (value.ValueKind == JsonValueKind.Number)
        {
            number = value.GetDouble();
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            number = value.GetString() switch
            {
                "NaN" => double.NaN,
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                var text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) => parsed,
                var text => throw new ProtoJsonException(path, $"\"{text}\" is not a valid number."),
            };
        }
        else
        {
            throw new ProtoJsonException(path, $"Expected a number, got {Describe(value)}.");
        }

        if (!double.IsFinite(number) && value.ValueKind == JsonValueKind.Number)
        {
            throw new ProtoJsonException(path, "The number is out of range.");
        }

        if (isFloat && double.IsFinite(number) && Math.Abs(number) > float.MaxValue)
        {
            throw new ProtoJsonException(path, $"{value.GetRawText()} is out of range for float.");
        }

        return number;
    }

    private static ByteString ParseBytes(string text, string path)
    {
        var normalized = text.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - (normalized.Length % 4)) % 4), '=');
        try
        {
            return UnsafeByteOperations.UnsafeWrap(Convert.FromBase64String(normalized));
        }
        catch (FormatException)
        {
            throw new ProtoJsonException(path, "Expected base64-encoded bytes.");
        }
    }

    private static int ParseEnum(ProtoEnumType enumType, JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var name = value.GetString()!;
            return enumType.TryGetNumber(name, out var number)
                ? number
                : throw new ProtoJsonException(path, $"'{name}' is not a value of {enumType.FullName}. Valid values: {ListNames(enumType.Values.Select(v => v.Key))}.");
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric))
        {
            return numeric;
        }

        throw new ProtoJsonException(path, $"Expected an enum name of {enumType.FullName}, got {Describe(value)}.");
    }

    private static string ExpectString(JsonElement value, string path, string expected) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ProtoJsonException(path, $"Expected {expected}, got {Describe(value)}.");

    private static bool IsDefault(object value) => value switch
    {
        double d => BitConverter.DoubleToInt64Bits(d) == 0,
        float f => BitConverter.SingleToInt32Bits(f) == 0,
        long l => l == 0,
        ulong ul => ul == 0,
        int i => i == 0,
        uint ui => ui == 0,
        bool b => !b,
        string s => s.Length == 0,
        ByteString bytes => bytes.IsEmpty,
        _ => false,
    };

    private static WireFormat.WireType WireTypeOf(ProtoFieldType type) => type switch
    {
        ProtoFieldType.Double or ProtoFieldType.Fixed64 or ProtoFieldType.SFixed64 => WireFormat.WireType.Fixed64,
        ProtoFieldType.Float or ProtoFieldType.Fixed32 or ProtoFieldType.SFixed32 => WireFormat.WireType.Fixed32,
        ProtoFieldType.String or ProtoFieldType.Bytes or ProtoFieldType.Message => WireFormat.WireType.LengthDelimited,
        ProtoFieldType.Group => WireFormat.WireType.StartGroup,
        _ => WireFormat.WireType.Varint,
    };

    private static void WriteScalar(CodedOutputStream output, ProtoFieldType type, object value)
    {
        switch (type)
        {
            case ProtoFieldType.Double: output.WriteDouble((double)value); break;
            case ProtoFieldType.Float: output.WriteFloat((float)value); break;
            case ProtoFieldType.Int64: output.WriteInt64((long)value); break;
            case ProtoFieldType.SInt64: output.WriteSInt64((long)value); break;
            case ProtoFieldType.SFixed64: output.WriteSFixed64((long)value); break;
            case ProtoFieldType.UInt64: output.WriteUInt64((ulong)value); break;
            case ProtoFieldType.Fixed64: output.WriteFixed64((ulong)value); break;
            case ProtoFieldType.Int32: output.WriteInt32((int)value); break;
            case ProtoFieldType.SInt32: output.WriteSInt32((int)value); break;
            case ProtoFieldType.SFixed32: output.WriteSFixed32((int)value); break;
            case ProtoFieldType.UInt32: output.WriteUInt32((uint)value); break;
            case ProtoFieldType.Fixed32: output.WriteFixed32((uint)value); break;
            case ProtoFieldType.Bool: output.WriteBool((bool)value); break;
            case ProtoFieldType.String: output.WriteString((string)value); break;
            case ProtoFieldType.Bytes: output.WriteBytes((ByteString)value); break;
            case ProtoFieldType.Enum: output.WriteEnum((int)value); break;
            default: throw new InvalidOperationException($"{type} is not a scalar type.");
        }
    }

    private static byte[] WriteMessage(Action<CodedOutputStream> write)
    {
        using var buffer = new MemoryStream();
        using (var output = new CodedOutputStream(buffer, leaveOpen: true))
        {
            write(output);
        }

        return buffer.ToArray();
    }

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => $"the string \"{Truncate(value.GetString()!)}\"",
        JsonValueKind.Number => $"the number {value.GetRawText()}",
        JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Null => "null",
        _ => value.ValueKind.ToString(),
    };

    private static string Truncate(string text) => text.Length <= 40 ? text : text[..40] + "…";

    private static string ListNames(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count <= MaxListedNames
            ? string.Join(", ", list)
            : string.Join(", ", list.Take(MaxListedNames)) + $", … ({list.Count - MaxListedNames} more)";
    }
}
