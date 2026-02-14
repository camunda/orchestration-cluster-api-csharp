// Compilable usage examples for job operations and workers.
// Region tags are referenced by DocFX overwrite files.
#pragma warning disable CS8321 // Local function is declared but never used

using Camunda.Orchestration.Sdk;
using Camunda.Orchestration.Sdk.Api;
using Camunda.Orchestration.Sdk.Runtime;

namespace Camunda.Orchestration.Sdk.Examples;

internal static class JobExamples
{
    // <ActivateJobs>
    static async Task ActivateJobsExample()
    {
        using var client = Camunda.CreateClient();

        var response = await client.ActivateJobsAsync(new JobActivationRequest
        {
            Type = "send-email",
            MaxJobsToActivate = 10,
            Timeout = 300_000, // 5 minutes
            Worker = "email-worker-1"
        });

        foreach (var job in response.Jobs)
        {
            Console.WriteLine($"Job {job.JobKey}: {job.Type}");
        }
    }
    // </ActivateJobs>

    // <CompleteJob>
    static async Task CompleteJobExample()
    {
        using var client = Camunda.CreateClient();

        // Activate jobs and get a JobKey from the response
        var activated = await client.ActivateJobsAsync(new JobActivationRequest
        {
            Type = "send-email",
            MaxJobsToActivate = 1,
            Timeout = 300_000,
            Worker = "email-worker-1"
        });
        var jobKey = activated.Jobs[0].JobKey;

        await client.CompleteJobAsync(jobKey, new JobCompletionRequest
        {
            Variables = new Dictionary<string, object>
            {
                ["emailSent"] = true
            }
        });
    }
    // </CompleteJob>

    // <FailJob>
    static async Task FailJobExample()
    {
        using var client = Camunda.CreateClient();

        // Activate a job and get its JobKey from the response
        var activated = await client.ActivateJobsAsync(new JobActivationRequest
        {
            Type = "send-email",
            MaxJobsToActivate = 1,
            Timeout = 300_000,
            Worker = "email-worker-1"
        });
        var jobKey = activated.Jobs[0].JobKey;

        await client.FailJobAsync(jobKey, new JobFailRequest
        {
            Retries = 2,
            ErrorMessage = "SMTP server unreachable",
            RetryBackOff = 30_000 // 30 seconds
        });
    }
    // </FailJob>

    // <ThrowJobError>
    static async Task ThrowJobErrorExample()
    {
        using var client = Camunda.CreateClient();

        // Activate a job and get its JobKey from the response
        var activated = await client.ActivateJobsAsync(new JobActivationRequest
        {
            Type = "send-email",
            MaxJobsToActivate = 1,
            Timeout = 300_000,
            Worker = "email-worker-1"
        });
        var jobKey = activated.Jobs[0].JobKey;

        await client.ThrowJobErrorAsync(jobKey, new JobErrorRequest
        {
            ErrorCode = "INVALID_ADDRESS",
            ErrorMessage = "Recipient email address is invalid"
        });
    }
    // </ThrowJobError>

    // <JobWorker>
    #pragma warning disable CS1998 // Async method lacks await (handler is simple for demo purposes)
    static async Task JobWorkerExample()
    {
        await using var client = Camunda.CreateClient();

        client.CreateJobWorker(
            new JobWorkerConfig
            {
                JobType = "send-email",
                JobTimeoutMs = 300_000, // 5 minutes
                MaxConcurrentJobs = 5,
                PollIntervalMs = 5_000
            },
            async (job, ct) =>
            {
                var recipient = job.GetVariables<Dictionary<string, string>>()?
                    .GetValueOrDefault("recipient");
                Console.WriteLine($"Sending email to {recipient}");
            });

        // Run all registered workers until cancelled
        using var cts = new CancellationTokenSource();
        await client.RunWorkersAsync(ct: cts.Token);
    }
    // </JobWorker>

    // <SearchJobs>
    static async Task SearchJobsExample()
    {
        using var client = Camunda.CreateClient();

        var result = await client.SearchJobsAsync(new JobSearchQuery());

        foreach (var job in result.Items!)
        {
            Console.WriteLine($"Job {job.JobKey}: type={job.Type}, state={job.State}");
        }
    }
    // </SearchJobs>
}
