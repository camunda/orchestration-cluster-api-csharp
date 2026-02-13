using Camunda.Client.Api;
using FluentAssertions;

namespace Camunda.Client.IntegrationTests;

/// <summary>
/// Tests for job activation and completion:
/// deploy → create instance → activate job → complete job.
/// </summary>
[Collection("Camunda")]
public class JobTests(CamundaFixture fixture)
{
    [Fact]
    public async Task CanActivateAndCompleteJob()
    {
        // Deploy & start
        await fixture.DeployResourceAsync("test-process.bpmn");
        var createResult = await fixture.CreateProcessInstanceAsync("integration-test-process");
        var processInstanceKey = createResult.ProcessInstanceKey;

        try
        {
            // Activate job (with retry — may take a moment for the job to appear)
            // Filter to only activate jobs belonging to THIS process instance to
            // avoid picking up leftover jobs from previous test runs.
            ActivateJobsResponse? activation = null;
            ActivatedJobResult? targetJob = null;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                activation = await fixture.Client.ActivateJobsAsync(new JobActivationRequest
                {
                    Type = "integration-test-job",
                    MaxJobsToActivate = 10,
                    Timeout = 60_000,
                    Worker = "integration-test-worker",
                });

                if (activation?.Jobs != null)
                {
                    targetJob = activation.Jobs.FirstOrDefault(
                        j => j.ProcessInstanceKey.ToString() == processInstanceKey.ToString());
                }

                if (targetJob != null)
                    break;

                await Task.Delay(500);
            }

            targetJob.Should().NotBeNull("a job for the created process instance should be available");

            targetJob!.Type.Should().Be("integration-test-job");

            var jobKey = targetJob.JobKey;

            // Complete the job
            await fixture.Client.CompleteJobAsync(jobKey, new JobCompletionRequest
            {
                Variables = new Dictionary<string, object> { ["completed"] = true },
            });

            // Verify process instance completes (no longer ACTIVE)
            var doneDeadline = DateTimeOffset.UtcNow.AddSeconds(15);
            bool completed = false;
            while (DateTimeOffset.UtcNow < doneDeadline)
            {
                var search = await fixture.Client.SearchProcessInstancesAsync(
                    new SearchProcessInstancesRequest());

                completed = search?.Items != null &&
                    HasItemWithKeyAndState(search.Items, processInstanceKey.ToString(), "COMPLETED");

                if (completed)
                    break;
                await Task.Delay(500);
            }

            completed.Should().BeTrue("process instance should reach COMPLETED state after job completion");
        }
        catch
        {
            // Best-effort cleanup
            await fixture.CancelProcessInstanceAsync(processInstanceKey);
            throw;
        }
    }

    [Fact]
    public async Task ActivateJobs_ReturnsEmptyWhenNoJobsAvailable()
    {
        var activation = await fixture.Client.ActivateJobsAsync(new JobActivationRequest
        {
            Type = "nonexistent-job-type-" + Guid.NewGuid().ToString("N"),
            MaxJobsToActivate = 1,
            Timeout = 5_000,
            Worker = "integration-test-worker",
        });

        activation.Should().NotBeNull();
        // Should return empty or null jobs list — not an error
    }

    private static bool HasItemWithKeyAndState(List<ProcessInstanceResult> items, string processInstanceKey, string state) =>
        items.Any(i => i.ProcessInstanceKey.ToString() == processInstanceKey
            && i.State.ToString() == state);
}
