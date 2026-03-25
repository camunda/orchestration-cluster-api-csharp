// Compilable usage examples for tenant management operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class TenantExamples
{
    #region CreateTenant
    // <CreateTenant>
    public static async Task CreateTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateTenantAsync(new TenantCreateRequest
        {
            TenantId = "acme-corp",
            Name = "Acme Corporation",
        });

        Console.WriteLine($"Tenant key: {result.TenantId}");
    }
    // </CreateTenant>
    #endregion CreateTenant

    #region GetTenant

    // <GetTenant>
    public static async Task GetTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetTenantAsync(TenantId.AssumeExists("acme-corp"));
        Console.WriteLine($"Tenant: {result.Name}");
    }
    // </GetTenant>
    #endregion GetTenant

    #region SearchTenants

    // <SearchTenants>
    public static async Task SearchTenantsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchTenantsAsync(new TenantSearchQueryRequest());

        foreach (var tenant in result.Items)
        {
            Console.WriteLine($"Tenant: {tenant.Name}");
        }
    }
    // </SearchTenants>
    #endregion SearchTenants

    #region UpdateTenant

    // <UpdateTenant>
    public static async Task UpdateTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.UpdateTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            new TenantUpdateRequest
            {
                Name = "Acme Corp International",
            });
    }
    // </UpdateTenant>
    #endregion UpdateTenant

    #region DeleteTenant

    // <DeleteTenant>
    public static async Task DeleteTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteTenantAsync(TenantId.AssumeExists("acme-corp"));
    }
    // </DeleteTenant>
    #endregion DeleteTenant

    #region AssignUserToTenant

    // <AssignUserToTenant>
    public static async Task AssignUserToTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignUserToTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            Username.AssumeExists("jdoe"));
    }
    // </AssignUserToTenant>
    #endregion AssignUserToTenant

    #region UnassignUserFromTenant

    // <UnassignUserFromTenant>
    public static async Task UnassignUserFromTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignUserFromTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            Username.AssumeExists("jdoe"));
    }
    // </UnassignUserFromTenant>
    #endregion UnassignUserFromTenant

    #region AssignGroupToTenant

    // <AssignGroupToTenant>
    public static async Task AssignGroupToTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignGroupToTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            "engineering");
    }
    // </AssignGroupToTenant>
    #endregion AssignGroupToTenant

    #region UnassignGroupFromTenant

    // <UnassignGroupFromTenant>
    public static async Task UnassignGroupFromTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignGroupFromTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            "engineering");
    }
    // </UnassignGroupFromTenant>
    #endregion UnassignGroupFromTenant

    #region AssignRoleToTenant

    // <AssignRoleToTenant>
    public static async Task AssignRoleToTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignRoleToTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            "developer");
    }
    // </AssignRoleToTenant>
    #endregion AssignRoleToTenant

    #region UnassignRoleFromTenant

    // <UnassignRoleFromTenant>
    public static async Task UnassignRoleFromTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignRoleFromTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            "developer");
    }
    // </UnassignRoleFromTenant>
    #endregion UnassignRoleFromTenant

    #region AssignClientToTenant

    // <AssignClientToTenant>
    public static async Task AssignClientToTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignClientToTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            "my-service-account");
    }
    // </AssignClientToTenant>
    #endregion AssignClientToTenant

    #region UnassignClientFromTenant

    // <UnassignClientFromTenant>
    public static async Task UnassignClientFromTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignClientFromTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            "my-service-account");
    }
    // </UnassignClientFromTenant>
    #endregion UnassignClientFromTenant

    #region AssignMappingRuleToTenant

    // <AssignMappingRuleToTenant>
    public static async Task AssignMappingRuleToTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignMappingRuleToTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            "rule-123");
    }
    // </AssignMappingRuleToTenant>
    #endregion AssignMappingRuleToTenant

    #region UnassignMappingRuleFromTenant

    // <UnassignMappingRuleFromTenant>
    public static async Task UnassignMappingRuleFromTenantExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignMappingRuleFromTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            "rule-123");
    }
    // </UnassignMappingRuleFromTenant>
    #endregion UnassignMappingRuleFromTenant

    #region SearchUsersForTenant

    // <SearchUsersForTenant>
    public static async Task SearchUsersForTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchUsersForTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            new SearchUsersForTenantRequest());

        foreach (var user in result.Items)
        {
            Console.WriteLine($"User: {user.Username}");
        }
    }
    // </SearchUsersForTenant>
    #endregion SearchUsersForTenant

    #region SearchClientsForTenant

    // <SearchClientsForTenant>
    public static async Task SearchClientsForTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchClientsForTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            new SearchClientsForTenantRequest());

        foreach (var c in result.Items)
        {
            Console.WriteLine($"Client: {c.ClientId}");
        }
    }
    // </SearchClientsForTenant>
    #endregion SearchClientsForTenant

    #region SearchGroupIdsForTenant

    // <SearchGroupIdsForTenant>
    public static async Task SearchGroupIdsForTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchGroupIdsForTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            new TenantGroupSearchQueryRequest());

        foreach (var group in result.Items)
        {
            Console.WriteLine($"Group: {group.GroupId}");
        }
    }
    // </SearchGroupIdsForTenant>
    #endregion SearchGroupIdsForTenant

    #region SearchRolesForTenant

    // <SearchRolesForTenant>
    public static async Task SearchRolesForTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchRolesForTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            new RoleSearchQueryRequest());

        foreach (var role in result.Items)
        {
            Console.WriteLine($"Role: {role.Name}");
        }
    }
    // </SearchRolesForTenant>
    #endregion SearchRolesForTenant

    #region SearchMappingRulesForTenant

    // <SearchMappingRulesForTenant>
    public static async Task SearchMappingRulesForTenantExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchMappingRulesForTenantAsync(
            TenantId.AssumeExists("acme-corp"),
            new MappingRuleSearchQueryRequest());

        foreach (var rule in result.Items)
        {
            Console.WriteLine($"Mapping rule: {rule.MappingRuleId}");
        }
    }
    // </SearchMappingRulesForTenant>
    #endregion SearchMappingRulesForTenant
}
