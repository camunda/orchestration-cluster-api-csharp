using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Camunda.Orchestration.Sdk.Runtime;

/// <summary>
/// Marker interface for all Camunda domain key types.
/// Enables generic constraints and JSON converter discovery.
/// </summary>
public interface ICamundaKey
{
    /// <summary>The underlying string value.</summary>
    string Value { get; }
}

/// <summary>
/// JSON converter factory that handles any <see cref="ICamundaKey"/> struct.
/// Serializes as a plain JSON string; deserializes by calling the static AssumeExists factory.
/// </summary>
public sealed class CamundaKeyJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsValueType && typeof(ICamundaKey).IsAssignableFrom(typeToConvert);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(CamundaKeyJsonConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// Generic JSON converter for a specific <see cref="ICamundaKey"/> struct type.
/// </summary>
internal sealed class CamundaKeyJsonConverter<T> : JsonConverter<T> where T : struct, ICamundaKey
{
    private static readonly Func<string, T>? s_factory;

    static CamundaKeyJsonConverter()
    {
        // Find the static AssumeExists(string) method via reflection (once per type)
        var method = typeof(T).GetMethod("AssumeExists",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null, [typeof(string)], null);

        if (method != null)
            s_factory = (Func<string, T>)Delegate.CreateDelegate(typeof(Func<string, T>), method);
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();
        if (raw == null)
            throw new JsonException($"Expected a string value for {typeof(T).Name}, got null.");

        if (s_factory != null)
            return s_factory(raw);

        throw new JsonException($"No AssumeExists factory found on {typeof(T).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>
/// Marker interface for Camunda domain types backed by a long (int64) value.
/// </summary>
public interface ICamundaLongKey
{
    /// <summary>The underlying long value.</summary>
    long Value { get; }
}

/// <summary>
/// JSON converter factory that handles any <see cref="ICamundaLongKey"/> struct.
/// Serializes as a JSON number; deserializes by calling the static AssumeExists factory.
/// </summary>
public sealed class CamundaLongKeyJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsValueType && typeof(ICamundaLongKey).IsAssignableFrom(typeToConvert);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(CamundaLongKeyJsonConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// Generic JSON converter for a specific <see cref="ICamundaLongKey"/> struct type.
/// </summary>
internal sealed class CamundaLongKeyJsonConverter<T> : JsonConverter<T> where T : struct, ICamundaLongKey
{
    private static readonly Func<long, T>? s_factory;

    static CamundaLongKeyJsonConverter()
    {
        var method = typeof(T).GetMethod("AssumeExists",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null, [typeof(long)], null);

        if (method != null)
            s_factory = (Func<long, T>)Delegate.CreateDelegate(typeof(Func<long, T>), method);
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetInt64();

        if (s_factory != null)
            return s_factory(raw);

        throw new JsonException($"No AssumeExists factory found on {typeof(T).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Value);
    }
}

/// <summary>
/// Validation helpers for domain key constraints.
/// </summary>
public static class CamundaKeyValidation
{
    /// <summary>
    /// Validates a value against optional constraints (pattern, minLength, maxLength).
    /// Throws <see cref="ArgumentException"/> if validation fails.
    /// </summary>
    public static void AssertConstraints(string value, string typeName,
        string? pattern = null, int? minLength = null, int? maxLength = null)
    {
        if (minLength.HasValue && value.Length < minLength.Value)
            throw new ArgumentException(
                $"Value for {typeName} must be at least {minLength.Value} characters, got {value.Length}.", nameof(value));

        if (maxLength.HasValue && value.Length > maxLength.Value)
            throw new ArgumentException(
                $"Value for {typeName} must be at most {maxLength.Value} characters, got {value.Length}.", nameof(value));

        if (pattern != null && !Regex.IsMatch(value, pattern))
            throw new ArgumentException(
                $"Value for {typeName} does not match expected pattern '{pattern}'.", nameof(value));
    }

    /// <summary>
    /// Validates a value against optional constraints, returning true if valid.
    /// </summary>
    public static bool CheckConstraints(string value,
        string? pattern = null, int? minLength = null, int? maxLength = null)
    {
        if (minLength.HasValue && value.Length < minLength.Value)
            return false;
        if (maxLength.HasValue && value.Length > maxLength.Value)
            return false;
        if (pattern != null && !Regex.IsMatch(value, pattern))
            return false;
        return true;
    }
}
