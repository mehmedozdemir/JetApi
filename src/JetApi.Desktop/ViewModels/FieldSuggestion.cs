using JetApi.Desktop.Localization;
using JetApi.Grpc.Schema;

namespace JetApi.Desktop.ViewModels;

/// <summary>A field of a gRPC message, in the field list and in the message editor's suggestions.</summary>
/// <param name="Name">JSON name, as written in the message.</param>
/// <param name="Type">"string", "repeated Address", "map&lt;string, int32&gt;", "Color (enum)".</param>
/// <param name="Note">"oneof contact" for oneof members, otherwise empty.</param>
/// <param name="Field">The field.</param>
public sealed record FieldSuggestion(string Name, string Type, string Note, ProtoField Field)
{
    /// <summary>The suggestion for a field.</summary>
    public static FieldSuggestion From(ProtoField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new FieldSuggestion(field.JsonName, Describe(field), field.OneofName is { } oneof ? Loc.Format("Grpc_FieldOneof", oneof) : string.Empty, field);
    }

    /// <summary>A short type description.</summary>
    public static string Describe(ProtoField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.IsMap)
        {
            var entry = field.MessageType!;
            return $"map<{entry.FindField(1)!.Type.ToString().ToLowerInvariant()}, {Describe(entry.FindField(2)!)}>";
        }

        var type = field.Type switch
        {
            ProtoFieldType.Message => field.MessageType!.FullName.StartsWith("google.protobuf.", StringComparison.Ordinal)
                ? field.MessageType.FullName["google.protobuf.".Length..]
                : field.MessageType.FullName[(field.MessageType.FullName.LastIndexOf('.') + 1)..],
            ProtoFieldType.Enum => field.EnumType!.FullName[(field.EnumType.FullName.LastIndexOf('.') + 1)..] + " (enum)",
            _ => field.Type.ToString().ToLowerInvariant(),
        };
        return field.IsRepeated ? $"repeated {type}" : type;
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}
