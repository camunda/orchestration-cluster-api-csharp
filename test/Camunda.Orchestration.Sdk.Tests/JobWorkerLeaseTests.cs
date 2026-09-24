using System.Net;
using System.Text.Json;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// End-to-end guard for the worker-facing half of the lease feature: opting in on activation,
/// rejecting a server that ignores the lease, and threading the returned token onto every fenced
/// command.
///
/// <para>Class-of-defect scope: a leased job's token must ride the complete, fail, and throw-error
/// commands, and it must not be written into a caller-owned request object that a handler might
/// reuse. Each command path is exercised against the captured HTTP body; the copy-on-complete path
/// additionally asserts the caller's object is left untouched, since mutating it would leak one
/// job's token onto the next.</para>
/// </summary>
public class JobWorkerLeaseTests
{
    private static string JobJson(string jobKey, string? leaseToken) => $$"""
        {
            "type": "lease-test",
            "processDefinitionId": "p",
            "processDefinitionVersion": 1,
            "elementId": "task-1",
            "customHeaders": {},
            "worker": "w",
            "retries": 3,
            "deadline": 1700000000000,
            "variables": {},
            "tenantId": "<default>",
            "jobKey": "{{jobKey}}",
            "processInstanceKey": "789012",
            "processDefinitionKey": "345678",
            "elementInstanceKey": "901234",
            "kind": "BPMN_ELEMENT",
            "listenerEventType": "UNSPECIFIED"{{(leaseToken is null ? "" : $",\n            \"jobLeaseToken\": \"{leaseToken}\"")}}
        }
        """;

    private static Dictionary<string, string> Config() => new()
    {
        ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
        ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
    };

    private static CamundaClient NewClient(MockHttpMessageHandler handler) => new(new CamundaOptions
    {
        Config = Config(),
        HttpMessageHandler = handler,
    });

