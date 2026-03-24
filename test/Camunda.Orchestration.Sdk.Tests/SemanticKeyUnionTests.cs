using FluentAssertions;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for semantic key union types (ScopeKey, ResourceKey).
/// Verifies that branch types are implicitly assignable to the union type
/// and that cross-type equality comparisons work correctly.
/// </summary>
public class SemanticKeyUnionTests
{
    // ── ScopeKey (ProcessInstanceKey | ElementInstanceKey) ──────────────────

    [Fact]
    public void ScopeKey_ImplicitConversion_FromProcessInstanceKey_PreservesValue()
    {
        var pik = ProcessInstanceKey.AssumeExists("2251799813683890");
        ScopeKey scope = pik;
        scope.Value.Should().Be("2251799813683890");
    }

    [Fact]
    public void ScopeKey_ImplicitConversion_FromElementInstanceKey_PreservesValue()
    {
        var eik = ElementInstanceKey.AssumeExists("2251799813683891");
        ScopeKey scope = eik;
        scope.Value.Should().Be("2251799813683891");
    }

    [Fact]
    public void ScopeKey_Equality_WithProcessInstanceKey_SameValue_IsTrue()
    {
        var scope = ScopeKey.AssumeExists("123");
        var pik = ProcessInstanceKey.AssumeExists("123");
        (scope == pik).Should().BeTrue();
        (scope != pik).Should().BeFalse();
    }

    [Fact]
    public void ScopeKey_Equality_WithProcessInstanceKey_DifferentValue_IsFalse()
    {
        var scope = ScopeKey.AssumeExists("123");
        var pik = ProcessInstanceKey.AssumeExists("456");
        (scope == pik).Should().BeFalse();
        (scope != pik).Should().BeTrue();
    }

    [Fact]
    public void ScopeKey_Equality_WithElementInstanceKey_SameValue_IsTrue()
    {
        var scope = ScopeKey.AssumeExists("789");
        var eik = ElementInstanceKey.AssumeExists("789");
        (scope == eik).Should().BeTrue();
        (scope != eik).Should().BeFalse();
    }

    [Fact]
    public void ScopeKey_Equality_WithElementInstanceKey_DifferentValue_IsFalse()
    {
        var scope = ScopeKey.AssumeExists("789");
        var eik = ElementInstanceKey.AssumeExists("999");
        (scope == eik).Should().BeFalse();
        (scope != eik).Should().BeTrue();
    }

    // ── ResourceKey (ProcessDefinitionKey | DecisionRequirementsKey | FormKey | DecisionDefinitionKey) ─

    [Fact]
    public void ResourceKey_ImplicitConversion_FromProcessDefinitionKey_PreservesValue()
    {
        var pdk = ProcessDefinitionKey.AssumeExists("2251799813683001");
        ResourceKey resource = pdk;
        resource.Value.Should().Be("2251799813683001");
    }

    [Fact]
    public void ResourceKey_ImplicitConversion_FromDecisionRequirementsKey_PreservesValue()
    {
        var drk = DecisionRequirementsKey.AssumeExists("2251799813683002");
        ResourceKey resource = drk;
        resource.Value.Should().Be("2251799813683002");
    }

    [Fact]
    public void ResourceKey_ImplicitConversion_FromFormKey_PreservesValue()
    {
        var fk = FormKey.AssumeExists("2251799813683003");
        ResourceKey resource = fk;
        resource.Value.Should().Be("2251799813683003");
    }

    [Fact]
    public void ResourceKey_ImplicitConversion_FromDecisionDefinitionKey_PreservesValue()
    {
        var ddk = DecisionDefinitionKey.AssumeExists("2251799813683004");
        ResourceKey resource = ddk;
        resource.Value.Should().Be("2251799813683004");
    }

    [Fact]
    public void ResourceKey_Equality_WithProcessDefinitionKey_SameValue_IsTrue()
    {
        var resource = ResourceKey.AssumeExists("100");
        var pdk = ProcessDefinitionKey.AssumeExists("100");
        (resource == pdk).Should().BeTrue();
        (resource != pdk).Should().BeFalse();
    }

    [Fact]
    public void ResourceKey_Equality_WithDecisionDefinitionKey_SameValue_IsTrue()
    {
        var resource = ResourceKey.AssumeExists("200");
        var ddk = DecisionDefinitionKey.AssumeExists("200");
        (resource == ddk).Should().BeTrue();
    }
}
