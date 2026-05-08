using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Camunda.Orchestration.Sdk.Generator;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class test: every inline string enum property in the OpenAPI spec
/// must produce a typed C# enum property in the generated SDK — never a bare
/// <c>string</c>. This guards the entire class of properties, not just known
/// instances, so new inline enums added in future spec updates are automatically
/// covered.
/// </summary>
public class InlineEnumPropertyTests
{
    private static readonly Assembly s_sdkAssembly = typeof(CamundaClient).Assembly;

    /// <summary>
    /// Yields (schemaName, propName) for every inline string enum
    /// property discovered in the bundled OpenAPI spec.
    /// </summary>
    public static IEnumerable<object[]> InlineStringEnumProperties()
    {
        var spec = LoadBundledSpec();
        var schemas = spec["components"]!["schemas"]!.AsObject();

        foreach (var (schemaName, schemaDef) in schemas)
        {
            if (schemaDef is not JsonObject schemaObj)
                continue;

            // Collect properties from the schema itself and from allOf composition
            var properties = new Dictionary<string, JsonNode>();

            if (schemaObj["properties"] is JsonObject directProps)
            {
                foreach (var (propName, propDef) in directProps)
                {
                    if (propDef != null)
                        properties[propName] = propDef;
                }
            }

            if (schemaObj["allOf"] is JsonArray allOfArray)
            {
                foreach (var allOfItem in allOfArray)
                {
                    if (allOfItem is not JsonObject allOfObj)
                        continue;

                    // Resolve $ref in allOf items
                    JsonObject? propsSource = null;
                    if (allOfObj["$ref"]?.GetValue<string>() is string refPath)
                    {
                        var refName = refPath.Split('/')[^1];
                        if (schemas[refName] is JsonObject refSchema
                            && refSchema["properties"] is JsonObject refProps)
                        {
                            propsSource = refProps;
                        }
                    }
                    else if (allOfObj["properties"] is JsonObject inlineProps)
                    {
                        propsSource = inlineProps;
                    }

                    if (propsSource != null)
                    {
                        foreach (var (propName, propDef) in propsSource)
                        {
                            if (propDef != null)
                                properties.TryAdd(propName, propDef);
                        }
                    }
                }
            }

            foreach (var (propName, propDef) in properties)
            {
                if (propDef is not JsonObject propObj)
                    continue;

                // Only inline enums: type=string + enum=[...] + no $ref
                var typeStr = propObj["type"]?.GetValue<string>();
                var enumArr = propObj["enum"] as JsonArray;
                var hasRef = propObj["$ref"] != null;

                if (typeStr == "string" && enumArr is { Count: > 0 } && !hasRef)
                {
                    yield return [schemaName, propName];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(InlineStringEnumProperties))]
    public void InlineStringEnum_GeneratesTypedEnumProperty(string schemaName, string propName)
    {
        // Find the C# class for the schema
        var csharpTypeName = $"Camunda.Orchestration.Sdk.{CSharpClientGenerator.SanitizeTypeName(schemaName)}";
        var type = s_sdkAssembly.GetType(csharpTypeName);
        Assert.True(type != null, $"Expected C# type {csharpTypeName} for schema {schemaName}");

        // Find the property on the type
        var csharpPropName = CSharpClientGenerator.ToPascalCase(propName);
        var prop = type.GetProperty(csharpPropName, BindingFlags.Public | BindingFlags.Instance);
        Assert.True(prop != null,
            $"{csharpTypeName}.{csharpPropName} not found (JSON property: {propName})");

        // The property type must be an enum (or nullable enum), NOT string
        var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        Assert.True(propType.IsEnum,
            $"{csharpTypeName}.{csharpPropName} should be a typed enum but is {prop.PropertyType.Name}. " +
            $"Inline string enum properties must not fall through as bare strings.");
    }

    [Fact]
    public void InlineStringEnumProperties_AreNotEmpty()
    {
        // Sanity check: the test data source should yield at least the known 45 properties.
        // If this fails, the spec or the data source logic has changed in a breaking way.
        var count = InlineStringEnumProperties().Count();
        Assert.True(count >= 45,
            $"Expected at least 45 inline string enum properties in the spec but found {count}. " +
            $"If properties were removed from the spec, update this threshold.");
    }

    [Fact]
    public void AllInlineEnums_HaveJsonStringEnumConverterAttribute()
    {
        // Derive inline enum types from reflection on actual properties
        // rather than re-encoding the naming convention (handles disambiguation).
        var inlineEnumTypes = GetInlineEnumTypesFromReflection();

        foreach (var enumType in inlineEnumTypes)
        {
            var attr = enumType.GetCustomAttribute<JsonConverterAttribute>();
            Assert.True(attr != null,
                $"{enumType.FullName} is missing [JsonConverter(typeof(JsonStringEnumConverter))]");
            Assert.True(attr.ConverterType == typeof(JsonStringEnumConverter),
                $"{enumType.FullName} has [JsonConverter] but converter type is " +
                $"{attr.ConverterType?.Name}, expected JsonStringEnumConverter");
        }
    }

    [Fact]
    public void AllInlineEnumMembers_HaveJsonPropertyNameAttribute()
    {
        // Derive inline enum types from reflection on actual properties.
        var inlineEnumTypes = GetInlineEnumTypesFromReflection();

        // Load spec to verify wire values match
        var spec = LoadBundledSpec();
        var schemas = spec["components"]!["schemas"]!.AsObject();

        foreach (var enumType in inlineEnumTypes)
        {
            foreach (var member in Enum.GetNames(enumType))
            {
                var field = enumType.GetField(member);
                Assert.True(field != null, $"{enumType.FullName}.{member} field not found");
                var attr = field.GetCustomAttribute<JsonPropertyNameAttribute>();
                Assert.True(attr != null,
                    $"{enumType.FullName}.{member} is missing [JsonPropertyName] — wire format will not match spec");
            }
        }
    }

    [Fact]
    public void AllInlineEnumMembers_MatchSpecWireValues()
    {
        // Verify that [JsonPropertyName] values match the spec enum values exactly
        var spec = LoadBundledSpec();
        var schemas = spec["components"]!["schemas"]!.AsObject();

        foreach (var row in InlineStringEnumProperties())
        {
            var schemaName = (string)row[0];
            var propName = (string)row[1];

            // Resolve spec enum values
            var specEnumValues = GetSpecEnumValues(schemas, schemaName, propName);
            if (specEnumValues == null)
                continue;

            // Find the reflected enum type via the property
            var csharpTypeName = $"Camunda.Orchestration.Sdk.{CSharpClientGenerator.SanitizeTypeName(schemaName)}";
            var type = s_sdkAssembly.GetType(csharpTypeName);
            if (type == null)
                continue;

            var csharpPropName = CSharpClientGenerator.ToPascalCase(propName);
            var prop = type.GetProperty(csharpPropName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
                continue;

            var enumType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (!enumType.IsEnum)
                continue;

            // Collect wire values from [JsonPropertyName] attributes
            var wireValues = new HashSet<string>();
            foreach (var member in Enum.GetNames(enumType))
            {
                var field = enumType.GetField(member);
                var attr = field?.GetCustomAttribute<JsonPropertyNameAttribute>();
                if (attr != null)
                    wireValues.Add(attr.Name);
            }

            // Every spec value should have a matching [JsonPropertyName]
            foreach (var specValue in specEnumValues)
            {
                Assert.True(wireValues.Contains(specValue),
                    $"{enumType.FullName} is missing member with [JsonPropertyName(\"{specValue}\")] " +
                    $"(schema: {schemaName}, property: {propName})");
            }
        }
    }

    /// <summary>
    /// Derives inline enum types by reflecting on the properties discovered by the
    /// Theory data source — avoids re-encoding the generator's naming convention.
    /// </summary>
    private static HashSet<Type> GetInlineEnumTypesFromReflection()
    {
        var enumTypes = new HashSet<Type>();
        foreach (var row in InlineStringEnumProperties())
        {
            var schemaName = (string)row[0];
            var propName = (string)row[1];

            var csharpTypeName = $"Camunda.Orchestration.Sdk.{CSharpClientGenerator.SanitizeTypeName(schemaName)}";
            var type = s_sdkAssembly.GetType(csharpTypeName);
            if (type == null)
                continue;

            var csharpPropName = CSharpClientGenerator.ToPascalCase(propName);
            var prop = type.GetProperty(csharpPropName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
                continue;

            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (propType.IsEnum)
                enumTypes.Add(propType);
        }
        Assert.True(enumTypes.Count > 0, "No inline enum types found via reflection");
        return enumTypes;
    }

    private static List<string>? GetSpecEnumValues(JsonObject schemas, string schemaName, string propName)
    {
        if (schemas[schemaName] is not JsonObject schemaObj)
            return null;

        // Direct properties
        if (schemaObj["properties"] is JsonObject props && props[propName] is JsonObject propObj)
        {
            if (propObj["enum"] is JsonArray arr)
                return arr.Select(e => e?.GetValue<string>() ?? "").ToList();
        }

        // allOf composition
        if (schemaObj["allOf"] is JsonArray allOf)
        {
            foreach (var item in allOf)
            {
                if (item is not JsonObject itemObj)
                    continue;

                JsonObject? source = null;
                if (itemObj["$ref"]?.GetValue<string>() is string refPath)
                {
                    var refName = refPath.Split('/')[^1];
                    if (schemas[refName] is JsonObject refSchema)
                        source = refSchema["properties"] as JsonObject;
                }
                else
                {
                    source = itemObj["properties"] as JsonObject;
                }

                if (source?[propName] is JsonObject refPropObj && refPropObj["enum"] is JsonArray refArr)
                    return refArr.Select(e => e?.GetValue<string>() ?? "").ToList();
            }
        }

        return null;
    }

    private static JsonNode LoadBundledSpec()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "external-spec", "bundled", "rest-api.bundle.json");
            if (File.Exists(candidate))
                return JsonNode.Parse(File.ReadAllText(candidate))!;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Could not locate external-spec/bundled/rest-api.bundle.json");
    }
}
