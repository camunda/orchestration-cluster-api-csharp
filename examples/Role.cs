// Compilable usage examples for role management operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class RoleExamples
{
    #region CreateRole
    public static async Task CreateRoleExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateRoleAsync(new RoleCreateRequest
        {
            Name = "developer",
        });

        Console.WriteLine($"Role key: {result.RoleKey}");
    }
    #endregion CreateRole

    #region GetRole
    public static async Task GetRoleExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetRoleAsync("developer");
        Console.WriteLine($"Role: {result.Name}");
    }
    #endregion GetRole

    #region SearchRoles
    public static async Task SearchRolesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchRolesAsync(new RoleSearchQueryRequest());

        foreach (var role in result.Items)
        {
            Console.WriteLine($"Role: {role.Name}");
        }
    }
    #endregion SearchRoles

    #region UpdateRole
    public static async Task UpdateRoleExample()
    {
        using var client = CamundaClient.Create();

        await client.UpdateRoleAsync("developer", new RoleUpdateRequest
        {
            Name = "senior-developer",
        });
    }
    #endregion UpdateRole

    #region DeleteRole
    public static async Task DeleteRoleExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteRoleAsync("developer");
    }
    #endregion DeleteRole

    #region AssignRoleToUser
    public static async Task AssignRoleToUserExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignRoleToUserAsync("developer", new Username("jdoe"));
    }
    #endregion AssignRoleToUser

    #region UnassignRoleFromUser
    public static async Task UnassignRoleFromUserExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignRoleFromUserAsync("developer", new Username("jdoe"));
    }
    #endregion UnassignRoleFromUser

    #region AssignRoleToGroup
    public static async Task AssignRoleToGroupExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignRoleToGroupAsync("developer", "engineering");
    }
    #endregion AssignRoleToGroup

    #region UnassignRoleFromGroup
    public static async Task UnassignRoleFromGroupExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignRoleFromGroupAsync("developer", "engineering");
    }
    #endregion UnassignRoleFromGroup

    #region AssignRoleToClient
    public static async Task AssignRoleToClientExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignRoleToClientAsync("developer", "my-service-account");
    }
    #endregion AssignRoleToClient

    #region UnassignRoleFromClient
    public static async Task UnassignRoleFromClientExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignRoleFromClientAsync("developer", "my-service-account");
    }
    #endregion UnassignRoleFromClient

    #region AssignRoleToMappingRule
    public static async Task AssignRoleToMappingRuleExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignRoleToMappingRuleAsync("developer", "rule-123");
    }
    #endregion AssignRoleToMappingRule

    #region UnassignRoleFromMappingRule
    public static async Task UnassignRoleFromMappingRuleExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignRoleFromMappingRuleAsync("developer", "rule-123");
    }
    #endregion UnassignRoleFromMappingRule

    #region SearchUsersForRole
    public static async Task SearchUsersForRoleExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchUsersForRoleAsync(
            "developer",
            new SearchUsersForRoleRequest());

        foreach (var user in result.Items)
        {
            Console.WriteLine($"User: {user.Username}");
        }
    }
    #endregion SearchUsersForRole

    #region SearchGroupsForRole
    public static async Task SearchGroupsForRoleExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchGroupsForRoleAsync(
            "developer",
            new RoleGroupSearchQueryRequest());

        foreach (var group in result.Items)
        {
            Console.WriteLine($"Group: {group.Name}");
        }
    }
    #endregion SearchGroupsForRole

    #region SearchClientsForRole
    public static async Task SearchClientsForRoleExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchClientsForRoleAsync(
            "developer",
            new SearchClientsForRoleRequest());

        foreach (var c in result.Items)
        {
            Console.WriteLine($"Client: {c.ClientId}");
        }
    }
    #endregion SearchClientsForRole

    #region SearchMappingRulesForRole
    public static async Task SearchMappingRulesForRoleExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchMappingRulesForRoleAsync(
            "developer",
            new MappingRuleSearchQueryRequest());

        foreach (var rule in result.Items)
        {
            Console.WriteLine($"Mapping rule: {rule.MappingRuleId}");
        }
    }
    #endregion SearchMappingRulesForRole
}
