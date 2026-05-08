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
                    if (allOfItem is JsonObject allOfObj && allOfObj["properties"] is JsonObject allOfProps)
                    {
                        foreach (var (propName, propDef) in allOfProps)
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
        // Every inline enum type should have [JsonConverter(typeof(JsonStringEnumConverter))]
        var inlineEnumTypeNames = InlineStringEnumProperties()
            .Select(row => $"{CSharpClientGenerator.SanitizeTypeName((string)row[0])}{CSharpClientGenerator.ToPascalCase((string)row[1])}")
            .Distinct()
            .ToList();

        foreach (var enumName in inlineEnumTypeNames)
        {
            var fullName = $"Camunda.Orchestration.Sdk.{enumName}";
            var type = s_sdkAssembly.GetType(fullName);
            Assert.True(type != null, $"Expected inline enum type {fullName}");

            var attr = type.GetCustomAttribute<JsonConverterAttribute>();
            Assert.True(attr != null,
                $"{fullName} is missing [JsonConverter(typeof(JsonStringEnumConverter))]");
        }
    }

    [Fact]
    public void AllInlineEnumMembers_HaveJsonPropertyNameAttribute()
    {
        // Every member of every inline enum must have [JsonPropertyName] for wire format fidelity
        var inlineEnumTypeNames = InlineStringEnumProperties()
            .Select(row => $"{CSharpClientGenerator.SanitizeTypeName((string)row[0])}{CSharpClientGenerator.ToPascalCase((string)row[1])}")
            .Distinct()
            .ToList();

        foreach (var enumName in inlineEnumTypeNames)
        {
            var fullName = $"Camunda.Orchestration.Sdk.{enumName}";
            var type = s_sdkAssembly.GetType(fullName);
            Assert.True(type != null, $"Expected inline enum type {fullName}");

            foreach (var member in Enum.GetNames(type))
            {
                var field = type.GetField(member);
                Assert.True(field != null, $"{fullName}.{member} field not found");
                var attr = field.GetCustomAttribute<JsonPropertyNameAttribute>();
                Assert.True(attr != null,
                    $"{fullName}.{member} is missing [JsonPropertyName] — wire format will not match spec");
            }
        }
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
