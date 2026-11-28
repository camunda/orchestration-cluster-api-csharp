using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Job commands (completeJob, failJob, throwJobError) are not idempotent, so they are retried only
/// when the server says the command was not applied (429/503, or a connection failure before the
/// request was sent), and the worker never follows an unknown-outcome completion with failJob.
/// See https://github.com/camunda/orchestration-cluster-api-csharp/issues/579.
///
/// <para>Class-of-defect scope: every job command is checked against every unknown-outcome
/// status, not only completeJob with 504.</para>
/// </summary>
public class JobCommandRetryTests
{
    private static Dictionary<string, string> Config() => new()
    {
        ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
        ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
        ["CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS"] = "3",
        ["CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS"] = "1",
        ["CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS"] = "2",
    };

    public static IEnumerable<object[]> CommandsByStatus(int[] statuses)
    {
        foreach (var command in new[] { "completion", "failure", "error" })
            foreach (var status in statuses)
                yield return new object[] { command, status };
    }

    private static readonly int[] UnknownOutcomeStatuses = { 500, 502, 504 };
    private static readonly int[] NotAppliedStatuses = { 429, 503 };

    public static IEnumerable<object[]> UnknownOutcome() => CommandsByStatus(UnknownOutcomeStatuses);
    public static IEnumerable<object[]> NotApplied() => CommandsByStatus(NotAppliedStatuses);

