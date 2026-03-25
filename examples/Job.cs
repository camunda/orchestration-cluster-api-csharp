// Compilable usage examples for job operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class JobExamples
{
    #region ActivateJobs
    // <ActivateJobs>
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
    // </ActivateJobs>
    #endregion ActivateJobs

    #region CompleteJob

    // <CompleteJob>
    public static async Task CompleteJobExample()
    {
        using var client = CamundaClient.Create();

        await client.CompleteJobAsync(
            JobKey.AssumeExists("123456"),
            new JobCompletionRequest());
    }
    // </CompleteJob>
    #endregion CompleteJob

    #region FailJob

    // <FailJob>
    public static async Task FailJobExample()
    {
        using var client = CamundaClient.Create();

        await client.FailJobAsync(
            JobKey.AssumeExists("123456"),
            new JobFailRequest
            {
                Retries = 3,
                RetryBackOff = 5000,
                ErrorMessage = "Something went wrong",
            });
    }
    // </FailJob>
    #endregion FailJob

    #region ThrowJobError

    // <ThrowJobError>
    public static async Task ThrowJobErrorExample()
    {
        using var client = CamundaClient.Create();

        await client.ThrowJobErrorAsync(
            JobKey.AssumeExists("123456"),
            new JobErrorRequest
            {
                ErrorCode = "VALIDATION_ERROR",
                ErrorMessage = "Input validation failed",
            });
    }
    // </ThrowJobError>
    #endregion ThrowJobError

    #region UpdateJob

    // <UpdateJob>
    public static async Task UpdateJobExample()
    {
        using var client = CamundaClient.Create();

        await client.UpdateJobAsync(
            JobKey.AssumeExists("123456"),
            new JobUpdateRequest
            {
                Changeset = new JobChangeset { Retries = 3 },
            });
    }
    // </UpdateJob>
    #endregion UpdateJob

    #region SearchJobs

    // <SearchJobs>
    public static async Task SearchJobsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchJobsAsync(new JobSearchQuery());

        foreach (var job in result.Items)
        {
            Console.WriteLine($"Job: {job.JobKey}");
        }
    }
    // </SearchJobs>
    #endregion SearchJobs
}
