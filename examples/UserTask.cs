// Compilable usage examples for user task operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class UserTaskExamples
{
    #region AssignUserTask
    public static async Task AssignUserTaskExample()
    {
        using var client = CamundaClient.Create();

        await client.AssignUserTaskAsync(
            new UserTaskKey("123456"),
            new UserTaskAssignmentRequest
            {
                Assignee = "user@example.com",
            });
    }
    #endregion AssignUserTask

    #region CompleteUserTask
    public static async Task CompleteUserTaskExample()
    {
        using var client = CamundaClient.Create();

        await client.CompleteUserTaskAsync(
            new UserTaskKey("123456"),
            new UserTaskCompletionRequest());
    }
    #endregion CompleteUserTask

    #region UnassignUserTask
    public static async Task UnassignUserTaskExample()
    {
        using var client = CamundaClient.Create();

        await client.UnassignUserTaskAsync(new UserTaskKey("123456"));
    }
    #endregion UnassignUserTask

    #region GetUserTask
    public static async Task GetUserTaskExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetUserTaskAsync(new UserTaskKey("123456"));
        Console.WriteLine($"User task: {result.UserTaskKey}");
    }
    #endregion GetUserTask

    #region UpdateUserTask
    public static async Task UpdateUserTaskExample()
    {
        using var client = CamundaClient.Create();

        await client.UpdateUserTaskAsync(
            new UserTaskKey("123456"),
            new UserTaskUpdateRequest());
    }
    #endregion UpdateUserTask

    #region GetUserTaskForm
    public static async Task GetUserTaskFormExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetUserTaskFormAsync(new UserTaskKey("123456"));
        Console.WriteLine($"Form: {result.FormKey}");
    }
    #endregion GetUserTaskForm

    #region SearchUserTasks
    public static async Task SearchUserTasksExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchUserTasksAsync(new UserTaskSearchQuery());

        foreach (var task in result.Items)
        {
            Console.WriteLine($"User task: {task.UserTaskKey}");
        }
    }
    #endregion SearchUserTasks

    #region SearchUserTaskVariables
    public static async Task SearchUserTaskVariablesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchUserTaskVariablesAsync(
            new UserTaskKey("123456"),
            new SearchUserTaskVariablesRequest());

        foreach (var variable in result.Items)
        {
            Console.WriteLine($"Variable: {variable.Name}");
        }
    }
    #endregion SearchUserTaskVariables

    #region SearchUserTaskAuditLogs
    public static async Task SearchUserTaskAuditLogsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchUserTaskAuditLogsAsync(
            new UserTaskKey("123456"),
            new UserTaskAuditLogSearchQueryRequest());

        foreach (var log in result.Items)
        {
            Console.WriteLine($"Audit log: {log.AuditLogKey}");
        }
    }
    #endregion SearchUserTaskAuditLogs
}
