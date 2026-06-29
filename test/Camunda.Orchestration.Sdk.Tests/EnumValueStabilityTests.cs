using Camunda.Orchestration.Sdk.Generator;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class regression guard for enum numeric-value stability (see #280).
///
/// Generated enums serialize by name, but their numeric values are baked into
/// consumer IL. The defect class: when the upstream spec inserts a member
/// mid-list, the generator (which assigned values by position) shifted every
/// later member's value, silently changing behaviour for any consumer that cast
/// or persisted the numeric value.
///
/// The fix is <see cref="EnumValueRegistry"/>: a persisted member→int manifest
/// that keeps existing assignments stable and only ever appends. These tests
/// assert the invariant directly — inserting/removing members must never change
/// the value of a member that was already assigned.
/// </summary>
public class EnumValueStabilityTests
{
    private static string NewManifestPath() =>
        Path.Combine(Path.GetTempPath(), $"enumvalues-{Guid.NewGuid():N}.json");

    [Fact]
    public void Assign_SeedsByCallOrder_FromEmpty()
    {
        var path = NewManifestPath();
        try
        {
            EnumValueRegistry.LoadFrom(path);

            Assert.Equal(0, EnumValueRegistry.Assign("MyEnum", "ALPHA"));
            Assert.Equal(1, EnumValueRegistry.Assign("MyEnum", "BRAVO"));
            Assert.Equal(2, EnumValueRegistry.Assign("MyEnum", "CHARLIE"));
            // Re-asking is idempotent.
            Assert.Equal(1, EnumValueRegistry.Assign("MyEnum", "BRAVO"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Assign_IsStable_WhenMemberInsertedMidList_AcrossRuns()
    {
        var path = NewManifestPath();
        try
        {
            // First generation run: members ALPHA, BRAVO.
            EnumValueRegistry.LoadFrom(path);
            var alpha1 = EnumValueRegistry.Assign("MyEnum", "ALPHA");
            var bravo1 = EnumValueRegistry.Assign("MyEnum", "BRAVO");
            EnumValueRegistry.Save();

            // Second run after upstream inserts CHARLIE *between* ALPHA and BRAVO.
            EnumValueRegistry.LoadFrom(path);
            var alpha2 = EnumValueRegistry.Assign("MyEnum", "ALPHA");
            var charlie = EnumValueRegistry.Assign("MyEnum", "CHARLIE");
            var bravo2 = EnumValueRegistry.Assign("MyEnum", "BRAVO");

            // Existing members keep their values; the new one is appended.
            Assert.Equal(alpha1, alpha2);
            Assert.Equal(bravo1, bravo2);
            Assert.Equal(2, charlie);
            // Critically, BRAVO did NOT shift to make room for CHARLIE.
            Assert.NotEqual(charlie, bravo2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Assign_DoesNotReuse_RemovedMemberValue()
    {
        var path = NewManifestPath();
        try
        {
            // Run 1: ALPHA=0, BRAVO=1, CHARLIE=2.
            EnumValueRegistry.LoadFrom(path);
            EnumValueRegistry.Assign("MyEnum", "ALPHA");
            EnumValueRegistry.Assign("MyEnum", "BRAVO");
            EnumValueRegistry.Assign("MyEnum", "CHARLIE");
            EnumValueRegistry.Save();

            // Run 2: BRAVO removed upstream, DELTA added. DELTA must not reuse
            // BRAVO's retired value (1) — tombstones keep retired ints reserved.
            EnumValueRegistry.LoadFrom(path);
            EnumValueRegistry.Assign("MyEnum", "ALPHA");
            EnumValueRegistry.Assign("MyEnum", "CHARLIE");
            var delta = EnumValueRegistry.Assign("MyEnum", "DELTA");

            Assert.Equal(3, delta);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
