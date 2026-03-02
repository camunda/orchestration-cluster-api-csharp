using System.Text.Json;
using System.Text.Json.Serialization;

namespace Camunda.Orchestration.Sdk.Generator;

/// <summary>
/// Deserialized representation of spec-metadata.json produced by camunda-schema-bundler.
/// Provides pre-computed behavioral flags so the generator doesn't need to re-derive them
/// from the raw OpenAPI spec.
/// </summary>
internal sealed class SpecMetadata
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "";

    [JsonPropertyName("specHash")]
    public string SpecHash { get; set; } = "";

    [JsonPropertyName("semanticKeys")]
    public List<SemanticKeyMeta> SemanticKeys { get; set; } = [];

    [JsonPropertyName("operations")]
    public List<OperationMetadataEntry> Operations { get; set; } = [];

    [JsonPropertyName("deprecatedEnumMembers")]
    public List<DeprecatedEnumSchemaEntry> DeprecatedEnumMembers { get; set; } = [];

    [JsonPropertyName("integrity")]
    public IntegrityInfo? Integrity { get; set; }

    // ── Lookup helpers ──

    private Dictionary<string, OperationMetadataEntry>? _opLookup;
    private HashSet<string>? _semanticKeyNames;

    public OperationMetadataEntry? GetOperation(string operationId)
    {
        _opLookup ??= Operations.ToDictionary(o => o.OperationId, o => o);
        return _opLookup.GetValueOrDefault(operationId);
    }

    public bool IsSemanticKey(string schemaName)
    {
        _semanticKeyNames ??= new HashSet<string>(SemanticKeys.Select(k => k.Name));
        return _semanticKeyNames.Contains(schemaName);
    }

    private Dictionary<string, Dictionary<string, string>>? _deprecatedEnumLookup;

    /// <summary>
    /// Returns the deprecatedInVersion string if the given enum member is deprecated, otherwise null.
    /// </summary>
    public string? GetDeprecatedEnumMemberVersion(string schemaName, string memberValue)
    {
        if (_deprecatedEnumLookup == null)
        {
            _deprecatedEnumLookup = new Dictionary<string, Dictionary<string, string>>();
            foreach (var entry in DeprecatedEnumMembers)
            {
                var members = new Dictionary<string, string>();
                foreach (var m in entry.DeprecatedMembers)
                {
                    members[m.Name] = m.DeprecatedInVersion;
                }
                _deprecatedEnumLookup[entry.SchemaName] = members;
            }
        }
        if (_deprecatedEnumLookup.TryGetValue(schemaName, out var lookup)
            && lookup.TryGetValue(memberValue, out var version))
        {
            return version;
        }
        return null;
    }

    public static SpecMetadata Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SpecMetadata>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize spec metadata from {path}");
    }
}

internal sealed class SemanticKeyMeta
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("semanticType")]
    public string SemanticType { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("constraints")]
    public ConstraintsMeta? Constraints { get; set; }

    [JsonPropertyName("flags")]
    public SemanticKeyFlags? Flags { get; set; }
}

internal sealed class ConstraintsMeta
{
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    [JsonPropertyName("minLength")]
    public int? MinLength { get; set; }

    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }
}

internal sealed class SemanticKeyFlags
{
    [JsonPropertyName("semanticKey")]
    public bool SemanticKey { get; set; }

    [JsonPropertyName("includesLongKeyRef")]
    public bool IncludesLongKeyRef { get; set; }

    [JsonPropertyName("deprecated")]
    public bool Deprecated { get; set; }
}

internal sealed class OperationMetadataEntry
{
    [JsonPropertyName("operationId")]
    public string OperationId { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("eventuallyConsistent")]
    public bool EventuallyConsistent { get; set; }

    [JsonPropertyName("hasRequestBody")]
    public bool HasRequestBody { get; set; }

    [JsonPropertyName("requestBodyUnion")]
    public bool RequestBodyUnion { get; set; }

    [JsonPropertyName("bodyOnly")]
    public bool BodyOnly { get; set; }

    [JsonPropertyName("pathParams")]
    public List<string> PathParams { get; set; } = [];

    [JsonPropertyName("queryParams")]
    public List<QueryParamMeta> QueryParams { get; set; } = [];

    [JsonPropertyName("requestBodyUnionRefs")]
    public List<string> RequestBodyUnionRefs { get; set; } = [];

    [JsonPropertyName("optionalTenantIdInBody")]
    public bool OptionalTenantIdInBody { get; set; }
}

internal sealed class QueryParamMeta
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

internal sealed class IntegrityInfo
{
    [JsonPropertyName("totalSemanticKeys")]
    public int TotalSemanticKeys { get; set; }

    [JsonPropertyName("totalUnions")]
    public int TotalUnions { get; set; }

    [JsonPropertyName("totalOperations")]
    public int TotalOperations { get; set; }

    [JsonPropertyName("totalEventuallyConsistent")]
    public int TotalEventuallyConsistent { get; set; }
}

internal sealed class DeprecatedEnumSchemaEntry
{
    [JsonPropertyName("schemaName")]
    public string SchemaName { get; set; } = "";

    [JsonPropertyName("enumValues")]
    public List<string> EnumValues { get; set; } = [];

    [JsonPropertyName("deprecatedMembers")]
    public List<DeprecatedEnumMemberEntry> DeprecatedMembers { get; set; } = [];
}

internal sealed class DeprecatedEnumMemberEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("deprecatedInVersion")]
    public string DeprecatedInVersion { get; set; } = "";
}
