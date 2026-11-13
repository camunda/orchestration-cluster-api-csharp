using System.Net;
using System.Text;
using System.Text.Json;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Guards the job worker's generic handler-failure path: an exception that is neither
/// <see cref="BpmnErrorException"/> nor <see cref="JobFailureException"/> must auto-fail the job
/// with one retry consumed, and must leave the worker able to take more work.
///
/// <para>Class-of-defect scope: the assertion is about the <em>category</em> "any unhandled
/// exception", not one exception type, so the retry-arithmetic cases run over several unrelated
/// exception types. The retry count is pinned at both ends of <c>Math.Max(0, Retries - 1)</c> —
/// a job that arrives with retries left, and one that arrives with none — because the failure
/// mode of a regression there is a negative retry count sent to the broker. The capacity case
/// exists because the decrement lives in a <c>finally</c>: if a future edit lets an exception
/// escape ahead of it, <c>ActiveJobs</c> never returns to zero and the worker silently wedges at
/// capacity instead of failing anything.</para>
/// </summary>
public class JobWorkerHandlerFailureTests
{
    private static Dictionary<string, string> Config() => new()
    {
        ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
        ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
    };

    public static IEnumerable<object[]> UnhandledExceptions()
    {
        yield return new object[] { new InvalidOperationException("handler blew up") };
        yield return new object[] { new TimeoutException("downstream timed out") };
        yield return new object[] { new FormatException("bad payload") };
        yield return new object[] { new ArgumentException("bad argument") };
    }

    [Theory]
    [MemberData(nameof(UnhandledExceptions))]
    public async Task Unhandled_handler_exception_fails_the_job_with_one_retry_consumed(Exception thrown)
    {
        var handler = new WorkerStubHandler(JobJson(jobKey: "222", retries: 3));
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "failure-test", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = false },
            (_, _) => throw thrown);
        worker.Start();

        var command = await handler.Command.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal("/jobs/222/failure", command.Path);

        using var doc = JsonDocument.Parse(command.Body);
        Assert.Equal(2, doc.RootElement.GetProperty("retries").GetInt32());
        Assert.Equal(thrown.Message, doc.RootElement.GetProperty("errorMessage").GetString());

        // No backoff is requested on this path, so the job is retryable immediately.
        Assert.False(doc.RootElement.TryGetProperty("retryBackOff", out _));
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public async Task Auto_fail_never_sends_a_negative_retry_count(int jobRetries, int expectedRetries)
    {
        var handler = new WorkerStubHandler(JobJson(jobKey: "333", retries: jobRetries));
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "failure-test", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = false },
            (_, _) => throw new InvalidOperationException("handler blew up"));
        worker.Start();

        var command = await handler.Command.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        using var doc = JsonDocument.Parse(command.Body);
        Assert.Equal(expectedRetries, doc.RootElement.GetProperty("retries").GetInt32());
    }

    [Fact]
    public async Task Worker_regains_capacity_and_keeps_polling_after_a_handler_exception()
    {
        var handler = new WorkerStubHandler(JobJson(jobKey: "444", retries: 3));
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "failure-test", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = false },
            (_, _) => throw new InvalidOperationException("handler blew up"));
        worker.Start();

        await handler.Command.WaitAsync(TimeSpan.FromSeconds(5));

        // The decrement runs in the handler task's finally, just after the failure command is
        // answered, so the command arriving does not by itself mean the counter has moved yet.
        // The deadline is a safety net so a regression fails the run instead of hanging it — it
        // is not a correctness signal, and tightening it back into a race helps nobody.
        await WaitUntilAsync(() => worker.ActiveJobs == 0, TimeSpan.FromSeconds(5));

        Assert.Equal(0, worker.ActiveJobs);
        Assert.True(worker.IsRunning);

        // An auto-failed job must not take the poll loop down with it.
        Assert.NotNull(worker.Completion);
        Assert.False(worker.Completion!.IsFaulted);

        await worker.StopAsync(TimeSpan.FromSeconds(1));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException($"Condition not met within {timeout.TotalMilliseconds}ms.");
            await Task.Delay(5);
        }
    }

    private static string JobJson(string jobKey, int retries) => $$"""
        {
            "type": "failure-test",
            "processDefinitionId": "p",
            "processDefinitionVersion": 1,
            "elementId": "task-1",
            "customHeaders": {},
            "worker": "w",
            "retries": {{retries}},
            "deadline": 1700000000000,
            "variables": {},
            "tenantId": "<default>",
            "jobKey": "{{jobKey}}",
            "processInstanceKey": "789012",
            "processDefinitionKey": "345678",
            "elementInstanceKey": "901234",
            "kind": "BPMN_ELEMENT",
            "listenerEventType": "UNSPECIFIED"
        }
        """;

    /// <summary>
    /// Answers the first activation poll with one job and every later poll with an empty list, so
    /// the worker keeps polling. Anything that is not an activation poll is recorded as the
    /// command under test. Routing by path rather than by a response queue keeps the test
    /// independent of how the poll loop and the handler task interleave.
    /// </summary>
    private sealed class WorkerStubHandler(string jobJson) : HttpMessageHandler
    {
        private readonly TaskCompletionSource<(string Path, string Body)> _command =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _polls;

        public Task<(string Path, string Body)> Command => _command.Task;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/jobs/activation", StringComparison.Ordinal))
            {
                var first = Interlocked.Increment(ref _polls) == 1;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        first ? $"{{\"jobs\":[{jobJson}]}}" : "{\"jobs\":[]}",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            _command.TrySetResult((path, body));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
