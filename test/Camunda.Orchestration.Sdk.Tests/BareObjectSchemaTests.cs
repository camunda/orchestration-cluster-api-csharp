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
    /// Class-scoped sweep: no generated model in the current SDK should be
    /// an empty sealed class. If a response schema has no properties, it
    /// should not have been emitted as a class at all.
    /// </summary>
    [Fact]
    public void GeneratedModels_NoEmptyClasses()
    {
        var modelsPath = Path.Combine(
            FindRepoRoot(),
            "src", "Camunda.Orchestration.Sdk", "Generated", "Models.Generated.cs");

        Assert.True(File.Exists(modelsPath), $"Models.Generated.cs not found at {modelsPath}");
        var content = File.ReadAllText(modelsPath);

        // Find all sealed classes that are immediately closed (empty body).
        var emptyClassPattern = new Regex(
            @"public sealed class (\w+)\s*\{\s*\}",
            RegexOptions.Multiline);

        var matches = emptyClassPattern.Matches(content);
        if (matches.Count > 0)
        {
            var names = string.Join(", ", matches.Cast<Match>().Select(m => m.Groups[1].Value));
            Assert.Fail(
                $"Found {matches.Count} empty class(es) in Models.Generated.cs: {names}. " +
                "Bare 'type: object' schemas should map to 'object' or 'Dictionary<string, ...>', not empty classes.");
        }
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
