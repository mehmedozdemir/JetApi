extern alias pbnet;

using JetApi.Core.Model;
using PbField = pbnet::Google.Protobuf.Reflection.FieldDescriptorProto;
using PbFileSet = pbnet::Google.Protobuf.Reflection.FileDescriptorSet;
using PbMessage = pbnet::Google.Protobuf.Reflection.DescriptorProto;

namespace JetApi.Grpc.Schema;

/// <summary>A problem in a proto file.</summary>
/// <param name="File">Proto file path as imported.</param>
/// <param name="Line">1-based line, 0 when unknown.</param>
/// <param name="Column">1-based column, 0 when unknown.</param>
/// <param name="Message">Description.</param>
/// <param name="IsError">False for warnings.</param>
public sealed record ProtoDiagnostic(string File, int Line, int Column, string Message, bool IsError);

/// <summary>The result of loading proto files.</summary>
/// <param name="Schema">The schema, or <c>null</c> when there are errors.</param>
/// <param name="Diagnostics">Errors and warnings.</param>
public sealed record ProtoLoadResult(ProtoSchema? Schema, IReadOnlyList<ProtoDiagnostic> Diagnostics)
{
    /// <summary>True when the schema loaded.</summary>
    public bool IsSuccess => Schema is not null;
}

/// <summary>
/// Parses <c>.proto</c> files at runtime (no <c>protoc</c>, no code generation) using a managed
/// parser. Google's well-known types (<c>google/protobuf/*.proto</c>) are built in.
/// </summary>
public static class ProtoSchemaLoader
{
    /// <summary>Loads the proto files configured for a collection.</summary>
    /// <param name="settings">Import paths and files; import paths are relative to <paramref name="workspaceRoot"/>.</param>
    /// <param name="workspaceRoot">Workspace root. Used as the only import path when none is configured.</param>
    public static ProtoLoadResult Load(ProtoSettings settings, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        var importPaths = settings.ImportPaths.Count == 0
            ? [Path.GetFullPath(workspaceRoot)]
            : settings.ImportPaths.Select(path => Path.GetFullPath(path, workspaceRoot)).ToList();
        return Load(importPaths, settings.Files);
    }

    /// <summary>Loads <paramref name="files"/> (relative to one of <paramref name="importPaths"/>).</summary>
    public static ProtoLoadResult Load(IReadOnlyList<string> importPaths, IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(importPaths);
        ArgumentNullException.ThrowIfNull(files);

        var diagnostics = new List<ProtoDiagnostic>();
        if (files.Count == 0)
        {
            diagnostics.Add(new ProtoDiagnostic(string.Empty, 0, 0, "No proto files are configured.", true));
            return new ProtoLoadResult(null, diagnostics);
        }

        var set = new PbFileSet();
        foreach (var importPath in importPaths)
        {
            if (!Directory.Exists(importPath))
            {
                diagnostics.Add(new ProtoDiagnostic(string.Empty, 0, 0, $"Import path '{importPath}' does not exist.", true));
                continue;
            }

            set.AddImportPath(importPath);
        }

        foreach (var file in files)
        {
            var normalized = file.Replace('\\', '/');
            if (!set.Add(normalized, includeInOutput: true))
            {
                diagnostics.Add(new ProtoDiagnostic(normalized, 0, 0, $"'{normalized}' was not found in any import path.", true));
            }
        }

        if (diagnostics.Count > 0)
        {
            return new ProtoLoadResult(null, diagnostics);
        }

        set.Process();
        diagnostics.AddRange(set.GetErrors().Select(e => new ProtoDiagnostic(e.File ?? string.Empty, e.LineNumber, e.ColumnNumber, e.Message, e.IsError)));
        if (diagnostics.Any(d => d.IsError))
        {
            return new ProtoLoadResult(null, diagnostics);
        }

        return new ProtoLoadResult(Build(set), diagnostics);
    }