    private static Task Invoke(CamundaClient client, string command) => command switch
    {
        "completion" => client.CompleteJobAsync(JobKey.AssumeExists("1"), new JobCompletionRequest()),
        "failure" => client.FailJobAsync(JobKey.AssumeExists("1"), new JobFailRequest { Retries = 1 }),
        "error" => client.ThrowJobErrorAsync(JobKey.AssumeExists("1"), new JobErrorRequest { ErrorCode = "E" }),
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    [Theory]
    [MemberData(nameof(UnknownOutcome))]
    public async Task Job_command_with_unknown_outcome_is_sent_exactly_once(string command, int status)
    {
        var handler = new StubHandler(_ => Status(status));
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        await Assert.ThrowsAnyAsync<Exception>(() => Invoke(client, command));

        Assert.Equal(1, handler.Count($"/v2/jobs/1/{command}"));
    }

    [Theory]
    [MemberData(nameof(NotApplied))]
    public async Task Job_command_not_applied_is_retried(string command, int status)
    {
        var handler = new StubHandler(_ => Status(status));
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        await Assert.ThrowsAnyAsync<Exception>(() => Invoke(client, command));

        Assert.Equal(3, handler.Count($"/v2/jobs/1/{command}"));
    }

    [Theory]
    [InlineData("completion")]
    [InlineData("failure")]
    [InlineData("error")]
    public async Task Job_command_client_timeout_is_not_retried(string command)
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timeout", new TimeoutException()));
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        await Assert.ThrowsAnyAsync<Exception>(() => Invoke(client, command));

        Assert.Equal(1, handler.Count($"/v2/jobs/1/{command}"));
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError, true)]
    [InlineData(HttpRequestError.NameResolutionError, true)]
    [InlineData(HttpRequestError.SecureConnectionError, true)]
    [InlineData(HttpRequestError.ResponseEnded, false)]
    [InlineData(HttpRequestError.Unknown, false)]
    public void Job_command_transport_failures_retry_only_before_send(HttpRequestError error, bool retryable)
    {
        var decision = HttpRetryExecutor.ClassifyJobCommand(new HttpRequestException(error, "boom"));
        Assert.Equal(retryable, decision.Retryable);
    }

    [Fact]
    public async Task Other_operations_keep_retrying_500()
    {
        var handler = new StubHandler(_ => Status(500));
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        await Assert.ThrowsAnyAsync<Exception>(() => client.GetTopologyAsync());

        Assert.Equal(3, handler.Count("/v2/topology"));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(504)]
    [InlineData(404)]
    [InlineData(409)]
    public async Task Worker_does_not_fail_job_after_unknown_outcome_or_not_found(int status)
    {
        var handler = new StubHandler(path => path.EndsWith("/completion", StringComparison.Ordinal)
            ? Status(status)
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        await using var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "t", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = false },
            (_, _) => Task.FromResult<object?>(null));
        worker.Start();

        // Safety-net deadline, not a correctness signal.
        await WaitUntilAsync(() => handler.Count("/v2/jobs/222/completion") == 1 && worker.ActiveJobs == 0);
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, handler.Count("/v2/jobs/222/completion"));
        Assert.Equal(0, handler.Count("/v2/jobs/222/failure"));
    }

    [Theory]
    [InlineData("completion")]
    [InlineData("failure")]
    [InlineData("error")]
    public async Task Job_command_500_resource_exhausted_is_not_retried(string command)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(
                """{"type":"about:blank","title":"x","status":500,"detail":"RESOURCE_EXHAUSTED: busy"}""",
                Encoding.UTF8, "application/problem+json"),
        });
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Invoke(client, command));

        Assert.Equal(1, handler.Count($"/v2/jobs/1/{command}"));
        Assert.True(HttpRetryExecutor.IsOutcomeUnknown(ex));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Worker_does_not_fail_job_when_completion_retries_exhausted(int status)
    {
        var handler = new StubHandler(path => path.EndsWith("/completion", StringComparison.Ordinal)
            ? Status(status)
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        await using var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "t", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = false },
            (_, _) => Task.FromResult<object?>(null));
        worker.Start();

        // Safety-net deadline, not a correctness signal.
        await WaitUntilAsync(() => handler.Count("/v2/jobs/222/completion") == 3 && worker.ActiveJobs == 0);
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(0, handler.Count("/v2/jobs/222/failure"));
    }

    [Fact]
    public async Task Worker_does_not_fail_job_after_404_http_request_exception()
    {
        var handler = new StubHandler(path => path.EndsWith("/completion", StringComparison.Ordinal)
            ? throw new HttpRequestException("not found", null, HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        await using var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "t", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = false },
            (_, _) => Task.FromResult<object?>(null));
        worker.Start();

        // Safety-net deadline, not a correctness signal.
        await WaitUntilAsync(() => handler.Count("/v2/jobs/222/completion") == 1 && worker.ActiveJobs == 0);
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, handler.Count("/v2/jobs/222/completion"));
        Assert.Equal(0, handler.Count("/v2/jobs/222/failure"));
    }

    [Fact]
    public async Task Worker_fails_job_when_completion_is_rejected_permanently()
    {
        var handler = new StubHandler(path => path.EndsWith("/completion", StringComparison.Ordinal)
            ? Status(400)
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        await using var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "t", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = false },
            (_, _) => Task.FromResult<object?>(null));
        worker.Start();

        // Safety-net deadline, not a correctness signal.
        await WaitUntilAsync(() => handler.Count("/v2/jobs/222/failure") == 1 && worker.ActiveJobs == 0);
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, handler.Count("/v2/jobs/222/completion"));
        Assert.Equal(1, handler.Count("/v2/jobs/222/failure"));
    }

    [Fact]
    public async Task Worker_retries_completion_on_503()
    {
        var completions = 0;
        var handler = new StubHandler(path => path.EndsWith("/completion", StringComparison.Ordinal)
            ? (Interlocked.Increment(ref completions) < 3 ? Status(503) : new HttpResponseMessage(HttpStatusCode.NoContent))
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        await using var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "t", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = false },
            (_, _) => Task.FromResult<object?>(null));
        worker.Start();

        await WaitUntilAsync(() => handler.Count("/v2/jobs/222/completion") == 3 && worker.ActiveJobs == 0);
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(3, handler.Count("/v2/jobs/222/completion"));
        Assert.Equal(0, handler.Count("/v2/jobs/222/failure"));
    }

    private static HttpResponseMessage Status(int status) => new((HttpStatusCode)status)
    {
        Content = new StringContent(
            $$"""{"type":"about:blank","title":"x","status":{{status}},"detail":"x"}""",
            Encoding.UTF8, "application/problem+json"),
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Condition not met within 5s.");
            await Task.Delay(5);
        }
    }

    private const string JobJson = """
        {"type":"t","processDefinitionId":"p","processDefinitionVersion":1,"elementId":"e",
         "customHeaders":{},"worker":"w","retries":3,"deadline":1700000000000,"variables":{},
         "tenantId":"<default>","jobKey":"222","processInstanceKey":"789012",
         "processDefinitionKey":"345678","elementInstanceKey":"901234","kind":"BPMN_ELEMENT",
         "listenerEventType":"UNSPECIFIED"}
        """;

    private sealed class StubHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, int> _counts = new();
        private int _polls;

        public int Count(string path) => _counts.TryGetValue(path, out var n) ? n : 0;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/jobs/activation", StringComparison.Ordinal))
            {
                var first = Interlocked.Increment(ref _polls) == 1;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(first ? $"{{\"jobs\":[{JobJson}]}}" : "{\"jobs\":[]}",
                        Encoding.UTF8, "application/json"),
                });
            }
            _counts.AddOrUpdate(path, 1, (_, n) => n + 1);
            return Task.FromResult(respond(path));
        }
    }
}
