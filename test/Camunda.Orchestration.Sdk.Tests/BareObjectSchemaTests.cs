using System.Text.Json;
using System.Text.RegularExpressions;
using Camunda.Orchestration.Sdk.Generator;
using Microsoft.OpenApi;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class regression guard: bare <c>type: object</c> schemas with no
/// properties must not produce empty model classes. They should map to
/// <c>object</c> (unconstrained) or <c>Dictionary&lt;string, …&gt;</c>
/// (when <c>additionalProperties</c> is set).
///
/// Covers the class of defects where an inline response schema like
/// <c>{ "type": "object" }</c> causes the generator to emit a useless
/// empty <c>sealed class FooResponse { }</c>.
/// </summary>
public class BareObjectSchemaTests
{
    // ── Unit tests: MapType type resolution ──

    [Fact]
    public void MapType_BareObjectSchema_ReturnsObject()
    {
        // A schema with only { "type": "object" } and no properties
        // should map to "object", not trigger an inline class.
        var schema = new OpenApiSchema { Type = JsonSchemaType.Object };

        var result = CSharpClientGenerator.MapType(schema);

        Assert.Equal("object", result);
    }

    [Fact]
    public void MapType_ObjectWithAdditionalProperties_ReturnsDictionary()
    {
        // { "type": "object", "additionalProperties": true }
        // should map to Dictionary<string, object>.
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            AdditionalProperties = new OpenApiSchema(), // empty schema = any value
        };

        var result = CSharpClientGenerator.MapType(schema);

        Assert.Equal("Dictionary<string, object>", result);
    }

    [Fact]
    public void MapType_ObjectWithTypedAdditionalProperties_ReturnsDictionaryOfType()
    {
        // { "type": "object", "additionalProperties": { "type": "string" } }
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.String },
        };

        var result = CSharpClientGenerator.MapType(schema);

        Assert.Equal("Dictionary<string, string>", result);
    }

    /// <summary>
    /// Class-scoped sweep: no generated model may be an empty sealed class
    /// <em>unless</em> the spec deliberately declares it sealed and empty
    /// (<c>additionalProperties: false</c> with no properties).
    ///
    /// The defect this guards is a free-form <c>{ "type": "object" }</c> schema
    /// producing a useless empty class instead of mapping to <c>object</c> /
    /// <c>Dictionary&lt;string, …&gt;</c>. A named component that explicitly
    /// forbids additional properties is a different thing: an intentionally
    /// empty contract (e.g. a request body reserved for future filters). Those
    /// stay classes on purpose — when the spec later adds properties, callers
    /// keep compiling, whereas an <c>object</c> parameter would have to change
    /// type and break them.
    /// </summary>
    [Fact]
    public void GeneratedModels_NoEmptyClasses_ExceptDeliberatelySealedSchemas()
    {
        var repoRoot = FindRepoRoot();
        var modelsPath = Path.Combine(
            repoRoot, "src", "Camunda.Orchestration.Sdk", "Generated", "Models.Generated.cs");

        Assert.True(File.Exists(modelsPath), $"Models.Generated.cs not found at {modelsPath}");
        var content = File.ReadAllText(modelsPath);

        // Find all sealed classes that are immediately closed (empty body).
        var emptyClassPattern = new Regex(
            @"public sealed class (\w+)\s*\{\s*\}",
            RegexOptions.Multiline);

        var sealedEmptySchemas = LoadDeliberatelySealedEmptySchemas(repoRoot);

        var offenders = emptyClassPattern.Matches(content)
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Where(name => !sealedEmptySchemas.Contains(name))
            .ToList();

        if (offenders.Count > 0)
        {
            Assert.Fail(
                $"Found {offenders.Count} empty class(es) in Models.Generated.cs: {string.Join(", ", offenders)}. " +
                "Free-form 'type: object' schemas should map to 'object' or 'Dictionary<string, ...>', not empty classes. " +
                "Only components that declare 'additionalProperties: false' with no properties may be emitted as empty classes.");
        }
    }

    /// <summary>
    /// Names of component schemas the bundled spec declares as objects with no
    /// properties and <c>additionalProperties: false</c> — intentionally empty
    /// contracts rather than free-form objects. The spec is parsed once per
    /// call so reporting many offenders does not re-read it per candidate.
    /// Anything not in this set (including inline schemas the generator
    /// materialised) is treated as the defect this test guards.
    /// </summary>
    private static HashSet<string> LoadDeliberatelySealedEmptySchemas(string repoRoot)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        var specPath = Path.Combine(repoRoot, "external-spec", "bundled", "rest-api.bundle.json");
        if (!File.Exists(specPath))
            return result;

        using var doc = JsonDocument.Parse(File.ReadAllText(specPath));
        if (!doc.RootElement.TryGetProperty("components", out var components) ||
            !components.TryGetProperty("schemas", out var schemas))
        {
            return result;
        }

        foreach (var entry in schemas.EnumerateObject())
        {
            var schema = entry.Value;
            if (schema.ValueKind != JsonValueKind.Object)
                continue;

            var isObject = schema.TryGetProperty("type", out var type) &&
                           type.ValueKind == JsonValueKind.String &&
                           type.GetString() == "object";

            var hasNoProperties = !schema.TryGetProperty("properties", out var props) ||
                                  !props.EnumerateObject().Any();

            var sealsAdditional = schema.TryGetProperty("additionalProperties", out var addl) &&
                                  addl.ValueKind == JsonValueKind.False;

            var hasNoComposition = !schema.TryGetProperty("allOf", out _) &&
                                   !schema.TryGetProperty("oneOf", out _) &&
                                   !schema.TryGetProperty("anyOf", out _);

            if (isObject && hasNoProperties && sealsAdditional && hasNoComposition)
                result.Add(entry.Name);
        }

        return result;
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Camunda.Orchestration.Sdk.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Cannot find repo root");
    }
}
