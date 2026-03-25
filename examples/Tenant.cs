// Compilable usage examples for tenant management operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class TenantExamples
{
    #region CreateTenant
    public static async Task CreateTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateTenantAsync(new TenantCreateRequest
        {
            TenantId = "acme-corp",
            Name = "Acme Corporation",
        });

        Console.WriteLine($"Tenant key: {result.TenantKey}");
    }
    #endregion CreateTenant

    #region GetTenant
    public static async Task GetTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetTenantAsync(new TenantId("acme-corp"));
        Console.WriteLine($"Tenant: {result.Name}");
    }
    #endregion GetTenant

    #region SearchTenants
    public static async Task SearchTenantsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchTenantsAsync(new TenantSearchQueryRequest());

        foreach (var tenant in result.Items)
        {
            Console.WriteLine($"Tenant: {tenant.Name}");
        }
    }
    #endregion SearchTenants

    #region UpdateTenant
    public static async Task UpdateTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.UpdateTenantAsync(
            new TenantId("acme-corp"),
            new TenantUpdateRequest
            {
                Name = "Acme Corp International",
            });
    }
    #endregion UpdateTenant

    #region DeleteTenant
    public static async Task DeleteTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteTenantAsync(new TenantId("acme-corp"));
    }
    #endregion DeleteTenant

    #region AssignUserToTenant
    public static async Task AssignUserToTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignUserToTenantAsync(
            new TenantId("acme-corp"),
            new Username("jdoe"));
    }
    #endregion AssignUserToTenant

    #region UnassignUserFromTenant
    public static async Task UnassignUserFromTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignUserFromTenantAsync(
            new TenantId("acme-corp"),
            new Username("jdoe"));
    }
    #endregion UnassignUserFromTenant

    #region AssignGroupToTenant
    public static async Task AssignGroupToTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignGroupToTenantAsync(
            new TenantId("acme-corp"),
            "engineering");
    }
    #endregion AssignGroupToTenant

    #region UnassignGroupFromTenant
    public static async Task UnassignGroupFromTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignGroupFromTenantAsync(
            new TenantId("acme-corp"),
            "engineering");
    }
    #endregion UnassignGroupFromTenant

    #region AssignRoleToTenant
    public static async Task AssignRoleToTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignRoleToTenantAsync(
            new TenantId("acme-corp"),
            "developer");
    }
    #endregion AssignRoleToTenant

    #region UnassignRoleFromTenant
    public static async Task UnassignRoleFromTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignRoleFromTenantAsync(
            new TenantId("acme-corp"),
            "developer");
    }
    #endregion UnassignRoleFromTenant

    #region AssignClientToTenant
    public static async Task AssignClientToTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignClientToTenantAsync(
            new TenantId("acme-corp"),
            "my-service-account");
    }
    #endregion AssignClientToTenant

    #region UnassignClientFromTenant
    public static async Task UnassignClientFromTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignClientFromTenantAsync(
            new TenantId("acme-corp"),
            "my-service-account");
    }
    #endregion UnassignClientFromTenant

    #region AssignMappingRuleToTenant
    public static async Task AssignMappingRuleToTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignMappingRuleToTenantAsync(
            new TenantId("acme-corp"),
            "rule-123");
    }
    #endregion AssignMappingRuleToTenant

    #region UnassignMappingRuleFromTenant
    public static async Task UnassignMappingRuleFromTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignMappingRuleFromTenantAsync(
            new TenantId("acme-corp"),
            "rule-123");
    }
    #endregion UnassignMappingRuleFromTenant

    #region SearchUsersForTenant
    public static async Task SearchUsersForTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchUsersForTenantAsync(
            new TenantId("acme-corp"),
            new SearchUsersForTenantRequest());

        foreach (var user in result.Items)
        {
            Console.WriteLine($"User: {user.Username}");
        }
    }
    #endregion SearchUsersForTenant

    #region SearchClientsForTenant
    public static async Task SearchClientsForTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchClientsForTenantAsync(
            new TenantId("acme-corp"),
            new SearchClientsForTenantRequest());

        foreach (var c in result.Items)
        {
            Console.WriteLine($"Client: {c.ClientId}");
        }
    }
    #endregion SearchClientsForTenant

    #region SearchGroupIdsForTenant
    public static async Task SearchGroupIdsForTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchGroupIdsForTenantAsync(
            new TenantId("acme-corp"),
            new TenantGroupSearchQueryRequest());

        foreach (var group in result.Items)
        {
            Console.WriteLine($"Group: {group.Name}");
        }
    }
    #endregion SearchGroupIdsForTenant

    #region SearchRolesForTenant
    public static async Task SearchRolesForTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchRolesForTenantAsync(
            new TenantId("acme-corp"),
            new RoleSearchQueryRequest());

        foreach (var role in result.Items)
        {
            Console.WriteLine($"Role: {role.Name}");
        }
    }
    #endregion SearchRolesForTenant

    #region SearchMappingRulesForTenant
    public static async Task SearchMappingRulesForTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchMappingRulesForTenantAsync(
            new TenantId("acme-corp"),
            new MappingRuleSearchQueryRequest());

        foreach (var rule in result.Items)
        {
            Console.WriteLine($"Mapping rule: {rule.MappingRuleId}");
        }
    }
    #endregion SearchMappingRulesForTenant
}
