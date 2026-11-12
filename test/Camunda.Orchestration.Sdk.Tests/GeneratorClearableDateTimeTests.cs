using Camunda.Orchestration.Sdk.Generator;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class regression guard for clearable date-time properties in the generator.
///
/// The defect: the specification marks dates the API clears with an empty string by
/// <c>x-clearable: true</c> on a shared <c>ClearableDateTime</c> schema (an <c>anyOf</c> of a
/// date-time and a <c>maxLength: 0</c> string, camunda/camunda#68710). Generated naively, that
/// schema becomes a string-valued <c>ICamundaKey</c> struct and every property using it loses
/// its <see cref="DateTimeOffset"/>? type. Older specifications declare the same properties as
/// plain date-times and only say in prose that <c>""</c> clears them, which a bare
/// <see cref="DateTimeOffset"/>? cannot express.
///
/// The test covers the class: every clearable shape (an <c>x-clearable</c> schema reached by
/// <c>$ref</c>, through a single-element <c>allOf</c>, inline; and the two prose-only shapes)
/// must yield a <see cref="DateTimeOffset"/>? property with a clear, while a plain date-time is
/// left untouched and no <c>x-clearable</c> schema is emitted as a type.
/// </summary>
public class GeneratorClearableDateTimeTests
{
    private const string Spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Test", "version": "1.0.0" },
          "paths": {
            "/thing": {
              "post": {
                "operationId": "updateThing",
                "requestBody": {
                  "content": { "application/json": { "schema": { "$ref": "#/components/schemas/NewShape" } } }
                },
                "responses": { "204": { "description": "ok" } }
              }
            }
          },
          "components": {
            "schemas": {
              "ClearableDateTime": {
                "description": "A date-time, or an empty string to clear the persisted value.",
                "type": "string",
                "nullable": true,
                "anyOf": [
                  { "type": "string", "format": "date-time", "nullable": true },
                  { "type": "string", "maxLength": 0, "nullable": true }
                ],
                "x-clearable": true
              },
              "NewShape": {
                "type": "object",
                "properties": {
                  "viaAllOf": {
                    "allOf": [ { "$ref": "#/components/schemas/ClearableDateTime" } ],
                    "description": "Clear by providing an empty String."
                  },
                  "viaRef": { "$ref": "#/components/schemas/ClearableDateTime" },
                  "inlineMarker": {
                    "type": "string",
                    "nullable": true,
                    "anyOf": [
                      { "type": "string", "format": "date-time" },
                      { "type": "string", "maxLength": 0 }
                    ],
                    "x-clearable": true
                  },
                  "plainDate": { "type": "string", "format": "date-time", "nullable": true }
                }
              },
              "OldBullet": {
                "type": "object",
                "description": "Corrections.\n\n* `dueDate` - clear by providing an empty String\n* `assignee` - clear by providing an empty String\n",
                "properties": {
                  "dueDate": { "type": "string", "format": "date-time", "nullable": true },
                  "assignee": { "type": "string", "nullable": true },
                  "plainDate": { "type": "string", "format": "date-time", "nullable": true }
                }
              },
              "OldSentence": {
                "type": "object",
                "properties": {
                  "followUpDate": {
                    "type": "string",
                    "format": "date-time",
                    "description": "The follow-up date. Reset by providing an empty String.",
                    "nullable": true
                  }
                }
              }
            }
          }
        }
        """;

    [Theory]
    [InlineData("NewShape", "ViaAllOf", "viaAllOf")]
    [InlineData("NewShape", "ViaRef", "viaRef")]
    [InlineData("NewShape", "InlineMarker", "inlineMarker")]
    [InlineData("OldBullet", "DueDate", "dueDate")]
    [InlineData("OldSentence", "FollowUpDate", "followUpDate")]
    public void Clearable_property_keeps_its_date_type_and_gains_a_clear(string className, string prop, string json)
    {
        var cls = GenerateClass(className);

        var property = Property(cls, prop);
        Assert.Equal("DateTimeOffset?", property.Type.ToString());
        Assert.True(HasAttribute(property, "JsonIgnore"), $"{className}.{prop} must not bind to JSON directly.");
        Assert.NotNull(property.AccessorList?.Accessors.SingleOrDefault(a => a.Keyword.ValueText == "set"));

        Assert.Equal("bool", Property(cls, $"Is{prop}Cleared").Type.ToString());

        var clear = cls.Members.OfType<MethodDeclarationSyntax>().SingleOrDefault(m => m.Identifier.ValueText == $"Clear{prop}");
        Assert.True(clear != null, $"Expected {className}.Clear{prop}().");
        Assert.Equal(className, clear!.ReturnType.ToString());
        Assert.Empty(clear.ParameterList.Parameters);

        // Exactly one member binds the wire name, and it is the clear-aware wire property.
        var bound = cls.Members.OfType<PropertyDeclarationSyntax>()
            .Where(p => p.AttributeLists.SelectMany(l => l.Attributes)
                .Any(a => a.Name.ToString() == "JsonPropertyName" && a.ArgumentList!.Arguments[0].ToString() == $"\"{json}\""))
            .ToList();
        var wire = Assert.Single(bound);
        Assert.Equal("global::Camunda.Orchestration.Sdk.ClearableDateTimeWire?", wire.Type.ToString());
        Assert.True(HasAttribute(wire, "JsonInclude"));
    }

    [Theory]
    [InlineData("NewShape", "PlainDate")]
    [InlineData("OldBullet", "PlainDate")]
    public void Plain_date_time_is_left_untouched(string className, string prop)
    {
        var cls = GenerateClass(className);

        Assert.Equal("DateTimeOffset?", Property(cls, prop).Type.ToString());
        Assert.DoesNotContain(cls.Members.OfType<MethodDeclarationSyntax>(), m => m.Identifier.ValueText == $"Clear{prop}");
    }

    [Fact]
    public void Non_date_property_named_in_a_clear_bullet_is_left_untouched()
    {
        // `assignee` is a string, which can already carry "": nothing to add.
        var cls = GenerateClass("OldBullet");

        Assert.Equal("string?", Property(cls, "Assignee").Type.ToString());
        Assert.DoesNotContain(cls.Members.OfType<MethodDeclarationSyntax>(), m => m.Identifier.ValueText == "ClearAssignee");
    }

    [Fact]
    public void Clearable_schema_is_not_emitted_as_a_type()
    {
        var models = Generate(Spec);
        var root = CSharpSyntaxTree.ParseText(models).GetCompilationUnitRoot();

        Assert.DoesNotContain(
            root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>(),
            t => t.Identifier.ValueText == "ClearableDateTime");
        Assert.DoesNotContain("ClearableDateTime.AssumeExists", models);
    }

    [Fact]
    public void Clearable_marker_on_a_non_date_fails_generation()
    {
        const string anchor = "\"viaRef\": { \"$ref\": \"#/components/schemas/ClearableDateTime\" },";
        var spec = Spec.Replace(
            anchor,
            anchor + " \"clearableText\": { \"type\": \"string\", \"x-clearable\": true },",
            StringComparison.Ordinal);
        Assert.Contains("clearableText", spec);

        var ex = Assert.Throws<InvalidOperationException>(() => Generate(spec));
        Assert.Contains("NewShape.clearableText", ex.Message);
    }

    private static ClassDeclarationSyntax GenerateClass(string className)
    {
        var root = CSharpSyntaxTree.ParseText(Generate(Spec)).GetCompilationUnitRoot();
        var cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .SingleOrDefault(c => c.Identifier.ValueText == className);
        Assert.True(cls != null, $"Expected a generated class named '{className}'.");
        return cls!;
    }

    private static string Generate(string spec)
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, spec);
        File.WriteAllText(tmp.MetadataPath, MinimalBundledSpec.MinimalMetadata);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);
        return File.ReadAllText(Path.Combine(tmp.OutputDir, "Models.Generated.cs"));
    }

    private static PropertyDeclarationSyntax Property(ClassDeclarationSyntax cls, string name)
    {
        var property = cls.Members.OfType<PropertyDeclarationSyntax>().SingleOrDefault(p => p.Identifier.ValueText == name);
        Assert.True(property != null, $"Expected property '{cls.Identifier.ValueText}.{name}'.");
        return property!;
    }

    private static bool HasAttribute(MemberDeclarationSyntax member, string name) =>
        member.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString() == name);

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
            var dir = Path.Combine(Path.GetTempPath(), "csharp-gen-clearable-" + Guid.NewGuid().ToString("N"));
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
