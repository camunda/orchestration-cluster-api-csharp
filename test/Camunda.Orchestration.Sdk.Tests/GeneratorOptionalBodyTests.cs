using Camunda.Orchestration.Sdk.Generator;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class regression guard for optional request bodies
/// (<c>requestBody.required: false</c>) — see issue #469.
///
/// The defect: the generator keyed the emitted method signature only on
/// whether a request body <em>schema</em> existed, ignoring
/// <c>requestBody.required</c>. Two defect instances resulted, both
/// diverging from the JS SDK:
///
/// 1. An optional body whose content media type carries <em>no schema</em>
///    (e.g. <c>cancelBatchOperation</c>, <c>resumeBatchOperation</c>,
///    <c>suspendBatchOperation</c>) was dropped from the signature entirely,
///    so the SDK sent <em>no</em> request body. The JS SDK types these as
///    <c>body?: unknown</c> and sends <c>{}</c> when the caller omits it.
///
/// 2. An optional body <em>with</em> a schema (e.g. <c>listSecrets</c>) was
///    emitted as a <em>required</em> parameter, forcing callers to pass an
///    empty request object. The JS SDK types these as <c>body?: T</c> and
///    sends no body when omitted.
///
/// The test targets the defect <em>class</em>: for every operation whose
/// request body is optional, the generated signature must accept omission
/// (<c>T? body = null</c>), and the wire behaviour must match the JS SDK —
/// schemaless bodies default to <c>{}</c> on the wire, schema-ful bodies
/// send nothing when omitted.
/// </summary>
public class GeneratorOptionalBodyTests
{
    // One spec exercising all four request-body shapes:
    //   schemalessOptional  — requestBody.required=false, no schema (cancelBatchOperation shape)
    //   schemafulOptional   — requestBody.required=false, schema $ref (listSecrets shape)
    //   schemafulRequired   — requestBody.required=true,  schema $ref (the common case)
    //   noBody              — no requestBody at all
    private const string OptionalBodySpec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Test", "version": "1.0.0" },
          "paths": {
            "/things/{thingKey}/cancellation": {
              "post": {
                "operationId": "schemalessOptional",
                "parameters": [
                  {
                    "name": "thingKey",
                    "in": "path",
                    "required": true,
                    "schema": { "type": "string" }
                  }
                ],
                "requestBody": {
                  "required": false,
                  "content": { "application/json": { } }
                },
                "responses": { "204": { "description": "done" } }
              }
            },
            "/things/list": {
              "post": {
                "operationId": "schemafulOptional",
                "requestBody": {
                  "required": false,
                  "content": {
                    "application/json": { "schema": { "$ref": "#/components/schemas/ThingListRequest" } }
                  }
                },
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": {
                      "application/json": { "schema": { "$ref": "#/components/schemas/ThingListResult" } }
                    }
                  }
                }
              }
            },
            "/things": {
              "post": {
                "operationId": "schemafulRequired",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": { "schema": { "$ref": "#/components/schemas/ThingCreateRequest" } }
                  }
                },
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": {
                      "application/json": { "schema": { "$ref": "#/components/schemas/ThingListResult" } }
                    }
                  }
                }
              }
            },
            "/things/ping": {
              "post": {
                "operationId": "noBody",
                "responses": { "204": { "description": "done" } }
              }
            }
          },
          "components": {
            "schemas": {
              "ThingListRequest": {
                "type": "object",
                "properties": { "filter": { "type": "string" } }
              },
              "ThingCreateRequest": {
                "type": "object",
                "required": [ "name" ],
                "properties": { "name": { "type": "string" } }
              },
              "ThingListResult": {
                "type": "object",
                "properties": { "items": { "type": "array", "items": { "type": "string" } } }
              }
            }
          }
        }
        """;

    [Fact]
    public void Schemaless_optional_body_is_emitted_as_optional_object_and_defaults_to_empty_json_object()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, OptionalBodySpec);
        File.WriteAllText(tmp.MetadataPath, MinimalBundledSpec.MinimalMetadata);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);

        var client = File.ReadAllText(Path.Combine(tmp.OutputDir, "CamundaClient.Generated.cs"));
        var root = CSharpSyntaxTree.ParseText(client).GetCompilationUnitRoot();
        var method = SingleMethod(root, "SchemalessOptionalAsync");

        // Signature: path param + optional schemaless body. The JS SDK types
        // this body as `unknown`; `object?` is the C# equivalent.
        var parameters = method.ParameterList.Parameters;
        Assert.Equal(3, parameters.Count);
        Assert.Equal("string", parameters[0].Type?.ToString());
        Assert.Equal("thingKey", parameters[0].Identifier.ValueText);
        Assert.Equal("object?", parameters[1].Type?.ToString());
        Assert.Equal("body", parameters[1].Identifier.ValueText);
        Assert.NotNull(parameters[1].Default); // optional

        // Wire behaviour: an omitted body must still send `{}` (JS parity),
        // never a bodyless POST.
        var bodyText = method.Body!.ToString();
        Assert.Contains("body ?? new object()", bodyText);
    }

    [Fact]
    public void Schemaful_optional_body_is_emitted_as_nullable_and_omitted_body_sends_nothing()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, OptionalBodySpec);
        File.WriteAllText(tmp.MetadataPath, MinimalBundledSpec.MinimalMetadata);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);

        var client = File.ReadAllText(Path.Combine(tmp.OutputDir, "CamundaClient.Generated.cs"));
        var root = CSharpSyntaxTree.ParseText(client).GetCompilationUnitRoot();
        var method = SingleMethod(root, "SchemafulOptionalAsync");

        // Signature: optional typed body (JS: body?: ThingListRequest).
        var parameters = method.ParameterList.Parameters;
        Assert.Equal(2, parameters.Count);
        Assert.Equal("ThingListRequest?", parameters[0].Type?.ToString());
        Assert.Equal("body", parameters[0].Identifier.ValueText);
        Assert.NotNull(parameters[0].Default); // optional

        // Wire behaviour: the body is passed through as-is; the runtime
        // omits the request content when it is null (JS: no body sent).
        var bodyText = method.Body!.ToString();
        Assert.DoesNotContain("?? new object()", bodyText);
        Assert.Contains("path, body, ct", bodyText);
    }

    [Fact]
    public void Required_body_stays_a_required_parameter()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, OptionalBodySpec);
        File.WriteAllText(tmp.MetadataPath, MinimalBundledSpec.MinimalMetadata);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);

        var client = File.ReadAllText(Path.Combine(tmp.OutputDir, "CamundaClient.Generated.cs"));
        var root = CSharpSyntaxTree.ParseText(client).GetCompilationUnitRoot();
        var method = SingleMethod(root, "SchemafulRequiredAsync");

        var parameters = method.ParameterList.Parameters;
        Assert.Equal(2, parameters.Count);
        Assert.Equal("ThingCreateRequest", parameters[0].Type?.ToString());
        Assert.Equal("body", parameters[0].Identifier.ValueText);
        Assert.Null(parameters[0].Default); // required — no default value
    }

    [Fact]
    public void Operation_without_request_body_keeps_bodyless_signature()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, OptionalBodySpec);
        File.WriteAllText(tmp.MetadataPath, MinimalBundledSpec.MinimalMetadata);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);

        var client = File.ReadAllText(Path.Combine(tmp.OutputDir, "CamundaClient.Generated.cs"));
        var root = CSharpSyntaxTree.ParseText(client).GetCompilationUnitRoot();
        var method = SingleMethod(root, "NoBodyAsync");

        // Only the CancellationToken — no body parameter at all.
        var parameters = method.ParameterList.Parameters;
        Assert.Single(parameters);
        Assert.Equal("CancellationToken", parameters[0].Type?.ToString());
    }

    // A oneOf body whose variants inherit an optional `tenantId` from an
    // allOf-referenced base schema — the ProcessInstanceCreationInstruction
    // shape. The bundler's optionalTenantIdInBody metadata flag does not
    // follow allOf, so without the generator's own detection the
    // default-tenant injection is silently dropped (issue #469).
    private const string AllOfTenantIdSpec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Test", "version": "1.0.0" },
          "paths": {
            "/instances": {
              "post": {
                "operationId": "createInstance",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": { "schema": { "$ref": "#/components/schemas/CreateInstruction" } }
                  }
                },
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": {
                      "application/json": { "schema": { "$ref": "#/components/schemas/CreateResult" } }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "CreateInstruction": {
                "type": "object",
                "oneOf": [
                  { "$ref": "#/components/schemas/CreateByKey" },
                  { "$ref": "#/components/schemas/CreateById" }
                ]
              },
              "CreateInstructionBase": {
                "type": "object",
                "properties": {
                  "tenantId": { "type": "string" }
                }
              },
              "CreateByKey": {
                "type": "object",
                "required": [ "definitionKey" ],
                "allOf": [ { "$ref": "#/components/schemas/CreateInstructionBase" } ],
                "properties": { "definitionKey": { "type": "string" } }
              },
              "CreateById": {
                "type": "object",
                "required": [ "definitionId" ],
                "allOf": [ { "$ref": "#/components/schemas/CreateInstructionBase" } ],
                "properties": { "definitionId": { "type": "string" } }
              },
              "CreateResult": {
                "type": "object",
                "properties": { "instanceKey": { "type": "string" } }
              }
            }
          }
        }
        """;

    [Fact]
    public void Optional_tenantId_inherited_via_allOf_still_gets_default_tenant_injection()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, AllOfTenantIdSpec);
        File.WriteAllText(tmp.MetadataPath, MinimalBundledSpec.MinimalMetadata);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);

        // The operation emits the SetDefaultTenantId hook…
        var client = File.ReadAllText(Path.Combine(tmp.OutputDir, "CamundaClient.Generated.cs"));
        var root = CSharpSyntaxTree.ParseText(client).GetCompilationUnitRoot();
        var method = SingleMethod(root, "CreateInstanceAsync");
        Assert.Contains("SetDefaultTenantId", method.Body!.ToString());

        // …and every oneOf variant implements ITenantIdSettable even though
        // tenantId arrives only through the allOf-composed base schema.
        var models = File.ReadAllText(Path.Combine(tmp.OutputDir, "Models.Generated.cs"));
        var modelsRoot = CSharpSyntaxTree.ParseText(models).GetCompilationUnitRoot();
        foreach (var variant in new[] { "CreateByKey", "CreateById" })
        {
            var cls = modelsRoot.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .SingleOrDefault(c => c.Identifier.ValueText == variant);
            Assert.True(cls != null, $"Expected a generated class named '{variant}'.");
            Assert.Contains(
                cls!.BaseList?.Types.Select(t => t.ToString()) ?? Enumerable.Empty<string>(),
                t => t.Contains("ITenantIdSettable"));
        }
    }

    private static MethodDeclarationSyntax SingleMethod(CompilationUnitSyntax root, string name)
    {
        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(m => m.Identifier.ValueText == name);
        Assert.True(method != null, $"Expected a generated method named '{name}'.");
        return method!;
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
            var dir = Path.Combine(Path.GetTempPath(), "csharp-gen-optbody-" + Guid.NewGuid().ToString("N"));
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
