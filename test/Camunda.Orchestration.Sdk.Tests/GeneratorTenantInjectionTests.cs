using Camunda.Orchestration.Sdk.Generator;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class regression guard for default-tenant injection on mixed
/// <c>oneOf</c>/<c>anyOf</c> request bodies (issue #564).
///
/// The defect: the generator emitted the
/// <c>if (body is ITenantIdSettable __t) __t.SetDefaultTenantId(…)</c> line only
/// when <em>every</em> union variant had an optional <c>tenantId</c>
/// (<c>HasOptionalTenantIdInAnyVariant</c> returned true only when all variants
/// did, despite its name). A mixed union — e.g.
/// <c>ProcessDefinitionSuspensionInstruction</c>, whose by-id variant has an
/// optional <c>tenantId</c> and whose by-key variant has none — therefore got no
/// injection at all, and the by-id body went out without the effective default
/// tenant. The Camunda Java client injects on exactly this path
/// (<c>SuspendProcessDefinitionCommandImpl</c> sets
/// <c>tenantId(config.getDefaultTenantId())</c> on the by-id path only).
///
/// The test targets the defect <em>class</em>: for any operation whose request
/// body is a union in which <em>at least one</em> variant has an optional
/// top-level <c>tenantId</c>, the generated method must carry the injection —
/// the runtime <c>ITenantIdSettable</c> interface check already applies it to
/// the tenant-carrying variants only. Derived from the spec shape, not from a
/// list of operation names, so the next tenant-scoped union is covered without
/// a code change.
/// </summary>
public class GeneratorTenantInjectionTests
{
    private const string InjectionLine =
        "if (body is ITenantIdSettable __t) __t.SetDefaultTenantId(_config.DefaultTenantId);";

    // Mirrors the real ProcessDefinitionSuspensionInstruction shape: a oneOf
    // body whose variants disagree on tenantId.
    //
    //   Instruction       : oneOf[ByKey, ById]
    //   InstructionByKey  : { key (required) }                       — no tenantId
    //   InstructionById   : { id (required), tenantId? }             — optional tenantId
    //
    // Two operations over the same union prove the detection is per-spec-shape,
    // not a hard-coded operation list.
    private const string MixedOneOfSpec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Test", "version": "1.0.0" },
          "paths": {
            "/things/suspension": {
              "post": {
                "operationId": "suspendThings",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": { "schema": { "$ref": "#/components/schemas/Instruction" } }
                  }
                },
                "responses": { "204": { "description": "done" } }
              }
            },
            "/things/resumption": {
              "post": {
                "operationId": "resumeThings",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": { "schema": { "$ref": "#/components/schemas/Instruction" } }
                  }
                },
                "responses": { "204": { "description": "done" } }
              }
            }
          },
          "components": {
            "schemas": {
              "Instruction": {
                "oneOf": [
                  { "$ref": "#/components/schemas/InstructionByKey" },
                  { "$ref": "#/components/schemas/InstructionById" }
                ]
              },
              "InstructionByKey": {
                "type": "object",
                "required": ["thingKey"],
                "properties": {
                  "thingKey": { "type": "string" }
                }
              },
              "InstructionById": {
                "type": "object",
                "required": ["thingId"],
                "properties": {
                  "thingId": { "type": "string" },
                  "tenantId": { "type": "string" }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public void Mixed_oneOf_body_gets_injection_when_any_variant_has_optional_tenantId()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, MixedOneOfSpec);
        // The metadata flag deliberately says false for both operations (the
        // bundler's all-variants semantics) — detection must come from the spec.
        File.WriteAllText(tmp.MetadataPath, """
            {
              "schemaVersion": "1",
              "specHash": "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
              "semanticKeys": [],
              "unions": [],
              "operations": [
                { "operationId": "suspendThings", "optionalTenantIdInBody": false },
                { "operationId": "resumeThings", "optionalTenantIdInBody": false }
              ],
              "deprecatedEnumMembers": []
            }
            """);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);
        var client = File.ReadAllText(Path.Combine(tmp.OutputDir, "CamundaClient.Generated.cs"));
        var models = File.ReadAllText(Path.Combine(tmp.OutputDir, "Models.Generated.cs"));

        // Both operations over the mixed union must carry the injection.
        var suspend = MethodBody(client, "SuspendThingsAsync");
        Assert.Contains(InjectionLine, suspend);
        var resume = MethodBody(client, "ResumeThingsAsync");
        Assert.Contains(InjectionLine, resume);

        // Only the tenant-carrying variant implements the interface, so the
        // runtime check applies the default to the by-id body alone.
        Assert.Contains(
            "InstructionById : Instruction, global::Camunda.Orchestration.Sdk.ITenantIdSettable",
            models);
        var byKey = models.Split("InstructionByKey : Instruction", 2)[1];
        Assert.DoesNotContain("ITenantIdSettable", byKey);
    }

    /// <summary>
    /// Extracts the source text of the named generated method (from its
    /// declaration up to the next method declaration or the end of the class).
    /// </summary>
    private static string MethodBody(string generatedSource, string methodName)
    {
        var marker = $" {methodName}(";
        var start = generatedSource.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected a generated method named '{methodName}'.");
        var next = generatedSource.IndexOf("\n    public ", start + marker.Length, StringComparison.Ordinal);
        return next < 0 ? generatedSource[start..] : generatedSource[start..next];
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
            var dir = Path.Combine(Path.GetTempPath(), "csharp-gen-tenant-" + Guid.NewGuid().ToString("N"));
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
