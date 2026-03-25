// Compilable usage examples for user management operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class UserExamples
{
    #region CreateUser
    public static async Task CreateUserExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateUserAsync(new UserRequest
        {
            Username = "jdoe",
            Name = "Jane Doe",
            Email = "jdoe@example.com",
            Password = "secure-password",
        });

        Console.WriteLine($"User key: {result.UserKey}");
    }
    #endregion CreateUser

    #region CreateAdminUser
    public static async Task CreateAdminUserExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateAdminUserAsync(new UserRequest
        {
            Username = "admin",
            Name = "Admin User",
            Email = "admin@example.com",
            Password = "admin-password",
        });

        Console.WriteLine($"Admin user key: {result.UserKey}");
    }
    #endregion CreateAdminUser

    #region GetUser
    public static async Task GetUserExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetUserAsync(new Username("jdoe"));
        Console.WriteLine($"User: {result.Username}");
    }
    #endregion GetUser

    #region SearchUsers
    public static async Task SearchUsersExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchUsersAsync(new UserSearchQueryRequest());

        foreach (var user in result.Items)
        {
            Console.WriteLine($"User: {user.Username}");
        }
    }
    #endregion SearchUsers

    #region UpdateUser
    public static async Task UpdateUserExample()
    {
        using var client = CamundaClient.Create();

        await client.UpdateUserAsync(
            new Username("jdoe"),
            new UserUpdateRequest
            {
                Name = "Jane Smith",
                Email = "jsmith@example.com",
            });
    }
    #endregion UpdateUser

    #region DeleteUser
    public static async Task DeleteUserExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteUserAsync(new Username("jdoe"));
    }
    #endregion DeleteUser
}
