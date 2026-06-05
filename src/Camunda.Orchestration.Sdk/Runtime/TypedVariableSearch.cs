using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Camunda.Orchestration.Sdk;

/// <summary>
/// A single declared variable derived from a DTO member.
/// </summary>
/// <param name="MemberName">The CLR member (property) name on the DTO.</param>
/// <param name="VariableName">
/// The variable name to query for and key the result by. This is the member's
/// <see cref="JsonPropertyNameAttribute"/> value when present, otherwise the member name
/// transformed by the serializer's naming policy (camelCase by default).
/// </param>
/// <param name="Required">
/// Whether the member is required for <see cref="VariableMap{T}.Validate"/>: <c>true</c> for
/// non-nullable members or members marked with the <c>required</c> modifier.
/// </param>
internal readonly record struct TypedVariableField(string MemberName, string VariableName, bool Required);

/// <summary>
/// Pure (I/O-free) core of the DTO-driven typed variable map feature.
///
/// <para>
/// Holds the reflection that derives the query from a DTO and the incremental collector that
/// collapses paged results into a bounded per-name map. Keeping this logic free of HTTP makes
/// the memory-bound and scope-collision behaviour directly unit-testable.
/// </para>
/// </summary>
internal static class TypedVariableSearch
{
    /// <summary>
    /// Derive the declared variables from a DTO type.
    /// </summary>
    /// <param name="dtoType">The DTO type to introspect.</param>
    /// <param name="options">
    /// The serializer options whose naming policy is used to resolve member names that do not
    /// carry an explicit <see cref="JsonPropertyNameAttribute"/>. The same options are used to
    /// deserialize the result, so the derived names bind cleanly back onto the DTO.
    /// </param>
    public static IReadOnlyList<TypedVariableField> ExtractFields(Type dtoType, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(dtoType);
        ArgumentNullException.ThrowIfNull(options);

        var nullabilityContext = new NullabilityInfoContext();
        var fields = new List<TypedVariableField>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in dtoType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Only consider readable members the serializer would round-trip.
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
            {
                continue;
            }

            var variableName = ResolveVariableName(property, options);
            if (!seen.Add(variableName))
            {
                throw new TypedVariablesException(
                    $"DTO '{dtoType.Name}' maps more than one member to the variable name "
                    + $"'{variableName}'. Use [JsonPropertyName] to disambiguate.");
            }

            var required = IsRequired(property, nullabilityContext);
            fields.Add(new TypedVariableField(property.Name, variableName, required));
        }

        return fields;
    }

    private static string ResolveVariableName(PropertyInfo property, JsonSerializerOptions options)
    {
        var explicitName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
        if (!string.IsNullOrEmpty(explicitName))
        {
            return explicitName;
        }

        return options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
    }

    private static bool IsRequired(PropertyInfo property, NullabilityInfoContext nullabilityContext)
    {
        // The `required` modifier is an unconditional presence contract.
        if (property.GetCustomAttribute<RequiredMemberAttribute>() is not null)
        {
            return true;
        }

        var propertyType = property.PropertyType;
        if (propertyType.IsValueType)
        {
            // A non-nullable value type cannot represent "absent"; Nullable<T> can.
            return Nullable.GetUnderlyingType(propertyType) is null;
        }

        // Reference type: required only when the nullable annotation says non-nullable.
        // ReadState reflects the declared nullability of the value consumers read; WriteState
        // can be Unknown for get-only members (common in immutable DTOs), which would wrongly
        // treat a non-nullable member as optional.
        return nullabilityContext.Create(property).ReadState == NullabilityState.NotNull;
    }

    /// <summary>
    /// Parse a serialized variable value into a self-contained <see cref="JsonElement"/>.
    /// </summary>
    /// <exception cref="VariableDeserializationException">
    /// When <paramref name="value"/> is not valid JSON.
    /// </exception>
    public static JsonElement ParseValue(string variableName, string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new VariableDeserializationException(variableName, exception);
        }
    }

    /// <summary>
    /// Incrementally collapses paged variable items into a parsed name-to-value map.
    ///
    /// <para>
    /// Memory stays bounded by the DTO shape rather than the total number of paged items: only
    /// the first value seen per requested name is retained, alongside the set of scope keys
    /// observed for that name (used for collision detection). Pages are ingested as they arrive
    /// and discarded, so large values for undeclared variables are never accumulated.
    /// </para>
    /// </summary>
    public sealed class VariableCollector
    {
        private readonly HashSet<string> _queryNames;
        private readonly Dictionary<string, string> _chosenValues = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _scopesSeen = new(StringComparer.Ordinal);

        /// <summary>Create a collector scoped to the declared query names.</summary>
        public VariableCollector(IEnumerable<string> queryNames)
        {
            ArgumentNullException.ThrowIfNull(queryNames);
            _queryNames = new HashSet<string>(queryNames, StringComparer.Ordinal);
        }

        /// <summary>Fold one page of results into the retained per-name state.</summary>
        public void Ingest(IEnumerable<VariableSearchResult> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            foreach (var item in items)
            {
                var name = item.Name;
                if (name is null || !_queryNames.Contains(name))
                {
                    continue;
                }

                if (!_scopesSeen.TryGetValue(name, out var scopes))
                {
                    scopes = new HashSet<string>(StringComparer.Ordinal);
                    _scopesSeen[name] = scopes;
                }

                scopes.Add(item.ScopeKey.ToString());
                _chosenValues.TryAdd(name, item.Value);
            }
        }

        /// <summary>
        /// Parse retained values, raising on scope collisions or malformed JSON.
        /// </summary>
        /// <exception cref="VariableScopeCollisionException">
        /// When a name was observed at more than one scope.
        /// </exception>
        /// <exception cref="VariableDeserializationException">
        /// When a retained value is not valid JSON.
        /// </exception>
        public IReadOnlyDictionary<string, JsonElement> Finalize()
        {
            var raw = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            foreach (var (name, value) in _chosenValues)
            {
                var scopes = _scopesSeen[name];
                if (scopes.Count > 1)
                {
                    var sorted = scopes.OrderBy(static s => s, StringComparer.Ordinal).ToList();
                    throw new VariableScopeCollisionException(name, sorted);
                }

                raw[name] = ParseValue(name, value);
            }

            return raw;
        }
    }
}
