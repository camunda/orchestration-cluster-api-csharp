// Compilable usage examples for user task operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class UserTaskExamples
{
    #region AssignUserTask
    // <AssignUserTask>
    public static async Task AssignUserTaskExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignUserTaskAsync(
            UserTaskKey.AssumeExists("123456"),
            new UserTaskAssignmentRequest
            {
                Assignee = "user@example.com",
            });
    }
    // </AssignUserTask>
    #endregion AssignUserTask

    #region CompleteUserTask

    // <CompleteUserTask>
    public static async Task CompleteUserTaskExample()
    {
        using var client = CamundaClient.Create();

        await client.CompleteUserTaskAsync(
            UserTaskKey.AssumeExists("123456"),
            new UserTaskCompletionRequest());
    }
    // </CompleteUserTask>
    #endregion CompleteUserTask

    #region UnassignUserTask

    // <UnassignUserTask>
    public static async Task UnassignUserTaskExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignUserTaskAsync(UserTaskKey.AssumeExists("123456"));
    }
    // </UnassignUserTask>
    #endregion UnassignUserTask

    #region GetUserTask

    // <GetUserTask>
    public static async Task GetUserTaskExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetUserTaskAsync(UserTaskKey.AssumeExists("123456"));
        Console.WriteLine($"User task: {result.UserTaskKey}");
    }
    // </GetUserTask>
    #endregion GetUserTask

    #region UpdateUserTask

    // <UpdateUserTask>
    public static async Task UpdateUserTaskExample()
    {
        using var client = CamundaClient.Create();

        await client.UpdateUserTaskAsync(
            UserTaskKey.AssumeExists("123456"),
            new UserTaskUpdateRequest());
    }
    // </UpdateUserTask>
    #endregion UpdateUserTask

    #region GetUserTaskForm

    // <GetUserTaskForm>
    public static async Task GetUserTaskFormExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetUserTaskFormAsync(UserTaskKey.AssumeExists("123456"));
        Console.WriteLine($"Form: {result.FormKey}");
    }
    // </GetUserTaskForm>
    #endregion GetUserTaskForm

    #region SearchUserTasks

    // <SearchUserTasks>
    public static async Task SearchUserTasksExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchUserTasksAsync(new UserTaskSearchQuery());

        foreach (var task in result.Items)
        {
            Console.WriteLine($"User task: {task.UserTaskKey}");
        }
    }
    // </SearchUserTasks>
    #endregion SearchUserTasks

    #region SearchUserTaskVariables

    // <SearchUserTaskVariables>
    public static async Task SearchUserTaskVariablesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchUserTaskVariablesAsync(
            UserTaskKey.AssumeExists("123456"),
            new SearchUserTaskVariablesRequest());

        foreach (var variable in result.Items)
        {
            Console.WriteLine($"Variable: {variable.Name}");
        }
    }
    // </SearchUserTaskVariables>
    #endregion SearchUserTaskVariables

    #region SearchUserTaskAuditLogs

    // <SearchUserTaskAuditLogs>
    public static async Task SearchUserTaskAuditLogsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchUserTaskAuditLogsAsync(
            UserTaskKey.AssumeExists("123456"),
            new UserTaskAuditLogSearchQueryRequest());

        foreach (var log in result.Items)
        {
            Console.WriteLine($"Audit log: {log.AuditLogKey}");
        }
    }
    // </SearchUserTaskAuditLogs>
    #endregion SearchUserTaskAuditLogs
}
