using System.Text.Json.Nodes;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class guard binding the generated coupling table, the runtime enforcement list, and the
/// OpenAPI spec together.
///
/// <para>Three surfaces can drift independently: the spec's <c>x-present-when</c> markers, the
/// generated <c>PresentWhenCouplings.All</c> table, and the hand-written
/// <c>PresentWhen.EnforcedCouplings</c> list the worker actually acts on. A gap between any two is
/// a real correctness hole — a coupling the spec declares but nobody enforces sends fenced commands
/// unfenced. These tests read the spec directly (not the generated table) so a regeneration that
/// dropped a marker cannot make them pass vacuously.</para>
/// </summary>
public class PresentWhenCouplingTests
{
    [Fact]
    public void Generated_table_matches_every_present_when_marker_in_the_spec()
    {
        var spec = LoadBundledSpec();

        var fromSpec = CollectMarkersFromSpec(spec);

        // No vacuous pass: if the spec grew zero markers the derivation is broken upstream.
        Assert.NotEmpty(fromSpec);

        var fromTable = PresentWhenCouplings.All
            .Select(c => (c.ResponseSchema, c.ResponseField, c.RequestFlag))
            .ToHashSet();

        Assert.Equal(fromSpec, fromTable);
    }

    [Fact]
    public void Every_enforced_coupling_key_names_a_row_in_the_generated_table()
    {
        var tableKeys = PresentWhenCouplings.All.Select(PresentWhen.CouplingKey).ToHashSet();

        // Reverse direction: the runtime must never enforce a coupling the table does not declare,
        // which would mean a hardcoded key drifted from the spec.
        foreach (var enforced in PresentWhen.EnforcedCouplings)
            Assert.Contains(enforced, tableKeys);
    }

    [Fact]
    public void The_lease_coupling_is_both_declared_and_enforced()
    {
        Assert.Contains(PresentWhen.LeaseCouplingKey, PresentWhen.EnforcedCouplings);
        Assert.Contains(PresentWhenCouplings.All, c => PresentWhen.CouplingKey(c) == PresentWhen.LeaseCouplingKey);
        Assert.Equal("withLease", PresentWhen.LeaseRequestFlag());
    }

    [Fact]
    public void RequireLeasePresence_throws_when_a_lease_was_requested_but_no_token_returned()
    {
        var ex = Assert.Throws<LeaseNotHonoredException>(
            () => PresentWhen.RequireLeasePresence(requested: true, jobKey: "42", token: null));

        Assert.Equal("42", ex.JobKey);
        Assert.Equal("withLease", ex.RequestFlag);
    }

    [Fact]
    public void RequireLeasePresence_throws_on_an_empty_token_the_same_as_a_missing_one()
    {
        Assert.Throws<LeaseNotHonoredException>(
            () => PresentWhen.RequireLeasePresence(requested: true, jobKey: "42", token: ""));
    }

    [Fact]
    public void RequireLeasePresence_is_a_noop_when_a_lease_was_requested_and_honored()
    {
        PresentWhen.RequireLeasePresence(requested: true, jobKey: "42", token: "tok");
    }

    [Fact]
    public void RequireLeasePresence_is_a_noop_when_no_lease_was_requested()
    {
        PresentWhen.RequireLeasePresence(requested: false, jobKey: "42", token: null);
    }

    private static HashSet<(string, string, string)> CollectMarkersFromSpec(JsonNode spec)
    {
        var result = new HashSet<(string, string, string)>();
        var schemas = spec["components"]?["schemas"]?.AsObject();
        if (schemas is null)
            return result;

        foreach (var (schemaName, schemaNode) in schemas)
        {
            var props = schemaNode?["properties"]?.AsObject();
            if (props is null)
                continue;

            foreach (var (fieldName, fieldNode) in props)
            {
                var marker = fieldNode?["x-present-when"];
                if (marker is null)
                    continue;

                var request = marker["request"]?.GetValue<string>();
                Assert.False(string.IsNullOrEmpty(request), $"marker on {schemaName}.{fieldName} has no request flag");
                result.Add((schemaName, fieldName, request!));
            }
        }

        return result;
    }

    private static JsonNode LoadBundledSpec()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "external-spec", "bundled", "rest-api.bundle.json");
            if (File.Exists(candidate))
                return JsonNode.Parse(File.ReadAllText(candidate))!;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Could not locate external-spec/bundled/rest-api.bundle.json");
    }
}
