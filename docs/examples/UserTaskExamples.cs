// Compilable usage examples for user task operations.
// Region tags are referenced by DocFX overwrite files.
#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CS8629 // Nullable value type may be null

using Camunda.Orchestration.Sdk;
using Camunda.Orchestration.Sdk.Api;

namespace Camunda.Orchestration.Sdk.Examples;

internal static class UserTaskExamples
{
    // <CompleteUserTask>
    private static async Task CompleteUserTaskExample()
    {
        using var client = CamundaClient.Create();

        // Find a user task via search
        var tasks = await client.SearchUserTasksAsync(new UserTaskSearchQuery());
        var userTaskKey = tasks.Items![0].UserTaskKey;

        await client.CompleteUserTaskAsync(userTaskKey, new UserTaskCompletionRequest
        {
            Variables = new Dictionary<string, object>
            {
                ["approved"] = true,
                ["comment"] = "Looks good"
            }
        });
    }
    // </CompleteUserTask>

    // <AssignUserTask>
    private static async Task AssignUserTaskExample()
    {
        using var client = CamundaClient.Create();

        // Find a user task via search
        var tasks = await client.SearchUserTasksAsync(new UserTaskSearchQuery());
        var userTaskKey = tasks.Items![0].UserTaskKey;

        await client.AssignUserTaskAsync(userTaskKey, new UserTaskAssignmentRequest
        {
            Assignee = "jane.doe"
        });
    }
    // </AssignUserTask>

    // <UnassignUserTask>
    private static async Task UnassignUserTaskExample()
    {
        using var client = CamundaClient.Create();

        // Find a user task via search
        var tasks = await client.SearchUserTasksAsync(new UserTaskSearchQuery());
        var userTaskKey = tasks.Items![0].UserTaskKey;

        await client.UnassignUserTaskAsync(userTaskKey);
    }
    // </UnassignUserTask>

    // <SearchUserTasks>
    private static async Task SearchUserTasksExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchUserTasksAsync(new UserTaskSearchQuery());

        foreach (var task in result.Items!)
        {
            Console.WriteLine($"Task {task.UserTaskKey}: {task.State}, assignee={task.Assignee}");
        }
    }
    // </SearchUserTasks>

    // <UpdateUserTask>
    private static async Task UpdateUserTaskExample()
    {
        using var client = CamundaClient.Create();

        // Find a user task via search
        var tasks = await client.SearchUserTasksAsync(new UserTaskSearchQuery());
        var userTaskKey = tasks.Items![0].UserTaskKey;

        await client.UpdateUserTaskAsync(userTaskKey, new UserTaskUpdateRequest
        {
            Changeset = new Changeset
            {
                DueDate = DateTimeOffset.UtcNow.AddDays(3),
                Priority = 80
            }
        });
    }
    // </UpdateUserTask>
}
