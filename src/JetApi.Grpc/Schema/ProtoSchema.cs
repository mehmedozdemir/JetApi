using System.Diagnostics.CodeAnalysis;

namespace JetApi.Grpc.Schema;

/// <summary>Protobuf scalar and composite field types, numbered like <c>FieldDescriptorProto.Type</c> minus one.</summary>
[SuppressMessage("Naming", "CA1720", Justification = "Member names are the protobuf type names.")]
public enum ProtoFieldType
{
    /// <summary>double</summary>
    Double,

    /// <summary>float</summary>
    Float,

    /// <summary>int64</summary>
    Int64,

    /// <summary>uint64</summary>
    UInt64,

    /// <summary>int32</summary>
    Int32,

    /// <summary>fixed64</summary>
    Fixed64,

    /// <summary>fixed32</summary>
    Fixed32,

    /// <summary>bool</summary>
    Bool,

    /// <summary>string</summary>
    String,

    /// <summary>proto2 group (not supported by the JSON codec)</summary>
    Group,

    /// <summary>A nested message.</summary>
    Message,

    /// <summary>bytes</summary>
    Bytes,

    /// <summary>uint32</summary>
    UInt32,

    /// <summary>An enum.</summary>
    Enum,

    /// <summary>sfixed32</summary>
    SFixed32,

    /// <summary>sfixed64</summary>
    SFixed64,

    /// <summary>sint32</summary>
    SInt32,

    /// <summary>sint64</summary>
    SInt64,
}

/// <summary>A loaded set of proto files: their services, messages and enums.</summary>
public sealed class ProtoSchema
{
    internal ProtoSchema(
        IReadOnlyList<ProtoService> services,
        IReadOnlyDictionary<string, ProtoMessageType> messages,
        IReadOnlyDictionary<string, ProtoEnumType> enums)
    {
        Services = services;
        Messages = messages;
        Enums = enums;
    }

    /// <summary>Services declared in the requested files (not in their imports), sorted by full name.</summary>
    public IReadOnlyList<ProtoService> Services { get; }

    /// <summary>All message types including imports, by full name without leading dot.</summary>
    public IReadOnlyDictionary<string, ProtoMessageType> Messages { get; }

    /// <summary>All enum types including imports, by full name without leading dot.</summary>
    public IReadOnlyDictionary<string, ProtoEnumType> Enums { get; }

    /// <summary>Finds a method by service full name and method name.</summary>
    public ProtoMethod? FindMethod(string serviceFullName, string methodName) =>
        Services.FirstOrDefault(s => s.FullName == serviceFullName)?.Methods.FirstOrDefault(m => m.Name == methodName);
}

/// <summary>A gRPC service.</summary>
/// <param name="FullName">Package-qualified name, e.g. <c>users.v1.UserService</c>.</param>
/// <param name="Name">Simple name.</param>
/// <param name="File">The proto file that declares it.</param>
/// <param name="Methods">Methods in declaration order.</param>
public sealed record ProtoService(string FullName, string Name, string File, IReadOnlyList<ProtoMethod> Methods);

/// <summary>A gRPC method.</summary>
/// <param name="ServiceFullName">Owning service.</param>
/// <param name="Name">Method name.</param>
/// <param name="InputType">Request message.</param>
/// <param name="OutputType">Response message.</param>
/// <param name="IsClientStreaming">Client sends a stream.</param>
/// <param name="IsServerStreaming">Server returns a stream.</param>
public sealed record ProtoMethod(
    string ServiceFullName,
    string Name,
    ProtoMessageType InputType,
    ProtoMessageType OutputType,
    bool IsClientStreaming,
    bool IsServerStreaming)
{
    /// <summary>True for request/response calls.</summary>
    public bool IsUnary => !IsClientStreaming && !IsServerStreaming;
}

/// <summary>A message type.</summary>
public sealed class ProtoMessageType
{
    private readonly List<ProtoField> _fields = [];
    private readonly Dictionary<string, ProtoField> _fieldsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, ProtoField> _fieldsByNumber = [];

    internal ProtoMessageType(string fullName, bool isMapEntry)
    {
        FullName = fullName;
        IsMapEntry = isMapEntry;
    }

