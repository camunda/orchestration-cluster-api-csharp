// Compilable usage examples for authorization operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class AuthorizationExamples
{
    #region CreateAuthorization
    public static async Task CreateAuthorizationExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateAuthorizationAsync(new AuthorizationRequest
        {
            ResourceType = "process-definition",
            Permissions = new[] { "READ", "UPDATE" },
            ResourceId = "my-process",
            OwnerType = "USER",
            OwnerKey = "user@example.com",
        });

        Console.WriteLine($"Authorization key: {result.AuthorizationKey}");
    }
    #endregion CreateAuthorization

    #region GetAuthorization
    public static async Task GetAuthorizationExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetAuthorizationAsync(
            new AuthorizationKey("123456"));

        Console.WriteLine($"Resource type: {result.ResourceType}");
    }
    #endregion GetAuthorization

    #region SearchAuthorizations
    public static async Task SearchAuthorizationsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchAuthorizationsAsync(
            new AuthorizationSearchQuery());

        foreach (var auth in result.Items)
        {
            Console.WriteLine($"Authorization: {auth.AuthorizationKey}");
        }
    }
    #endregion SearchAuthorizations

    #region UpdateAuthorization
    public static async Task UpdateAuthorizationExample()
    {
        using var client = CamundaClient.Create();

        await client.UpdateAuthorizationAsync(
            new AuthorizationKey("123456"),
            new AuthorizationRequest
            {
                ResourceType = "process-definition",
                Permissions = new[] { "READ", "UPDATE", "DELETE" },
                ResourceId = "my-process",
                OwnerType = "USER",
                OwnerKey = "user@example.com",
            });
    }
    #endregion UpdateAuthorization

    #region DeleteAuthorization
    public static async Task DeleteAuthorizationExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteAuthorizationAsync(new AuthorizationKey("123456"));
    }
    #endregion DeleteAuthorization

    #region GetAuthentication
    public static async Task GetAuthenticationExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetAuthenticationAsync();
        Console.WriteLine($"Authenticated user: {result.Username}");
    }
    #endregion GetAuthentication
}
