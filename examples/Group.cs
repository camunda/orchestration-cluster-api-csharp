// Compilable usage examples for group management operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class GroupExamples
{
    #region CreateGroup
    public static async Task CreateGroupExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateGroupAsync(new GroupCreateRequest
        {
            Name = "engineering",
        });

        Console.WriteLine($"Group key: {result.GroupKey}");
    }
    #endregion CreateGroup

    #region GetGroup
    public static async Task GetGroupExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetGroupAsync("engineering");
        Console.WriteLine($"Group: {result.Name}");
    }
    #endregion GetGroup

    #region SearchGroups
    public static async Task SearchGroupsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchGroupsAsync(new GroupSearchQueryRequest());

        foreach (var group in result.Items)
        {
            Console.WriteLine($"Group: {group.Name}");
        }
    }
    #endregion SearchGroups

    #region UpdateGroup
    public static async Task UpdateGroupExample()
    {
        using var client = CamundaClient.Create();

        await client.UpdateGroupAsync("engineering", new GroupUpdateRequest
        {
            Name = "engineering-team",
        });
    }
    #endregion UpdateGroup

    #region DeleteGroup
    public static async Task DeleteGroupExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteGroupAsync("engineering");
    }
    #endregion DeleteGroup

    #region AssignUserToGroup
    public static async Task AssignUserToGroupExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignUserToGroupAsync("engineering", new Username("jdoe"));
    }
    #endregion AssignUserToGroup

    #region UnassignUserFromGroup
    public static async Task UnassignUserFromGroupExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignUserFromGroupAsync("engineering", new Username("jdoe"));
    }
    #endregion UnassignUserFromGroup

    #region AssignClientToGroup
    public static async Task AssignClientToGroupExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignClientToGroupAsync("engineering", "my-service-account");
    }
    #endregion AssignClientToGroup

    #region UnassignClientFromGroup
    public static async Task UnassignClientFromGroupExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignClientFromGroupAsync("engineering", "my-service-account");
    }
    #endregion UnassignClientFromGroup

    #region AssignMappingRuleToGroup
    public static async Task AssignMappingRuleToGroupExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignMappingRuleToGroupAsync("engineering", "rule-123");
    }
    #endregion AssignMappingRuleToGroup

    #region UnassignMappingRuleFromGroup
    public static async Task UnassignMappingRuleFromGroupExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignMappingRuleFromGroupAsync("engineering", "rule-123");
    }
    #endregion UnassignMappingRuleFromGroup

    #region SearchUsersForGroup
    public static async Task SearchUsersForGroupExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchUsersForGroupAsync(
            "engineering",
            new SearchUsersForGroupRequest());

        foreach (var user in result.Items)
        {
            Console.WriteLine($"User: {user.Username}");
        }
    }
    #endregion SearchUsersForGroup

    #region SearchClientsForGroup
    public static async Task SearchClientsForGroupExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchClientsForGroupAsync(
            "engineering",
            new SearchClientsForGroupRequest());

        foreach (var c in result.Items)
        {
            Console.WriteLine($"Client: {c.ClientId}");
        }
    }
    #endregion SearchClientsForGroup

    #region SearchRolesForGroup
    public static async Task SearchRolesForGroupExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchRolesForGroupAsync(
            "engineering",
            new RoleSearchQueryRequest());

        foreach (var role in result.Items)
        {
            Console.WriteLine($"Role: {role.Name}");
        }
    }
    #endregion SearchRolesForGroup

    #region SearchMappingRulesForGroup
    public static async Task SearchMappingRulesForGroupExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchMappingRulesForGroupAsync(
            "engineering",
            new MappingRuleSearchQueryRequest());

        foreach (var rule in result.Items)
        {
            Console.WriteLine($"Mapping rule: {rule.MappingRuleId}");
        }
    }
    #endregion SearchMappingRulesForGroup
}