    [Fact]
    public async Task Activation_omits_withLease_when_the_worker_did_not_opt_in()
    {
        var body = await CaptureActivationBodyAsync(withLease: false);

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.TryGetProperty("withLease", out _));
    }

    [Fact]
    public async Task Activation_sends_withLease_true_when_the_worker_opted_in()
    {
        var body = await CaptureActivationBodyAsync(withLease: true);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("withLease", out var wl));
        Assert.True(wl.GetBoolean());
    }

    [Fact]
    public async Task RunWorkersAsync_keeps_running_when_one_worker_stops_cleanly()
    {
        // Regression: RunWorkersAsync must keep every other worker alive until shutdown or a
        // fault. A worker stopped directly completes its poll task cleanly, which must NOT be
        // mistaken for a fault and used to tear down the rest.
        var mock = new AlwaysEmptyJobsHandler();
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = mock });

        var a = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "lease-test", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = true },
            (_, _) => Task.FromResult<object?>(null));
        client.CreateJobWorker(
            new JobWorkerConfig { JobType = "lease-test", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = true },
            (_, _) => Task.FromResult<object?>(null));

        using var cts = new CancellationTokenSource();
        var run = client.RunWorkersAsync(TimeSpan.FromMilliseconds(50), cts.Token);

        // Stop worker A directly; its poll task completes cleanly.
        await a.StopAsync(TimeSpan.FromSeconds(1));

        // A settle window, not a correctness signal: if the clean completion were treated as a
        // fault, run would already have completed by now.
        await Task.Delay(250);
        Assert.False(run.IsCompleted);

        // Only shutdown ends it.
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunWorkersAsync_waits_for_cancellation_when_no_worker_is_polling()
    {
        // A worker exists but was never started, so there is no poll task to observe. The
        // wait-until-cancellation contract must still hold rather than returning immediately.
        var mock = new AlwaysEmptyJobsHandler();
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = mock });

        client.CreateJobWorker(
            new JobWorkerConfig { JobType = "lease-test", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = false },
            (_, _) => Task.FromResult<object?>(null));

        using var cts = new CancellationTokenSource();
        var run = client.RunWorkersAsync(TimeSpan.FromMilliseconds(50), cts.Token);

        // A settle window, not a correctness signal: with no pending poll task the buggy path
        // returns immediately, so run would already have completed by now.
        await Task.Delay(250);
        Assert.False(run.IsCompleted);

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunWorkersAsync_surfaces_LeaseNotHonored_when_a_leased_job_arrives_without_a_token()
    {
        var handler = new MockHttpMessageHandler();
        // Server ignores the lease: returns a job with no token even though withLease was requested.
        handler.Enqueue(HttpStatusCode.OK, $"{{\"jobs\":[{JobJson("111", leaseToken: null)}]}}");
        for (var i = 0; i < 8; i++)
            handler.Enqueue(HttpStatusCode.OK, "{\"jobs\":[]}");

        using var client = NewClient(handler);
        var handlerRan = false;
        client.CreateJobWorker(
            new JobWorkerConfig { JobType = "lease-test", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = true, WithLease = true },
            (_, _) => { handlerRan = true; return Task.FromResult<object?>(null); });

        var run = client.RunWorkersAsync(TimeSpan.FromMilliseconds(50));

        var ex = await Assert.ThrowsAsync<LeaseNotHonoredException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("111", ex.JobKey);
        // The unfenced job must never reach the handler.
        Assert.False(handlerRan);
    }

    [Fact]
    public async Task RunWorkersAsync_surfaces_LeaseNotHonored_when_a_leased_job_arrives_with_a_malformed_token()
    {
        var handler = new MockHttpMessageHandler();
        // Server returns a malformed (empty) lease token; strict JobLeaseToken deserialization
        // rejects it deep in the client. That must surface as the lease fault, not a silently
        // retried generic poll error.
        handler.Enqueue(HttpStatusCode.OK, $"{{\"jobs\":[{JobJson("222", leaseToken: "")}]}}");
        for (var i = 0; i < 8; i++)
            handler.Enqueue(HttpStatusCode.OK, "{\"jobs\":[]}");

        using var client = NewClient(handler);
        var handlerRan = false;
        client.CreateJobWorker(
            new JobWorkerConfig { JobType = "lease-test", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = true, WithLease = true },
            (_, _) => { handlerRan = true; return Task.FromResult<object?>(null); });

        var run = client.RunWorkersAsync(TimeSpan.FromMilliseconds(50));

        var ex = await Assert.ThrowsAsync<LeaseNotHonoredException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("withLease", ex.RequestFlag);
        Assert.False(handlerRan);
    }

    [Fact]
    public async Task RunWorkersAsync_surfaces_LeaseNotHonored_when_a_leased_job_arrives_with_a_wrong_typed_token()
    {
        var handler = new MockHttpMessageHandler();
        // Server returns a lease token of the wrong JSON type (a number). Strict JobLeaseToken
        // deserialization throws a JsonException (not the empty-string ArgumentException), which
        // must still be classified as a lease fault rather than a generic retried poll error.
        var wrongTypedJob = JobJson("333", leaseToken: null).Replace(
            "\"kind\": \"BPMN_ELEMENT\"",
            "\"jobLeaseToken\": 123,\n            \"kind\": \"BPMN_ELEMENT\"",
            StringComparison.Ordinal);
        handler.Enqueue(HttpStatusCode.OK, $"{{\"jobs\":[{wrongTypedJob}]}}");
        for (var i = 0; i < 8; i++)
            handler.Enqueue(HttpStatusCode.OK, "{\"jobs\":[]}");

        using var client = NewClient(handler);
        var handlerRan = false;
        client.CreateJobWorker(
            new JobWorkerConfig { JobType = "lease-test", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = true, WithLease = true },
            (_, _) => { handlerRan = true; return Task.FromResult<object?>(null); });

        var run = client.RunWorkersAsync(TimeSpan.FromMilliseconds(50));

        var ex = await Assert.ThrowsAsync<LeaseNotHonoredException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("withLease", ex.RequestFlag);
        Assert.False(handlerRan);
    }

    [Fact]
    public async Task Completion_carries_the_lease_token_without_mutating_a_caller_owned_request()
    {
        var callerRequest = new JobCompletionRequest { Variables = new { ok = true } };

        var body = await RunOneJobAndCaptureCommandAsync(
            leaseToken: "lease-abc",
            command: "completion",
            handler: (_, _) => Task.FromResult<object?>(callerRequest));

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("lease-abc", doc.RootElement.GetProperty("jobLeaseToken").GetString());

        // The handler's own object must be untouched, or the next job would inherit this token.
        Assert.Null(callerRequest.JobLeaseToken);
    }

    [Fact]
    public async Task Fail_carries_the_lease_token()
    {
        var body = await RunOneJobAndCaptureCommandAsync(
            leaseToken: "lease-def",
            command: "failure",
            handler: (_, _) => throw new JobFailureException("boom", retries: 2));

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("lease-def", doc.RootElement.GetProperty("jobLeaseToken").GetString());
    }

    [Fact]
    public async Task ThrowError_carries_the_lease_token()
    {
        var body = await RunOneJobAndCaptureCommandAsync(
            leaseToken: "lease-ghi",
            command: "error",
            handler: (_, _) => throw new BpmnErrorException("MY_ERR", "nope", variables: null));

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("lease-ghi", doc.RootElement.GetProperty("jobLeaseToken").GetString());
    }

    private static async Task<string> CaptureActivationBodyAsync(bool withLease)
    {
        var handler = new MockHttpMessageHandler();
        var firstBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Enqueue(async req =>
        {
            firstBody.TrySetResult(await req.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jobs\":[]}", System.Text.Encoding.UTF8, "application/json"),
            };
        });
        for (var i = 0; i < 8; i++)
            handler.Enqueue(HttpStatusCode.OK, "{\"jobs\":[]}");

        using var client = NewClient(handler);
        var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "lease-test", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = false, WithLease = withLease },
            (_, _) => Task.FromResult<object?>(null));
        worker.Start();

        var body = await firstBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(TimeSpan.FromSeconds(1));
        return body;
    }

    private static async Task<string> RunOneJobAndCaptureCommandAsync(
        string leaseToken,
        string command,
        JobHandler handler)
    {
        var mock = new MockHttpMessageHandler();
        var commandBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 1st poll returns one leased job; the command call captures its body; later polls are empty.
        mock.Enqueue(HttpStatusCode.OK, $"{{\"jobs\":[{JobJson("222", leaseToken)}]}}");
        mock.Enqueue(async req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var isCommand = command switch
            {
                "completion" => path.EndsWith("/completion", StringComparison.Ordinal),
                "failure" => path.EndsWith("/failure", StringComparison.Ordinal),
                "error" => path.EndsWith("/error", StringComparison.Ordinal),
                _ => false,
            };
            if (isCommand)
                commandBody.TrySetResult(await req.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        for (var i = 0; i < 8; i++)
            mock.Enqueue(HttpStatusCode.OK, "{\"jobs\":[]}");

        using var client = NewClient(mock);
        var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "lease-test", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = false, WithLease = true },
            handler);
        worker.Start();

        var body = await commandBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(TimeSpan.FromSeconds(1));
        return body;
    }

    /// <summary>Always answers an activation poll with an empty job list, so a worker keeps
    /// polling indefinitely rather than draining a fixed queue.</summary>
    private sealed class AlwaysEmptyJobsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jobs\":[]}", System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
