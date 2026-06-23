using Camunda.Orchestration.Sdk.Generator;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class regression guard for <c>allOf</c> flattening.
///
/// The defect: the generator merged a referenced schema's <em>direct</em>
/// properties into a composing class but did not recurse into that referenced
/// schema's own <c>allOf</c>. So a schema shaped as
/// <c>Filter = allOf[Fields] + {$or}</c>, where <c>Fields = allOf[BaseFields] + {…}</c>,
/// silently dropped every property contributed by <c>BaseFields</c>. This is the
/// exact shape of the orchestration-cluster filter schemas (e.g.
/// <c>ProcessInstanceFilter → ProcessInstanceFilterFields → BaseProcessInstanceFilterFields</c>),
/// which is why <c>tenantId</c>, <c>startDate</c>, <c>state</c>, … were missing
/// from <c>ProcessInstanceFilter</c> (see issue #265).
///
/// The test targets the defect <em>class</em>: any class composed via <c>allOf</c>
/// of an <c>allOf</c>-based schema must carry the transitively-inherited members
/// (and their <c>required</c> flag), at arbitrary nesting depth.
/// </summary>
public class GeneratorAllOfFlattenTests
{
    // Mirrors the real Filter/Fields/BaseFields shape, plus a third nesting
    // level (Root → Mid → BaseFields) to prove the flatten is transitive and
    // not merely two-deep.
    //
    //   BaseFilterFields : { baseField (required), baseTenant }
    //   MidFilterFields  : allOf[BaseFilterFields] + { midField }
    //   FilterFields     : allOf[MidFilterFields]  + { ownField }
    //   Filter           : allOf[FilterFields]     + { $or: FilterFields[] }
    private const string NestedAllOfSpec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Test", "version": "1.0.0" },
          "paths": {
            "/filter": {
              "get": {
                "operationId": "searchFilter",
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Filter" } } }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "BaseFilterFields": {
                "type": "object",
                "required": ["baseField"],
                "properties": {
                  "baseField": { "type": "string" },
                  "baseTenant": { "type": "string" }
                }
              },
              "MidFilterFields": {
                "type": "object",
                "allOf": [ { "$ref": "#/components/schemas/BaseFilterFields" } ],
                "properties": { "midField": { "type": "string" } }
              },
              "FilterFields": {
                "type": "object",
                "allOf": [ { "$ref": "#/components/schemas/MidFilterFields" } ],
                "properties": { "ownField": { "type": "string" } }
              },
              "Filter": {
                "type": "object",
                "allOf": [
                  { "$ref": "#/components/schemas/FilterFields" },
                  {
                    "type": "object",
                    "properties": {
                      "$or": { "type": "array", "items": { "$ref": "#/components/schemas/FilterFields" } }
                    }
                  }
                ]
              }
            }
          }
        }
        """;

    [Fact]
    public void Model_class_flattens_transitively_inherited_allOf_members()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, NestedAllOfSpec);
        File.WriteAllText(tmp.MetadataPath, MinimalBundledSpec.MinimalMetadata);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);

        var models = File.ReadAllText(Path.Combine(tmp.OutputDir, "Models.Generated.cs"));
        var root = CSharpSyntaxTree.ParseText(models).GetCompilationUnitRoot();

        // FilterFields is itself a two-level chain (FilterFields → MidFilterFields
        // → BaseFilterFields), so it already exercises the recursion: BaseField is
        // contributed two hops away and must still appear.
        var fields = PropertiesOf(root, "FilterFields");
        Assert.Contains("OwnField", fields.Keys);
        Assert.Contains("MidField", fields.Keys);
        Assert.Contains("BaseField", fields.Keys);

        // The defect: Filter composes FilterFields (itself an allOf chain), so it
        // must expose every transitively-inherited member — not just FilterFields'
        // direct property. On the unfixed generator BaseField/BaseTenant/MidField
        // are absent.
        var filter = PropertiesOf(root, "Filter");
        Assert.Contains("OwnField", filter.Keys);   // FilterFields direct (1 level)
        Assert.Contains("MidField", filter.Keys);    // MidFilterFields (2 levels)
        Assert.Contains("BaseField", filter.Keys);   // BaseFilterFields (3 levels)
        Assert.Contains("BaseTenant", filter.Keys);  // BaseFilterFields (3 levels)
        Assert.Contains("Or", filter.Keys);          // inline allOf fragment

        // required must also propagate transitively: baseField is required on
        // BaseFilterFields, so on Filter it is a non-nullable reference type
        // (emitted without a trailing '?').
        Assert.Equal("string", filter["BaseField"]);
        Assert.Equal("string?", filter["BaseTenant"]);
    }

    /// <summary>
    /// Returns a map of property name → declared C# type string for the named
    /// class in the parsed compilation unit.
    /// </summary>
    private static Dictionary<string, string> PropertiesOf(CompilationUnitSyntax root, string className)
    {
        var cls = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .SingleOrDefault(c => c.Identifier.ValueText == className);
        Assert.True(cls != null, $"Expected a generated class named '{className}'.");

        return cls!.Members
            .OfType<PropertyDeclarationSyntax>()
            .ToDictionary(p => p.Identifier.ValueText, p => p.Type.ToString());
    }

    private sealed class TempSpecDir : IDisposable
    {
        public string Root { get; }
        public string SpecPath => Path.Combine(Root, "spec.json");
        public string MetadataPath => Path.Combine(Root, "metadata.json");
        public string OutputDir => Path.Combine(Root, "out");

        private TempSpecDir(string root)
        {
            Root = root;
            Directory.CreateDirectory(OutputDir);
        }

        public static TempSpecDir Create()
        {
            var dir = Path.Combine(Path.GetTempPath(), "csharp-gen-allof-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return new TempSpecDir(dir);
        }

        public void Dispose()
        {
            try
            { Directory.Delete(Root, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}