    /// <summary>Full name without leading dot, e.g. <c>users.v1.User</c>.</summary>
    public string FullName { get; }

    /// <summary>True for the synthetic entry type of a <c>map&lt;K, V&gt;</c> field.</summary>
    public bool IsMapEntry { get; }

    /// <summary>Fields in declaration order.</summary>
    public IReadOnlyList<ProtoField> Fields => _fields;

    /// <summary>Finds a field by its JSON name or proto name.</summary>
    public ProtoField? FindField(string name) => _fieldsByName.GetValueOrDefault(name);

    /// <summary>Finds a field by number.</summary>
    public ProtoField? FindField(int number) => _fieldsByNumber.GetValueOrDefault(number);

    internal void AddField(ProtoField field)
    {
        _fields.Add(field);
        _fieldsByNumber[field.Number] = field;
        _fieldsByName.TryAdd(field.JsonName, field);
        _fieldsByName.TryAdd(field.Name, field);
    }

    /// <inheritdoc />
    public override string ToString() => FullName;
}

/// <summary>A message field.</summary>
public sealed class ProtoField
{
    internal ProtoField(string name, string jsonName, int number, ProtoFieldType type, bool isRepeated, bool hasPresence, bool isPacked, string? oneofName)
    {
        Name = name;
        JsonName = jsonName;
        Number = number;
        Type = type;
        IsRepeated = isRepeated;
        HasPresence = hasPresence;
        IsPacked = isPacked;
        OneofName = oneofName;
    }

    /// <summary>Name as declared, e.g. <c>user_id</c>.</summary>
    public string Name { get; }

    /// <summary>JSON name, e.g. <c>userId</c>.</summary>
    public string JsonName { get; }

    /// <summary>Field number.</summary>
    public int Number { get; }

    /// <summary>Field type.</summary>
    public ProtoFieldType Type { get; }

    /// <summary>True for repeated and map fields.</summary>
    public bool IsRepeated { get; }

    /// <summary>
    /// True when "not set" is distinguishable from the default value (messages, oneof members,
    /// proto3 <c>optional</c>, proto2 singular fields).
    /// </summary>
    public bool HasPresence { get; }

    /// <summary>True when repeated scalars are written packed.</summary>
    public bool IsPacked { get; }

    /// <summary>Name of the (non-synthetic) oneof this field belongs to, if any.</summary>
    public string? OneofName { get; }

    /// <summary>The message type for <see cref="ProtoFieldType.Message"/> fields.</summary>
    public ProtoMessageType? MessageType { get; internal set; }

    /// <summary>The enum type for <see cref="ProtoFieldType.Enum"/> fields.</summary>
    public ProtoEnumType? EnumType { get; internal set; }

    /// <summary>True for <c>map&lt;K, V&gt;</c> fields.</summary>
    public bool IsMap => IsRepeated && MessageType is { IsMapEntry: true };

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>An enum type.</summary>
public sealed class ProtoEnumType
{
    private readonly Dictionary<string, int> _numbersByName;
    private readonly Dictionary<int, string> _namesByNumber = [];

    internal ProtoEnumType(string fullName, IReadOnlyList<KeyValuePair<string, int>> values)
    {
        FullName = fullName;
        Values = values;
        _numbersByName = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        foreach (var (name, number) in values)
        {
            _namesByNumber.TryAdd(number, name);
        }
    }

    /// <summary>Full name without leading dot.</summary>
    public string FullName { get; }

    /// <summary>Values in declaration order.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> Values { get; }

    /// <summary>The value used when the field is not set: the one numbered 0, else the first.</summary>
    public KeyValuePair<string, int> DefaultValue =>
        Values.FirstOrDefault(v => v.Value == 0) is { Key: not null } zero ? zero : Values[0];

    /// <summary>Finds a value by name.</summary>
    public bool TryGetNumber(string name, out int number) => _numbersByName.TryGetValue(name, out number);

    /// <summary>Finds the first name declared for a number.</summary>
    public string? GetName(int number) => _namesByNumber.GetValueOrDefault(number);
}
