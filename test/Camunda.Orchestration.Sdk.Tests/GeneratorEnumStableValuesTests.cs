using System.Text.RegularExpressions;
using Camunda.Orchestration.Sdk.Generator;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class regression guard for issue #535: generated enum members took their
/// numeric value from their position in the spec's (alphabetical) value list, so a
/// mid-list insert (e.g. <c>ANSWER</c> in <c>PermissionTypeEnum</c>) shifted every later
/// member — a binary-breaking change. The guard covers every generated enum shape
/// (component enums and inline property enums) across insertions and removals: an
/// existing wire value must keep its number, and a number must never be reused.
/// </summary>
public class GeneratorEnumStableValuesTests
{
    private static string Spec(string[] values)
    {
        var list = string.Join(", ", values.Select(v => $"\"{v}\""));
        return $$"""
            {
              "openapi": "3.0.3",
              "info": { "title": "Test", "version": "1.0.0" },
              "servers": [ { "url": "http://localhost:8080/v2" } ],
              "paths": {
                "/things": {
                  "get": {
                    "operationId": "getThings",
                    "responses": { "200": { "description": "ok",
                      "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Holder" } } } } }
                  }
                }
              },
              "components": {
                "schemas": {
                  "ComponentEnum": { "type": "string", "enum": [{{list}}] },
                  "Holder": {
                    "type": "object",
                    "properties": {
                      "component": { "$ref": "#/components/schemas/ComponentEnum" },
                      "inlineKind": { "type": "string", "enum": [{{list}}] }
                    }
                  }
                }
              }
            }
            """;
    }

    private static Dictionary<string, Dictionary<string, int>> Enums(string source)
    {
        var result = new Dictionary<string, Dictionary<string, int>>();
        foreach (Match m in Regex.Matches(source, @"public enum (\w+)\s*\{(.*?)\n\}", RegexOptions.Singleline))
        {
            var members = new Dictionary<string, int>();
            foreach (Match mm in Regex.Matches(m.Groups[2].Value, @"JsonPropertyName\(""([^""]+)""\)\]\s*(?:\[[^\]]*\]\s*)*\w+(?:\s*=\s*(\d+))?,"))
            {
                // Without an explicit value the member is positional.
                members[mm.Groups[1].Value] = mm.Groups[2].Success ? int.Parse(mm.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : members.Count;
            }
            result[m.Groups[1].Value] = members;
        }
        return result;
    }

    private static Dictionary<string, Dictionary<string, int>> Generate(TempSpecDir tmp, string[] values)
    {
        File.WriteAllText(tmp.SpecPath, Spec(values));
        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);
        var all = Enums(File.ReadAllText(Path.Combine(tmp.OutputDir, "Models.Generated.cs")));
        Assert.Equal(2, all.Count);
        return all;
    }

    [Fact]
    public void Enum_member_values_are_stable_across_spec_inserts_and_removals()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.MetadataPath, MinimalBundledSpec.MinimalMetadata);

        var before = Generate(tmp, ["ACCESS", "CLAIM", "COMPLETE"]);
        var inserted = Generate(tmp, ["ACCESS", "ANSWER", "CLAIM", "COMPLETE"]);
        var removed = Generate(tmp, ["ACCESS", "ANSWER", "COMPLETE", "ZEBRA"]);

        foreach (var (enumName, members) in before)
        {
            foreach (var (wire, value) in members)
            {
                Assert.True(inserted[enumName][wire] == value,
                    $"{enumName}.{wire} changed from {value} to {inserted[enumName][wire]} after a mid-list insert");
            }

            Assert.Equal(3, inserted[enumName]["ANSWER"]);
            Assert.Equal(inserted[enumName]["COMPLETE"], removed[enumName]["COMPLETE"]);
            Assert.Equal(inserted[enumName]["ANSWER"], removed[enumName]["ANSWER"]);
            // CLAIM (value 1) was removed; its number must not be reused.
            Assert.Equal(4, removed[enumName]["ZEBRA"]);
        }
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
            var dir = Path.Combine(Path.GetTempPath(), "csharp-gen-enumvalues-" + Guid.NewGuid().ToString("N"));
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
