// Compilable usage examples for job operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class JobExamples
{
    #region ActivateJobs
    public static async Task ActivateJobsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.ActivateJobsAsync(new JobActivationRequest
        {
            Type = "my-job-type",
            MaxJobsToActivate = 10,
            Timeout = 300000,
            Worker = "my-worker",
        });

        foreach (var job in result.Jobs)
        {
            Console.WriteLine($"Job: {job.JobKey}");
        }
    }
    #endregion ActivateJobs

    #region CompleteJob
    public static async Task CompleteJobExample()
    {
        using var client = CamundaClient.Create();

        await client.CompleteJobAsync(
            new JobKey("123456"),
            new JobCompletionRequest());
    }
    #endregion CompleteJob

    #region FailJob
    public static async Task FailJobExample()
    {
        using var client = CamundaClient.Create();

        await client.FailJobAsync(
            new JobKey("123456"),
            new JobFailRequest
            {
                Retries = 3,
                RetryBackOff = 5000,
                ErrorMessage = "Something went wrong",
            });
    }
    #endregion FailJob

    #region ThrowJobError
    public static async Task ThrowJobErrorExample()
    {
        using var client = CamundaClient.Create();

        await client.ThrowJobErrorAsync(
            new JobKey("123456"),
            new JobErrorRequest
            {
                ErrorCode = "VALIDATION_ERROR",
                ErrorMessage = "Input validation failed",
            });
    }
    #endregion ThrowJobError

    #region UpdateJob
    public static async Task UpdateJobExample()
    {
        using var client = CamundaClient.Create();

        await client.UpdateJobAsync(
            new JobKey("123456"),
            new JobUpdateRequest
            {
                Retries = 3,
            });
    }
    #endregion UpdateJob

    #region SearchJobs
    public static async Task SearchJobsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchJobsAsync(new JobSearchQuery());

        foreach (var job in result.Items)
        {
            Console.WriteLine($"Job: {job.JobKey}");
        }
    }
    #endregion SearchJobs
}
