using Camunda.Orchestration.Sdk.Generator;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class guard for the generator half of the <c>x-present-when</c> feature: deriving the
/// dependent-presence coupling table from the OpenAPI spec.
///
/// <para>OpenAPI cannot express "this response field is present only when a request flag was
/// set", so the spec states the coupling out-of-band with an <c>x-present-when</c> marker and the
/// generator turns every marker into a row of the generated table. The class of defect these
/// tests guard is the derivation silently accepting a malformed marker, dropping a valid one, or
/// emitting an empty/mis-ordered table — any of which would let a real coupling go unenforced.</para>
/// </summary>
public class PresentWhenDerivationTests
{
    private static string Spec(string schemasJson) => $$"""
        {
          "openapi": "3.0.0",
          "info": { "title": "t", "version": "1" },
          "paths": {},
          "components": { "schemas": {{{schemasJson}}} }
        }
        """;

    private static string Marker(string schema, string field, string request, string equals = "true") => $$"""
        "{{schema}}": {
          "type": "object",
          "properties": {
            "{{field}}": {
              "type": "string",
              "x-present-when": { "request": "{{request}}", "equals": {{equals}} }
            }
          }
        }
        """;

    [Fact]
    public void Derive_returns_a_row_for_the_lease_marker_in_the_real_spec_shape()
    {
        var couplings = PresentWhenDerivation.Derive(
            Spec(Marker("ActivatedJobResult", "jobLeaseToken", "withLease")));

        var c = Assert.Single(couplings);
        Assert.Equal("ActivatedJobResult", c.ResponseSchema);
        Assert.Equal("jobLeaseToken", c.ResponseField);
        Assert.Equal("withLease", c.RequestFlag);
    }

    [Fact]
    public void Derive_sorts_by_schema_then_field_so_output_is_deterministic()
    {
        var couplings = PresentWhenDerivation.Derive(Spec(
            Marker("Zeta", "b", "flagZB") + "," +
            Marker("Alpha", "y", "flagAY") + "," +
            Marker("Alpha", "x", "flagAX")));

        Assert.Collection(couplings,
            c => { Assert.Equal("Alpha", c.ResponseSchema); Assert.Equal("x", c.ResponseField); },
            c => { Assert.Equal("Alpha", c.ResponseSchema); Assert.Equal("y", c.ResponseField); },
            c => { Assert.Equal("Zeta", c.ResponseSchema); Assert.Equal("b", c.ResponseField); });
    }

    [Fact]
    public void Derive_returns_an_empty_table_when_no_marker_exists()
    {
        // The generator's own tests feed it synthetic specs with no markers, so an absent
        // marker is not a derivation error. The guarantee that the *production* spec's marker
        // survived is enforced by PresentWhenCouplingTests against the real bundle.
        var noMarker = """
            "Plain": { "type": "object", "properties": { "x": { "type": "string" } } }
            """;

        Assert.Empty(PresentWhenDerivation.Derive(Spec(noMarker)));
    }

    [Theory]
    // request missing entirely
    [InlineData("""
        "S": { "type": "object", "properties": { "f": { "type": "string",
          "x-present-when": { "equals": true } } } }
        """)]
    // request explicitly null
    [InlineData("""
        "S": { "type": "object", "properties": { "f": { "type": "string",
          "x-present-when": { "request": null, "equals": true } } } }
        """)]
    // request non-string
    [InlineData("""
        "S": { "type": "object", "properties": { "f": { "type": "string",
          "x-present-when": { "request": 7, "equals": true } } } }
        """)]
    // request empty string
    [InlineData("""
        "S": { "type": "object", "properties": { "f": { "type": "string",
          "x-present-when": { "request": "", "equals": true } } } }
        """)]
    // equals != true
    [InlineData("""
        "S": { "type": "object", "properties": { "f": { "type": "string",
          "x-present-when": { "request": "flag", "equals": false } } } }
        """)]
    // equals missing
    [InlineData("""
        "S": { "type": "object", "properties": { "f": { "type": "string",
          "x-present-when": { "request": "flag" } } } }
        """)]
    public void Derive_rejects_every_malformed_marker_shape(string schemaJson)
    {
        Assert.Throws<InvalidOperationException>(() => PresentWhenDerivation.Derive(Spec(schemaJson)));
    }

    [Fact]
    public void Render_emits_the_coupling_as_a_row_of_the_generated_table()
    {
        var couplings = PresentWhenDerivation.Derive(
            Spec(Marker("ActivatedJobResult", "jobLeaseToken", "withLease")));

        var source = PresentWhenDerivation.Render(couplings);

        Assert.Contains("PresentWhenCoupling", source);
        Assert.Contains("\"ActivatedJobResult\"", source);
        Assert.Contains("\"jobLeaseToken\"", source);
        Assert.Contains("\"withLease\"", source);
    }

    [Fact]
    public void Render_escapes_hostile_characters_in_spec_names()
    {
        // Schema/field/flag names originate in the spec; a control or line-terminator
        // character in one must be emitted as a \u escape, not a raw literal that would
        // produce invalid or unsafe generated C#. Uses the same escaping the other
        // generator emission paths use (SafeEmit.SafeCSharpStringLiteral).
        var couplings = new[]
        {
            new PresentWhenDerivation.Coupling("Sch\u2028ema", "fie\rld", "fl\u0000ag"),
        };

        var source = PresentWhenDerivation.Render(couplings);

        Assert.DoesNotContain('\u2028', source);
        Assert.DoesNotContain('\r', source);
        Assert.DoesNotContain('\0', source);
        Assert.Contains("\\u2028", source);
        Assert.Contains("\\r", source);
        Assert.Contains("\\0", source);
    }
}