    private static ProtoSchema Build(PbFileSet set)
    {
        var messages = new Dictionary<string, ProtoMessageType>(StringComparer.Ordinal);
        var enums = new Dictionary<string, ProtoEnumType>(StringComparer.Ordinal);
        var pending = new List<(ProtoMessageType Type, PbMessage Descriptor, bool IsProto2)>();

        // Pass 1: create every type, so fields can reference types declared later or recursively.
        foreach (var file in set.Files)
        {
            var prefix = string.IsNullOrEmpty(file.Package) ? string.Empty : file.Package + ".";
            var isProto2 = string.IsNullOrEmpty(file.Syntax) || file.Syntax == "proto2";
            foreach (var enumType in file.EnumTypes)
            {
                AddEnum(enums, prefix + enumType.Name, enumType);
            }

            foreach (var message in file.MessageTypes)
            {
                CollectMessage(prefix + message.Name, message, isProto2, messages, enums, pending);
            }
        }

        // Pass 2: fields.
        foreach (var (type, descriptor, isProto2) in pending)
        {
            foreach (var field in descriptor.Fields)
            {
                type.AddField(CreateField(field, descriptor, isProto2, messages, enums));
            }
        }

        var services = set.Files
            .Where(file => file.IncludeInOutput)
            .SelectMany(file => file.Services.Select(service =>
            {
                var fullName = (string.IsNullOrEmpty(file.Package) ? string.Empty : file.Package + ".") + service.Name;
                return new ProtoService(
                    fullName,
                    service.Name,
                    file.Name,
                    [.. service.Methods.Select(method => new ProtoMethod(
                        fullName,
                        method.Name,
                        messages[method.InputType.TrimStart('.')],
                        messages[method.OutputType.TrimStart('.')],
                        method.ClientStreaming,
                        method.ServerStreaming))]);
            }))
            .OrderBy(service => service.FullName, StringComparer.Ordinal)
            .ToList();

        return new ProtoSchema(services, messages, enums);
    }

    private static void CollectMessage(
        string fullName,
        PbMessage descriptor,
        bool isProto2,
        Dictionary<string, ProtoMessageType> messages,
        Dictionary<string, ProtoEnumType> enums,
        List<(ProtoMessageType, PbMessage, bool)> pending)
    {
        var type = new ProtoMessageType(fullName, descriptor.Options?.MapEntry ?? false);
        messages[fullName] = type;
        pending.Add((type, descriptor, isProto2));

        foreach (var enumType in descriptor.EnumTypes)
        {
            AddEnum(enums, fullName + "." + enumType.Name, enumType);
        }

        foreach (var nested in descriptor.NestedTypes)
        {
            CollectMessage(fullName + "." + nested.Name, nested, isProto2, messages, enums, pending);
        }
    }

    private static void AddEnum(Dictionary<string, ProtoEnumType> enums, string fullName, pbnet::Google.Protobuf.Reflection.EnumDescriptorProto descriptor) =>
        enums[fullName] = new ProtoEnumType(fullName, [.. descriptor.Values.Select(v => new KeyValuePair<string, int>(v.Name, v.Number))]);

    private static ProtoField CreateField(
        PbField field,
        PbMessage owner,
        bool isProto2,
        Dictionary<string, ProtoMessageType> messages,
        Dictionary<string, ProtoEnumType> enums)
    {
        var type = (ProtoFieldType)((int)field.type - 1);
        var isRepeated = field.label == PbField.Label.LabelRepeated;
        var isInOneof = field.ShouldSerializeOneofIndex();
        var oneofName = isInOneof && !field.Proto3Optional ? owner.OneofDecls[field.OneofIndex].Name : null;
        var hasPresence = !isRepeated && (type == ProtoFieldType.Message || isInOneof || isProto2);
        var isPackable = isRepeated && type is not (ProtoFieldType.String or ProtoFieldType.Bytes or ProtoFieldType.Message or ProtoFieldType.Group);
        var isPacked = isPackable && (field.Options is { } options && options.ShouldSerializePacked() ? options.Packed : !isProto2);

        var result = new ProtoField(field.Name, string.IsNullOrEmpty(field.JsonName) ? field.Name : field.JsonName, field.Number, type, isRepeated, hasPresence, isPacked, oneofName);
        var typeName = field.TypeName?.TrimStart('.');
        if (type is ProtoFieldType.Message or ProtoFieldType.Group && typeName is not null)
        {
            result.MessageType = messages[typeName];
        }
        else if (type == ProtoFieldType.Enum && typeName is not null)
        {
            result.EnumType = enums[typeName];
        }

        return result;
    }
}
