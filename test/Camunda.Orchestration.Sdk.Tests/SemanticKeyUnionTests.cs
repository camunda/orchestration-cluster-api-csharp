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
        Assert.Equal("2251799813683890", scope.Value);
    }

    [Fact]
    public void ScopeKey_ImplicitConversion_FromElementInstanceKey_PreservesValue()
    {
        var eik = ElementInstanceKey.AssumeExists("2251799813683891");
        ScopeKey scope = eik;
        Assert.Equal("2251799813683891", scope.Value);
    }

    [Fact]
    public void ScopeKey_Equality_WithProcessInstanceKey_SameValue_IsTrue()
    {
        var scope = ScopeKey.AssumeExists("123");
        var pik = ProcessInstanceKey.AssumeExists("123");
        Assert.True(scope == pik);
        Assert.False(scope != pik);
    }

    [Fact]
    public void ScopeKey_Equality_WithProcessInstanceKey_DifferentValue_IsFalse()
    {
        var scope = ScopeKey.AssumeExists("123");
        var pik = ProcessInstanceKey.AssumeExists("456");
        Assert.False(scope == pik);
        Assert.True(scope != pik);
    }

    [Fact]
    public void ScopeKey_Equality_WithElementInstanceKey_SameValue_IsTrue()
    {
        var scope = ScopeKey.AssumeExists("789");
        var eik = ElementInstanceKey.AssumeExists("789");
        Assert.True(scope == eik);
        Assert.False(scope != eik);
    }

    [Fact]
    public void ScopeKey_Equality_WithElementInstanceKey_DifferentValue_IsFalse()
    {
        var scope = ScopeKey.AssumeExists("789");
        var eik = ElementInstanceKey.AssumeExists("999");
        Assert.False(scope == eik);
        Assert.True(scope != eik);
    }

    // ── ResourceKey (ProcessDefinitionKey | DecisionRequirementsKey | FormKey | DecisionDefinitionKey) ─

    [Fact]
    public void ResourceKey_ImplicitConversion_FromProcessDefinitionKey_PreservesValue()
    {
        var pdk = ProcessDefinitionKey.AssumeExists("2251799813683001");
        ResourceKey resource = pdk;
        Assert.Equal("2251799813683001", resource.Value);
    }

    [Fact]
    public void ResourceKey_ImplicitConversion_FromDecisionRequirementsKey_PreservesValue()
    {
        var drk = DecisionRequirementsKey.AssumeExists("2251799813683002");
        ResourceKey resource = drk;
        Assert.Equal("2251799813683002", resource.Value);
    }

    [Fact]
    public void ResourceKey_ImplicitConversion_FromFormKey_PreservesValue()
    {
        var fk = FormKey.AssumeExists("2251799813683003");
        ResourceKey resource = fk;
        Assert.Equal("2251799813683003", resource.Value);
    }

    [Fact]
    public void ResourceKey_ImplicitConversion_FromDecisionDefinitionKey_PreservesValue()
    {
        var ddk = DecisionDefinitionKey.AssumeExists("2251799813683004");
        ResourceKey resource = ddk;
        Assert.Equal("2251799813683004", resource.Value);
    }

    [Fact]
    public void ResourceKey_Equality_WithProcessDefinitionKey_SameValue_IsTrue()
    {
        var resource = ResourceKey.AssumeExists("100");
        var pdk = ProcessDefinitionKey.AssumeExists("100");
        Assert.True(resource == pdk);
        Assert.False(resource != pdk);
    }

    [Fact]
    public void ResourceKey_Equality_WithDecisionDefinitionKey_SameValue_IsTrue()
    {
        var resource = ResourceKey.AssumeExists("200");
        var ddk = DecisionDefinitionKey.AssumeExists("200");
        Assert.True(resource == ddk);
    }
}
