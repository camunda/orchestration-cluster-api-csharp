// Compilable usage examples for mapping rule operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class MappingRuleExamples
{
    #region CreateMappingRule
    public static async Task CreateMappingRuleExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateMappingRuleAsync(new MappingRuleCreateRequest
        {
            ClaimName = "groups",
            ClaimValue = "engineering",
            Name = "Engineering Group Mapping",
        });

        Console.WriteLine($"Mapping rule: {result.MappingRuleId}");
    }
    #endregion CreateMappingRule

    #region GetMappingRule
    public static async Task GetMappingRuleExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetMappingRuleAsync("rule-123");
        Console.WriteLine($"Mapping rule: {result.Name}");
    }
    #endregion GetMappingRule

    #region SearchMappingRule
    public static async Task SearchMappingRuleExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchMappingRuleAsync(
            new MappingRuleSearchQueryRequest());

        foreach (var rule in result.Items)
        {
            Console.WriteLine($"Mapping rule: {rule.Name}");
        }
    }
    #endregion SearchMappingRule

    #region UpdateMappingRule
    public static async Task UpdateMappingRuleExample()
    {
        using var client = CamundaClient.Create();

        await client.UpdateMappingRuleAsync("rule-123", new MappingRuleUpdateRequest
        {
            ClaimName = "groups",
            ClaimValue = "senior-engineering",
            Name = "Senior Engineering Mapping",
        });
    }
    #endregion UpdateMappingRule

    #region DeleteMappingRule
    public static async Task DeleteMappingRuleExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteMappingRuleAsync("rule-123");
    }
    #endregion DeleteMappingRule
}
