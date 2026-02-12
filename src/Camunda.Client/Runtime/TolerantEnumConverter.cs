using System.Text.Json;
using System.Text.Json.Serialization;

namespace Camunda.Client.Runtime;

/// <summary>
/// A <see cref="JsonConverterFactory"/> for enum types that tolerates API values
/// with underscores (e.g. <c>"BPMN_ELEMENT"</c>) when the C# member name has them
/// stripped (e.g. <c>BPMNELEMENT</c>).
///
/// <para>
/// This is needed because the OpenAPI generator emits enum member names without
/// underscores while the Camunda REST API returns UPPER_SNAKE_CASE strings.
/// .NET 8's <see cref="JsonStringEnumConverter"/> does not honor
/// <c>[JsonPropertyName]</c> on enum members (that was added in .NET 9), so
/// this converter bridges the gap.
/// </para>
/// </summary>
internal sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(TolerantEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class TolerantEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        // Build a lookup from UPPER_SNAKE_CASE (API value from [JsonPropertyName])
        // and from normalized (no underscores, case-insensitive) to the enum member.
        private static readonly Dictionary<string, TEnum> _apiNameMap = BuildApiNameMap();

        private static Dictionary<string, TEnum> BuildApiNameMap()
        {
            var map = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in typeof(TEnum).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                var value = (TEnum)member.GetValue(null)!;

                // Exact member name
                map.TryAdd(member.Name, value);

                // [JsonPropertyName] attribute value (e.g. "BPMN_ELEMENT")
                var jpn = member.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false);
                if (jpn.Length > 0)
                {
                    var apiName = ((JsonPropertyNameAttribute)jpn[0]).Name;
                    map.TryAdd(apiName, value);
                }
            }
            return map;
        }

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                var intVal = reader.GetInt32();
                return (TEnum)Enum.ToObject(typeof(TEnum), intVal);
            }

            var str = reader.GetString();
            if (str == null)
                return default;

            if (_apiNameMap.TryGetValue(str, out var result))
                return result;

            // Fallback: strip underscores and try case-insensitive parse
            if (Enum.TryParse<TEnum>(str.Replace("_", ""), ignoreCase: true, out result))
                return result;

            return default;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            // Write the [JsonPropertyName] value if available, otherwise the member name
            var name = value.ToString()!;
            var member = typeof(TEnum).GetField(name);
            if (member != null)
            {
                var jpn = member.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false);
                if (jpn.Length > 0)
                {
                    writer.WriteStringValue(((JsonPropertyNameAttribute)jpn[0]).Name);
                    return;
                }
            }

            writer.WriteStringValue(name);
        }
    }
}
