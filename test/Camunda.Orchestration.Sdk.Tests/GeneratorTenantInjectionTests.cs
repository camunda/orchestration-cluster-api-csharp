using System.Text;
using Camunda.Orchestration.Sdk.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

    private const string PluralInjectionLine =
        "if (body is ITenantIdsSettable __ts) __ts.SetDefaultTenantIds(_config.DefaultTenantId);";

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

    // Same mixed-union shape as MixedOneOfSpec, but with `anyOf` instead of
    // `oneOf`. Model generation only builds the abstract base + derived-variant
    // relationship for `oneOf` (BuildOneOfMaps ignores `anyOf`), so an `anyOf`
    // body is generated as a *sealed* class with no ITenantIdSettable. Emitting
    // `body is ITenantIdSettable` against that sealed type is a compile error
    // (CS8121): a sealed type that does not implement the interface can never
    // match the pattern. The injection must therefore be suppressed for body
    // shapes model generation cannot support (issue #565 review).
    private const string MixedAnyOfSpec = """
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
            }
          },
          "components": {
            "schemas": {
              "Instruction": {
                "anyOf": [
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
    public void Mixed_anyOf_body_gets_no_injection_when_model_generation_cannot_support_it()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, MixedAnyOfSpec);
        File.WriteAllText(tmp.MetadataPath, """
            {
              "schemaVersion": "1",
              "specHash": "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
              "semanticKeys": [],
              "unions": [],
              "operations": [
                { "operationId": "suspendThings", "optionalTenantIdInBody": false }
              ],
              "deprecatedEnumMembers": []
            }
            """);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);
        var client = File.ReadAllText(Path.Combine(tmp.OutputDir, "CamundaClient.Generated.cs"));
        var models = File.ReadAllText(Path.Combine(tmp.OutputDir, "Models.Generated.cs"));

        // The anyOf body is generated as a sealed class that does not implement
        // ITenantIdSettable, so the injection line must NOT be emitted — emitting
        // it would be a CS8121 compile error.
        var suspend = MethodBody(client, "SuspendThingsAsync");
        Assert.DoesNotContain(InjectionLine, suspend);

        // The generated models must compile together with the client method that
        // references them — no CS8121 from an impossible pattern check.
        AssertCompilesWithoutPatternMismatch(models, client, "SuspendThingsAsync");
    }

    // The plural `tenantIds` operation-level detector
    // (HasOptionalTenantIdsArrayInAnyVariant) descends into union variants, so an
    // anyOf body whose variants all carry an optional `tenantIds` returns true.
    // But the anyOf base is generated as a *sealed* class with no own TenantIds
    // property, and marking it ITenantIdsSettable would emit a SetDefaultTenantIds
    // method that references a non-existent TenantIds member (a compile error).
    // The injection is therefore suppressed for this shape, exactly like the
    // singular anyOf case: the union base has no own tenant property, so no
    // interface can be honoured through it.
    private const string PluralAnyOfSpec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Test", "version": "1.0.0" },
          "paths": {
            "/things/activation": {
              "post": {
                "operationId": "activateThings",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": { "schema": { "$ref": "#/components/schemas/Activation" } }
                  }
                },
                "responses": { "204": { "description": "done" } }
              }
            }
          },
          "components": {
            "schemas": {
              "Activation": {
                "anyOf": [
                  { "$ref": "#/components/schemas/ActivationByKey" },
                  { "$ref": "#/components/schemas/ActivationById" }
                ]
              },
              "ActivationByKey": {
                "type": "object",
                "required": ["thingKey"],
                "properties": {
                  "thingKey": { "type": "string" },
                  "tenantIds": { "type": "array", "items": { "type": "string" } }
                }
              },
              "ActivationById": {
                "type": "object",
                "required": ["thingId"],
                "properties": {
                  "thingId": { "type": "string" },
                  "tenantIds": { "type": "array", "items": { "type": "string" } }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public void Plural_anyOf_body_with_agreeing_tenantIds_gets_no_injection()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, PluralAnyOfSpec);
        File.WriteAllText(tmp.MetadataPath, """
            {
              "schemaVersion": "1",
              "specHash": "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
              "semanticKeys": [],
              "unions": [],
              "operations": [
                { "operationId": "activateThings", "optionalTenantIdInBody": false }
              ],
              "deprecatedEnumMembers": []
            }
            """);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);
        var client = File.ReadAllText(Path.Combine(tmp.OutputDir, "CamundaClient.Generated.cs"));
        var models = File.ReadAllText(Path.Combine(tmp.OutputDir, "Models.Generated.cs"));

        // The anyOf base has no own TenantIds property, so the injection is
        // suppressed and the base carries no ITenantIdsSettable.
        var activate = MethodBody(client, "ActivateThingsAsync");
        Assert.DoesNotContain(PluralInjectionLine, activate);
        AssertCompilesWithoutPatternMismatch(models, client, "ActivateThingsAsync");
    }

    // The plural defect class: an anyOf body whose variants all carry an
    // optional `tenantIds` but with DISAGREEING item types. The operation-level
    // detector (HasOptionalTenantIdsArrayInAnyVariant) only checks each variant
    // has an optional tenantIds array — not that the item types agree — so it
    // returns true. But model generation's GetOptionalTenantIdsInfo returns null
    // on disagreement, so the sealed body class gets NO ITenantIdsSettable and
    // the emitted `body is ITenantIdsSettable` is a CS8121 compile error. The
    // injection must be suppressed.
    private const string PluralAnyOfDisagreeingSpec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Test", "version": "1.0.0" },
          "paths": {
            "/things/activation": {
              "post": {
                "operationId": "activateThings",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": { "schema": { "$ref": "#/components/schemas/Activation" } }
                  }
                },
                "responses": { "204": { "description": "done" } }
              }
            }
          },
          "components": {
            "schemas": {
              "Activation": {
                "anyOf": [
                  { "$ref": "#/components/schemas/ActivationByKey" },
                  { "$ref": "#/components/schemas/ActivationById" }
                ]
              },
              "ActivationByKey": {
                "type": "object",
                "required": ["thingKey"],
                "properties": {
                  "thingKey": { "type": "string" },
                  "tenantIds": { "type": "array", "items": { "type": "string" } }
                }
              },
              "ActivationById": {
                "type": "object",
                "required": ["thingId"],
                "properties": {
                  "thingId": { "type": "string" },
                  "tenantIds": { "type": "array", "items": { "type": "integer" } }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public void Plural_anyOf_body_with_disagreeing_tenantIds_gets_no_injection()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, PluralAnyOfDisagreeingSpec);
        File.WriteAllText(tmp.MetadataPath, """
            {
              "schemaVersion": "1",
              "specHash": "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
              "semanticKeys": [],
              "unions": [],
              "operations": [
                { "operationId": "activateThings", "optionalTenantIdInBody": false }
              ],
              "deprecatedEnumMembers": []
            }
            """);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);
        var client = File.ReadAllText(Path.Combine(tmp.OutputDir, "CamundaClient.Generated.cs"));
        var models = File.ReadAllText(Path.Combine(tmp.OutputDir, "Models.Generated.cs"));

        var activate = MethodBody(client, "ActivateThingsAsync");
        Assert.DoesNotContain(PluralInjectionLine, activate);
        AssertCompilesWithoutPatternMismatch(models, client, "ActivateThingsAsync");
    }

    // An inline oneOf body that matches no component oneOf schema is generated
    // as a sealed <OperationId>Request class via GenerateClass. That class has
    // no own tenantId (the tenantId lives on the inline variants, which are not
    // generated as ITenantIdSettable types), so the injection line would again
    // be a CS8121 compile error and must be suppressed.
    private const string UnmatchedInlineOneOfSpec = """
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
                    "application/json": {
                      "schema": {
                        "oneOf": [
                          {
                            "type": "object",
                            "required": ["thingKey"],
                            "properties": { "thingKey": { "type": "string" } }
                          },
                          {
                            "type": "object",
                            "required": ["thingId"],
                            "properties": {
                              "thingId": { "type": "string" },
                              "tenantId": { "type": "string" }
                            }
                          }
                        ]
                      }
                    }
                  }
                },
                "responses": { "204": { "description": "done" } }
              }
            }
          },
          "components": { "schemas": { } }
        }
        """;

    [Fact]
    public void Unmatched_inline_oneOf_body_gets_no_injection_when_model_generation_cannot_support_it()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, UnmatchedInlineOneOfSpec);
        File.WriteAllText(tmp.MetadataPath, """
            {
              "schemaVersion": "1",
              "specHash": "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
              "semanticKeys": [],
              "unions": [],
              "operations": [
                { "operationId": "suspendThings", "optionalTenantIdInBody": false }
              ],
              "deprecatedEnumMembers": []
            }
            """);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);
        var client = File.ReadAllText(Path.Combine(tmp.OutputDir, "CamundaClient.Generated.cs"));
        var models = File.ReadAllText(Path.Combine(tmp.OutputDir, "Models.Generated.cs"));

        var suspend = MethodBody(client, "SuspendThingsAsync");
        Assert.DoesNotContain(InjectionLine, suspend);

        AssertCompilesWithoutPatternMismatch(models, client, "SuspendThingsAsync");
    }

    /// <summary>
    /// Compiles the generated models together with a tiny harness that performs
    /// the same <c>body is ITenantIdSettable</c> / <c>ITenantIdsSettable</c>
    /// pattern check the client emits, using the named method's real body
    /// parameter type, and asserts there is no compile error (in particular no
    /// CS8121 "an expression of type X cannot be handled by a pattern of type
    /// ITenantIdSettable"). This is the compile-level regression guard for the
    /// injection/model-generation mismatch.
    /// </summary>
    private static void AssertCompilesWithoutPatternMismatch(string modelsSource, string clientSource, string methodName)
    {
        // Reproduce the exact injection lines the generated method actually
        // emits against its real body parameter type. If the generator emitted
        // an injection against a sealed body type that does not implement the
        // interface, the harness reproduces that same line and fails to compile
        // with CS8121 — which is exactly the defect being guarded against. When
        // the generator (correctly) suppressed the injection, the harness body
        // is empty and only the models need to compile.
        var bodyType = ExtractBodyParameterType(clientSource, methodName);
        var method = MethodBody(clientSource, methodName);
        var injections = new StringBuilder();
        if (method.Contains(InjectionLine, StringComparison.Ordinal))
            injections.AppendLine("        if (body is ITenantIdSettable __t) __t.SetDefaultTenantId(\"x\");");
        if (method.Contains(PluralInjectionLine, StringComparison.Ordinal))
            injections.AppendLine("        if (body is ITenantIdsSettable __ts) __ts.SetDefaultTenantIds(\"x\");");
        var harness = $$"""
            namespace Camunda.Orchestration.Sdk;
            public static class InjectionHarness
            {
                public static void Apply({{bodyType}} body)
                {
            {{injections.ToString().TrimEnd()}}
                }
            }
            """;

        // The generated models rely on the SDK project's ImplicitUsings (e.g.
        // System.Collections.Generic for List<>), which a standalone Roslyn
        // compilation does not apply. Prepend the same set so the models parse.
        const string implicitUsings = """
            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            """;
        var modelsWithUsings = implicitUsings + modelsSource;

        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "InjectionHarnessAssembly",
            new[]
            {
                CSharpSyntaxTree.ParseText(modelsWithUsings),
                CSharpSyntaxTree.ParseText(harness),
            },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            errors.Count == 0,
            $"Generated models + injection harness failed to compile: {string.Join("; ", errors.Select(e => $"{e.Id}: {e.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}"))}");
    }

    /// <summary>
    /// Extracts the declared type of the first (body) parameter of the named
    /// generated method, e.g. <c>Instruction</c> from
    /// <c>SuspendThingsAsync(Instruction body, …)</c>.
    /// </summary>
    private static string ExtractBodyParameterType(string generatedSource, string methodName)
    {
        var marker = $" {methodName}(";
        var start = generatedSource.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected a generated method named '{methodName}'.");
        var paramStart = start + marker.Length;
        var paramEnd = generatedSource.IndexOf(' ', paramStart);
        Assert.True(paramEnd > paramStart, $"Could not parse body parameter type for '{methodName}'.");
        return generatedSource[paramStart..paramEnd];
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
