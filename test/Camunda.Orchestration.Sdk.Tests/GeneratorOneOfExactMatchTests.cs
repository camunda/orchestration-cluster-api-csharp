using Camunda.Orchestration.Sdk.Generator;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class regression guard for advanced-filter <c>oneOf</c> properties.
///
/// The Camunda search API models a filterable field as
/// <c>XFilterProperty = oneOf[ XExactMatch (a bare scalar), AdvancedXFilter ({ $eq, $in, … }) ]</c>,
/// so the wire accepts <em>either</em> a bare value (<c>"k": "123"</c>) or the
/// operator object (<c>"k": { "$eq": "123" }</c>). The bare/exact-match form is
/// also the shape older servers (that predate advanced filtering on that field)
/// expect.
///
/// The defect: the generator collapsed the <c>oneOf</c> to the non-primitive
/// (advanced) variant only, dropping the exact-match (scalar) branch entirely.
/// The generated type therefore exposed just <c>Eq</c>/<c>In</c>/… and there was
/// no way to send the bare form — which both hurts ergonomics and breaks
/// newer-SDK → older-server compatibility for these fields (see #267).
///
/// The fix targets the defect <em>class</em>: any <c>oneOf[scalar | advanced]</c>
/// filter property must also surface the exact-match branch — an
/// <c>ExactMatch</c> member, an implicit conversion from the value type, and a
/// converter that serialises the bare scalar when only the exact match is set.
/// </summary>
public class GeneratorOneOfExactMatchTests
{
    //   MyKey               : string  (semantic-key-like domain struct)
    //   MyKeyExactMatch     : string + allOf[MyKey]   (the bare/scalar branch)
    //   AdvancedMyKeyFilter : object { $eq: MyKey, $in: MyKey[] }
    //   MyKeyFilterProperty : oneOf[ MyKeyExactMatch, AdvancedMyKeyFilter ]
    private const string OneOfExactMatchSpec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Test", "version": "1.0.0" },
          "paths": {
            "/thing": {
              "get": {
                "operationId": "getThing",
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Thing" } } }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "MyKey": { "type": "string" },
              "MyKeyExactMatch": {
                "type": "string",
                "title": "Exact match",
                "allOf": [ { "$ref": "#/components/schemas/MyKey" } ]
              },
              "AdvancedMyKeyFilter": {
                "type": "object",
                "properties": {
                  "$eq":  { "allOf": [ { "$ref": "#/components/schemas/MyKey" } ] },
                  "$in":  { "type": "array", "items": { "$ref": "#/components/schemas/MyKey" } }
                }
              },
              "MyKeyFilterProperty": {
                "description": "MyKey property with full advanced search capabilities.",
                "oneOf": [
                  { "$ref": "#/components/schemas/MyKeyExactMatch" },
                  { "$ref": "#/components/schemas/AdvancedMyKeyFilter" }
                ]
              },
              "Thing": {
                "type": "object",
                "properties": {
                  "key": {
                    "allOf": [ { "$ref": "#/components/schemas/MyKeyFilterProperty" } ],
                    "description": "the key"
                  }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public void Filter_property_oneOf_exposes_exact_match_scalar_branch()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, OneOfExactMatchSpec);
        File.WriteAllText(tmp.MetadataPath, MinimalBundledSpec.MinimalMetadata);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);
        var models = File.ReadAllText(Path.Combine(tmp.OutputDir, "Models.Generated.cs"));
        var root = CSharpSyntaxTree.ParseText(models).GetCompilationUnitRoot();

        var filter = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .SingleOrDefault(c => c.Identifier.ValueText == "MyKeyFilterProperty");
        Assert.True(filter != null, "Expected a generated MyKeyFilterProperty class.");

        var props = filter!.Members.OfType<PropertyDeclarationSyntax>()
            .ToDictionary(p => p.Identifier.ValueText, p => p.Type.ToString());

        // The advanced operators must still be present (existing behaviour).
        Assert.Contains("Eq", props.Keys);
        Assert.Contains("In", props.Keys);

        // The defect: the exact-match (scalar) branch was dropped. It must be
        // surfaced as an ExactMatch member typed as the value type (MyKey), so a
        // bare value can be expressed at all.
        Assert.Contains("ExactMatch", props.Keys);
        Assert.Equal("MyKey?", props["ExactMatch"]);

        // Ergonomics + the original (pre-advanced-filter) call shape: assigning a
        // bare key must compile, via an implicit conversion from the value type.
        Assert.Contains("public static implicit operator MyKeyFilterProperty(MyKey", models);

        // Faithful wire behaviour requires a custom converter (bare scalar when
        // only ExactMatch is set; the { $eq, … } object otherwise).
        Assert.Contains("[JsonConverter(typeof(MyKeyFilterPropertyJsonConverter))]", models);
        Assert.Contains("class MyKeyFilterPropertyJsonConverter", models);
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
            var dir = Path.Combine(Path.GetTempPath(), "csharp-gen-oneof-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return new TempSpecDir(dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}
