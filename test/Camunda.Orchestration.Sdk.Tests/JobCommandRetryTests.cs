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
    public void Other_operations_keep_retrying_500()
    {
        // stable/10: non-job operations keep the default classifier, which retries a 500
        // surfaced as HttpRequestException; only job commands use the stricter rule.
        var ex = new HttpRequestException("boom", null, HttpStatusCode.InternalServerError);

        Assert.True(HttpRetryExecutor.DefaultClassify(ex).Retryable);
        Assert.False(HttpRetryExecutor.ClassifyJobCommand(ex).Retryable);
        Assert.DoesNotContain("getTopology", HttpRetryExecutor.JobCommandOperationIds);
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

    public static IEnumerable<object[]> UnknownOutcomeTransportFailures() => new[]
    {
        new object[] { "timeout" },
        new object[] { "response-ended" },
        new object[] { "unknown" },
    };

    private static Exception TransportFailure(string kind) => kind switch
    {
        "timeout" => new TaskCanceledException("timeout", new TimeoutException()),
        "response-ended" => new HttpRequestException(HttpRequestError.ResponseEnded, "ended"),
        "unknown" => new HttpRequestException(HttpRequestError.Unknown, "unknown"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(UnknownOutcomeTransportFailures))]
    public async Task Worker_does_not_fail_job_after_unknown_outcome_transport_failure(string kind)
    {
        // The worker's own token stays uncancelled: only the completion request fails.
        var handler = new StubHandler(path => path.EndsWith("/completion", StringComparison.Ordinal)
            ? throw TransportFailure(kind)
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
        Assert.Equal(0, worker.ActiveJobs);
    }

    [Theory]
    [InlineData("completion")]
    [InlineData("failure")]
    public async Task Job_command_without_body_still_sends_content(string command)
    {
        // Body-less requests may be transparently resent by SocketsHttpHandler after a lost
        // response; job commands must therefore always carry content.
        var withContent = 0;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent), r =>
        {
            if (r.Content != null)
                Interlocked.Increment(ref withContent);
        });
        using var client = new CamundaClient(new CamundaOptions { Config = Config(), HttpMessageHandler = handler });

        await (command == "completion"
            ? client.CompleteJobAsync(JobKey.AssumeExists("1"))
            : client.FailJobAsync(JobKey.AssumeExists("1")));

        Assert.Equal(1, handler.Count($"/v2/jobs/1/{command}"));
        Assert.Equal(1, withContent);
    }

    public static IEnumerable<object[]> WorkerCommandOutcomes()
    {
        foreach (var command in new[] { "completion", "failure", "error" })
        {
            foreach (var status in new[] { 404, 409, 500, 502, 504 })
                yield return new object[] { command, status.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            foreach (var kind in new[] { "timeout", "response-ended", "unknown" })
                yield return new object[] { command, kind };
        }
    }

    [Theory]
    [MemberData(nameof(WorkerCommandOutcomes))]
    public async Task Worker_logs_unapplied_command_as_warning_without_exception(string command, string outcome)
    {
        // Every worker command (complete, fail, throwError) must log 404/409 and unknown outcomes
        // as a warning without a stack trace, never as an error.
        var handler = new StubHandler(path => !path.EndsWith($"/{command}", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : int.TryParse(outcome, out var status) ? Status(status) : throw TransportFailure(outcome));
        var logs = new LogCapture();
        using var client = new CamundaClient(new CamundaOptions
        {
            Config = Config(),
            HttpMessageHandler = handler,
            LoggerFactory = logs,
        });

        await using var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "t", JobTimeoutMs = 30_000, MaxConcurrentJobs = 1, AutoStart = false },
            (_, _) => command switch
            {
                "completion" => Task.FromResult<object?>(null),
                "failure" => throw new JobFailureException("boom", 1),
                _ => throw new BpmnErrorException("E"),
            });
        worker.Start();

        // Safety-net deadline, not a correctness signal.
        await WaitUntilAsync(() => handler.Count($"/v2/jobs/222/{command}") == 1 && worker.ActiveJobs == 0);
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        var workerLogs = logs.Entries.Where(e => e.Category.Contains("JobWorker", StringComparison.Ordinal)).ToList();
        Assert.Contains(workerLogs, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Exception is null);
        Assert.DoesNotContain(workerLogs, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning && e.Exception is not null);
        Assert.DoesNotContain(workerLogs, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
    }

    [Theory]
    [InlineData("completion")]
    [InlineData("failure")]
    public async Task Body_less_job_command_is_not_resent_after_lost_response(string command)
    {
        // Real SocketsHttpHandler over loopback: a request on a reused connection that loses its
        // response may be transparently resent. Job commands must reach the server exactly once.
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var commandRequests = 0;
        using var serverCts = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            while (!serverCts.IsCancellationRequested)
            {
                System.Net.Sockets.TcpClient conn;
                try
                { conn = await listener.AcceptTcpClientAsync(serverCts.Token); }
                catch (OperationCanceledException) { return; }
                catch (System.Net.Sockets.SocketException) { return; }
                _ = Task.Run(async () =>
                {
                    using (conn)
                    {
                        var stream = conn.GetStream();
                        while (await ReadRequestAsync(stream) is { } path)
                        {
                            if (path.StartsWith("/v2/jobs/", StringComparison.Ordinal))
                            {
                                Interlocked.Increment(ref commandRequests);
                                return; // Drop the connection without a response.
                            }
                            var ok = Encoding.ASCII.GetBytes(
                                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}");
                            await stream.WriteAsync(ok);
                        }
                    }
                });
            }
        });

        var config = Config();
        config["CAMUNDA_REST_ADDRESS"] = $"http://127.0.0.1:{port}";
        using var client = new CamundaClient(new CamundaOptions { Config = config });
        try
        {
            // Establish a reusable keep-alive connection first.
            try
            { await client.GetTopologyAsync(); }
            catch (Exception) { /* response shape is irrelevant */ }

            await Assert.ThrowsAnyAsync<Exception>(() => command == "completion"
                ? client.CompleteJobAsync(JobKey.AssumeExists("1"))
                : client.FailJobAsync(JobKey.AssumeExists("1")));

            Assert.Equal(1, Volatile.Read(ref commandRequests));
        }
        finally
        {
            serverCts.Cancel();
            listener.Stop();
            await server;
        }
    }

    /// <summary>Reads one HTTP/1.1 request (headers + Content-Length body); returns its path, or null at EOF.</summary>
    private static async Task<string?> ReadRequestAsync(Stream stream)
    {
        var header = new List<byte>();
        var one = new byte[1];
        while (header.Count < 4 || header[^4] != '\r' || header[^3] != '\n' || header[^2] != '\r' || header[^1] != '\n')
        {
            if (await stream.ReadAsync(one) == 0)
                return null;
            header.Add(one[0]);
        }
        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
        var length = lines
            .Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            .Select(l => int.Parse(l["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture))
            .FirstOrDefault();
        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(read));
            if (n == 0)
                return null;
            read += n;
        }
        return lines[0].Split(' ')[1];
    }

    [Theory]
    [InlineData("POST", "/jobs/1/completion", true)]
    [InlineData("POST", "/jobs/1/failure", true)]
    [InlineData("POST", "/jobs/1/error", true)]
    [InlineData("GET", "/jobs/1/completion", false)]
    [InlineData("POST", "/jobs/activation", false)]
    [InlineData("POST", "/process-instances/1/cancellation", false)]
    public void Job_command_paths_are_recognised(string method, string path, bool expected)
    {
        Assert.Equal(expected, CamundaClient.IsJobCommandPath(new HttpMethod(method), path));
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

    private sealed class LogCapture : Microsoft.Extensions.Logging.ILoggerFactory
    {
        private readonly ConcurrentQueue<(string Category, Microsoft.Extensions.Logging.LogLevel Level, Exception? Exception)> _entries = new();

        public IReadOnlyList<(string Category, Microsoft.Extensions.Logging.LogLevel Level, Exception? Exception)> Entries => _entries.ToList();

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

        public void AddProvider(Microsoft.Extensions.Logging.ILoggerProvider provider) { }

        public void Dispose() { }

        private sealed class Logger(string category, ConcurrentQueue<(string, Microsoft.Extensions.Logging.LogLevel, Exception?)> sink) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
                TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => sink.Enqueue((category, logLevel, exception));
        }
    }

    private sealed class StubHandler(Func<string, HttpResponseMessage> respond, Action<HttpRequestMessage>? inspect = null) : HttpMessageHandler
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
            inspect?.Invoke(request);
            return Task.FromResult(respond(path));
        }
    }
}
