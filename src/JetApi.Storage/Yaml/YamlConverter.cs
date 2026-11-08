using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace JetApi.Storage.Yaml;

/// <summary>Shared YAML settings: camelCase keys, defaults omitted, multi-line strings as literal blocks, LF line endings.</summary>
internal static class YamlConverter
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .WithEventEmitter(next => new LiteralBlockEmitter(next))
        .WithNewLine("\n")
        .DisableAliases()
        .Build();

    // Unknown keys are errors on purpose: a typo in a hand-edited file must not be silently dropped.
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static string Serialize<T>(T value) => Serializer.Serialize(value);

    public static T Deserialize<T>(string yaml) => Deserializer.Deserialize<T>(yaml);

    private sealed class LiteralBlockEmitter(IEventEmitter next) : ChainedEventEmitter(next)
    {
        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
        {
            if (eventInfo.Source.Value is string text && text.Contains('\n', StringComparison.Ordinal))
            {
                eventInfo.Style = ScalarStyle.Literal;
            }

            base.Emit(eventInfo, emitter);
        }
    }
}
