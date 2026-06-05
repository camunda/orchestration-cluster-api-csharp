using System.Text.Json;

namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Result of a DTO-driven variable search (<see cref="CamundaClient.SearchVariablesAsDtoAsync{T}"/>).
///
/// <para>
/// Holds the parsed variable values keyed by their query name (the DTO member's
/// <c>[JsonPropertyName]</c> value, or the member name transformed by the serializer's naming
/// policy). Provides lenient, defensive access via <see cref="Get(string)"/> /
/// <see cref="Get{TValue}(string)"/> and a strict <see cref="Validate"/> that constructs the
/// declared DTO and enforces required members.
/// </para>
/// </summary>
/// <typeparam name="T">The DTO type that declared the variables to fetch.</typeparam>
public sealed class VariableMap<T>
    where T : class
{
    private readonly IReadOnlyDictionary<string, JsonElement> _raw;
    private readonly JsonSerializerOptions _options;

    internal VariableMap(IReadOnlyDictionary<string, JsonElement> raw, JsonSerializerOptions options)
    {
        _raw = raw;
        _options = options;
    }

    /// <summary>The parsed variable values, keyed by variable name.</summary>
    public IReadOnlyDictionary<string, JsonElement> Raw => _raw;

    /// <summary>Whether a variable with the given name is present in the result.</summary>
    public bool Contains(string variableName) => _raw.ContainsKey(variableName);

    /// <summary>
    /// Lenient access. Returns the parsed value as a <see cref="JsonElement"/>, or <c>null</c>
    /// when the variable is absent from the result.
    /// </summary>
    public JsonElement? Get(string variableName)
        => _raw.TryGetValue(variableName, out var value) ? value : null;

    /// <summary>
    /// Lenient, typed access. Deserializes the variable's value into <typeparamref name="TValue"/>,
    /// or returns <c>default</c> when the variable is absent.
    /// </summary>
    /// <exception cref="VariableDeserializationException">
    /// When the present value cannot be deserialized into <typeparamref name="TValue"/>.
    /// </exception>
    public TValue? Get<TValue>(string variableName)
    {
        if (!_raw.TryGetValue(variableName, out var value))
        {
            return default;
        }

        try
        {
            return value.Deserialize<TValue>(_options);
        }
        catch (JsonException exception)
        {
            throw new VariableDeserializationException(variableName, exception);
        }
    }

    /// <summary>
    /// Strict access. Constructs and returns the declared DTO.
    ///
    /// <para>
    /// Required members (non-nullable members, or members marked with the <c>required</c>
    /// modifier) must be present, otherwise a <see cref="VariableValidationException"/> is
    /// thrown listing the missing variables. Optional members are left at their default when
    /// absent.
    /// </para>
    /// </summary>
    /// <exception cref="VariableValidationException">
    /// When one or more required members are absent from the result.
    /// </exception>
    public T Validate()
    {
        var fields = TypedVariableSearch.ExtractFields(typeof(T), _options);

        var missing = fields
            .Where(field => field.Required && !_raw.ContainsKey(field.VariableName))
            .Select(field => field.VariableName)
            .ToList();

        if (missing.Count > 0)
        {
            throw new VariableValidationException(typeof(T), missing);
        }

        // Keys in `_raw` are already the resolved variable names, which match what the
        // serializer expects for binding, so a round-trip through JSON reconstructs the DTO.
        var json = JsonSerializer.SerializeToUtf8Bytes(_raw, _options);
        var result = JsonSerializer.Deserialize<T>(json, _options);

        return result
            ?? throw new VariableValidationException(typeof(T), fields.Select(f => f.VariableName).ToList());
    }
}
