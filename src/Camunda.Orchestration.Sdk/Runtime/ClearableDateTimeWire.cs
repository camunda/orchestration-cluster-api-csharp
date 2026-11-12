using System.Text.Json;
using System.Text.Json.Serialization;

namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Wire form of a clearable date-time property (the specification's <c>x-clearable</c>
/// <c>ClearableDateTime</c>): either a date-time, or an empty string that clears the
/// persisted value. A <c>null</c> or omitted property preserves the persisted value, which
/// the generated models express as a <c>null</c> <see cref="ClearableDateTimeWire"/>.
/// </summary>
/// <remarks>
/// Generated models keep the public property typed <see cref="DateTimeOffset"/>? and expose
/// the clear as <c>Clear&lt;Property&gt;()</c> / <c>Is&lt;Property&gt;Cleared</c>; this type is
/// only the internal, JSON-bound representation of that pair.
/// </remarks>
[JsonConverter(typeof(ClearableDateTimeWireJsonConverter))]
internal readonly struct ClearableDateTimeWire
{
    private ClearableDateTimeWire(DateTimeOffset? value, bool isCleared)
    {
        Value = value;
        IsCleared = isCleared;
    }

    /// <summary>The date-time to send, or <c>null</c> when the value is cleared.</summary>
    public DateTimeOffset? Value { get; }

    /// <summary>True when the value is sent as <c>""</c>, which clears it.</summary>
    public bool IsCleared { get; }

    /// <summary>A clear: serialised as <c>""</c>.</summary>
    public static ClearableDateTimeWire Cleared => new(null, true);

    /// <summary>No value: neither a date nor a clear, so the persisted value is preserved.</summary>
    public static ClearableDateTimeWire Preserved => new(null, false);

    /// <summary>A date-time: serialised exactly as a <see cref="DateTimeOffset"/>?.</summary>
    public static ClearableDateTimeWire Of(DateTimeOffset value) => new(value, false);

    /// <summary>
    /// The wire value for a model's (value, cleared) pair: a clear wins, a date is sent as-is,
    /// and neither yields <c>null</c> (omitted / preserved).
    /// </summary>
    public static ClearableDateTimeWire? From(DateTimeOffset? value, bool isCleared) =>
        isCleared ? Cleared : value is { } v ? Of(v) : null;
}

/// <summary>
/// Writes a <see cref="ClearableDateTimeWire"/> as <c>""</c> when cleared and reads <c>""</c>
/// back as a clear. Every other value is delegated as a <see cref="DateTimeOffset"/>?, the type
/// these properties had before they became clearable, so whatever converter the options resolve
/// for it (a caller's <c>JsonConverter&lt;DateTimeOffset?&gt;</c>, a lifted
/// <c>JsonConverter&lt;DateTimeOffset&gt;</c>, or the default) handles a set date exactly as it
/// did. A <c>null</c> that converter reads is "no value", which preserves the persisted value.
/// </summary>
internal sealed class ClearableDateTimeWireJsonConverter : JsonConverter<ClearableDateTimeWire>
{
    public override ClearableDateTimeWire Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && (reader.HasValueSequence ? reader.ValueSequence.IsEmpty : reader.ValueSpan.IsEmpty))
        {
            return ClearableDateTimeWire.Cleared;
        }

        return JsonSerializer.Deserialize<DateTimeOffset?>(ref reader, options) is { } date
            ? ClearableDateTimeWire.Of(date)
            : ClearableDateTimeWire.Preserved;
    }

    public override void Write(Utf8JsonWriter writer, ClearableDateTimeWire value, JsonSerializerOptions options)
    {
        if (value.IsCleared)
        {
            writer.WriteStringValue(string.Empty);
            return;
        }

        JsonSerializer.Serialize(writer, value.Value, options);
    }
}
