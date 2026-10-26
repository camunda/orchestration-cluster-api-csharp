using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Regression tests for https://github.com/camunda/orchestration-cluster-api-csharp/issues/479:
/// HTTP 429/503/500 responses surfaced as <see cref="HttpSdkException"/> were never retried
/// and never fed the adaptive backpressure manager, and the permit gate never resized when
/// the adaptive concurrency limit changed.
/// </summary>
public class HttpSdkExceptionRetryTests
{
    private const string ResourceExhausted503Body =
        """{"type":"about:blank","title":"RESOURCE_EXHAUSTED","status":503,"detail":"backpressure","instance":"/v2/jobs/activation"}""";

    private const string RateLimited429Body =
        """{"type":"about:blank","title":"Too Many Requests","status":429,"detail":"rate limited","instance":"/v2/jobs/activation"}""";

    private static CamundaClient CreateClient(
        MockHttpMessageHandler handler,
        Dictionary<string, string>? config = null,
        TimeProvider? timeProvider = null,
        IRandomSource? randomSource = null)
    {
        var merged = new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "http://mock.local/v2",
            ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
        };
        if (config != null)
            foreach (var kv in config)
                merged[kv.Key] = kv.Value;

        return new CamundaClient(new CamundaOptions
        {
            Config = merged,
            HttpMessageHandler = handler,
            TimeProvider = timeProvider,
            RandomSource = randomSource,
        });
    }

    // ---- 1. Classification: HttpSdkException must be retried for 429/503/500 ----

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(500)]
    public void DefaultClassify_RetriesHttpSdkException_ForRetryableStatuses(int status)
    {
        var ex = new HttpSdkException("boom", status, "op");

        var decision = HttpRetryExecutor.DefaultClassify(ex);

        Assert.True(decision.Retryable, $"HTTP {status} surfaced as HttpSdkException should be retryable");
        Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), decision.Reason);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    public void DefaultClassify_DoesNotRetryHttpSdkException_ForNonRetryableStatuses(int status)
    {
        var ex = new HttpSdkException("boom", status, "op");

        var decision = HttpRetryExecutor.DefaultClassify(ex);

        Assert.False(decision.Retryable, $"HTTP {status} surfaced as HttpSdkException should not be retryable");
    }

    [Fact]
    public async Task SendAsync_Retries503ResourceExhausted_UpToMaxAttempts()
    {
        // The issue's reproduction: every POST answered with 503 RESOURCE_EXHAUSTED.
        // Expected: HttpRetry.MaxAttempts (3) attempts per call, not 1.
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler, new Dictionary<string, string>
        {
            ["CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS"] = "1",
            ["CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS"] = "5",
        });

        for (var i = 0; i < 3; i++)
        {
            handler.Enqueue(HttpStatusCode.ServiceUnavailable, ResourceExhausted503Body);
        }

        var ex = await Assert.ThrowsAsync<HttpSdkException>(() =>
            client.ActivateJobsAsync(new JobActivationRequest
            {
                Type = "t",
                MaxJobsToActivate = 1,
                Timeout = 1000,
            }));

        Assert.Equal(503, ex.Status);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_Retries429_UpToMaxAttempts()
    {
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler, new Dictionary<string, string>
        {
            ["CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS"] = "1",
            ["CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS"] = "5",
        });

        for (var i = 0; i < 3; i++)
            handler.Enqueue(HttpStatusCode.TooManyRequests, RateLimited429Body);

        var ex = await Assert.ThrowsAsync<HttpSdkException>(() =>
            client.ActivateJobsAsync(new JobActivationRequest
            {
                Type = "t",
                MaxJobsToActivate = 1,
                Timeout = 1000,
            }));

        Assert.Equal(429, ex.Status);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_503ThenSuccess_ReturnsResult()
    {
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler, new Dictionary<string, string>
        {
            ["CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS"] = "1",
            ["CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS"] = "5",
        });

        handler.Enqueue(HttpStatusCode.ServiceUnavailable, ResourceExhausted503Body);
        handler.Enqueue(HttpStatusCode.OK, """{"jobs":[],"worker":"w"}""");

        var result = await client.ActivateJobsAsync(new JobActivationRequest
        {
            Type = "t",
            MaxJobsToActivate = 1,
            Timeout = 1000,
        });

        Assert.NotNull(result);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_DoesNotRetry404()
    {
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler);

        handler.Enqueue(HttpStatusCode.NotFound, """{"title":"Not Found","status":404}""");

        await Assert.ThrowsAsync<HttpSdkException>(() =>
            client.ActivateJobsAsync(new JobActivationRequest
            {
                Type = "t",
                MaxJobsToActivate = 1,
                Timeout = 1000,
            }));

        Assert.Single(handler.Requests);
    }

    // ---- 2. Backpressure recording: any backpressure signal must count ----

    [Fact]
    public async Task SendAsync_503ResourceExhausted_RecordsBackpressure()
    {
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler, new Dictionary<string, string>
        {
            ["CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS"] = "1",
            ["CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS"] = "5",
        });

        for (var i = 0; i < 3; i++)
            handler.Enqueue(HttpStatusCode.ServiceUnavailable, ResourceExhausted503Body);

        await Assert.ThrowsAsync<HttpSdkException>(() =>
            client.ActivateJobsAsync(new JobActivationRequest
            {
                Type = "t",
                MaxJobsToActivate = 1,
                Timeout = 1000,
            }));

        var state = client.GetBackpressureState();
        Assert.True(state.Consecutive > 0, "a 503 RESOURCE_EXHAUSTED must feed the backpressure manager");
        Assert.NotEqual("healthy", state.Severity);
    }

    [Fact]
    public async Task SendAsync_429_RecordsBackpressure()
    {
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler, new Dictionary<string, string>
        {
            ["CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS"] = "1",
            ["CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS"] = "5",
        });

        for (var i = 0; i < 3; i++)
            handler.Enqueue(HttpStatusCode.TooManyRequests, RateLimited429Body);

        await Assert.ThrowsAsync<HttpSdkException>(() =>
            client.ActivateJobsAsync(new JobActivationRequest
            {
                Type = "t",
                MaxJobsToActivate = 1,
                Timeout = 1000,
            }));

        Assert.True(client.GetBackpressureState().Consecutive > 0);
    }

    [Fact]
    public async Task SendAsync_Consecutive503s_EscalateSeverityAndReducePermits()
    {
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler, new Dictionary<string, string>
        {
            ["CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS"] = "1",
        });

        // SevereThreshold defaults to 3: three failing calls should reach "severe"
        // and shrink the permit cap below the BALANCED initial 16. A signal is
        // recorded both when a response is classified for retry and when the final
        // non-retried failure propagates, so three calls accumulate six events.
        for (var call = 0; call < 3; call++)
        {
            handler.Enqueue(HttpStatusCode.ServiceUnavailable, ResourceExhausted503Body);
            await Assert.ThrowsAsync<HttpSdkException>(() =>
                client.ActivateJobsAsync(new JobActivationRequest
                {
                    Type = "t",
                    MaxJobsToActivate = 1,
                    Timeout = 1000,
                }));
        }

        var state = client.GetBackpressureState();
        Assert.True(state.Consecutive >= 3, $"expected at least 3 consecutive signals, got {state.Consecutive}");
        Assert.Equal("severe", state.Severity);
        Assert.True(state.PermitsMax < 16, $"expected permits below the initial 16, got {state.PermitsMax}");
    }

    // ---- 3. Permit gate: must actually resize when the adaptive limit changes ----

    [Fact]
    public async Task PermitGate_ResizesDown_WhenBackpressureRecorded()
    {
        // InitialMax 4, soft factor 70%: one signal drops the cap to 2 (int(4*0.7)).
        // With a fixed-capacity semaphore (the bug), 3 concurrent acquisitions still
        // succeed; a gate that tracks the adaptive cap must block the third.
        var bp = new BackpressureManager(
            new BackpressureConfig { InitialMax = 4 },
            NullLogger.Instance,
            TimeProvider.System);

        await bp.AcquireAsync();
        await bp.AcquireAsync();

        bp.RecordBackpressure();
        Assert.Equal(2, bp.GetState().PermitsMax);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var third = bp.AcquireAsync(cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);

        bp.Release();
        bp.Release();
    }

    [Fact]
    public async Task PermitGate_ResizesUp_OnRecovery()
    {
        // After a reduction, recovery must raise the cap again — a fixed semaphore
        // would never let more than the *original* count through, but the point here
        // is the reverse: a shrunk gate must reopen as permits recover. Recovery only
        // kicks in after a quiet period on the clock, so use a fake clock to cross it
        // deterministically instead of sleeping.
        var clock = new FakeTimeProvider();
        var bp = new BackpressureManager(
            new BackpressureConfig
            {
                InitialMax = 2,
                RecoveryStep = 2,
                DecayQuietMs = 1000,
            },
            NullLogger.Instance,
            clock);

        bp.RecordBackpressure(); // cap: int(2 * 0.7) = 1
        Assert.Equal(1, bp.GetState().PermitsMax);

        clock.Advance(TimeSpan.FromMilliseconds(1500));
        bp.RecordHealthy(); // quiet window crossed -> decays, cap: 1 + 2 = 3
        Assert.Equal(3, bp.GetState().PermitsMax);

        // Three concurrent acquisitions must now fit without blocking.
        await bp.AcquireAsync();
        await bp.AcquireAsync();
        await bp.AcquireAsync();
        bp.Release();
        bp.Release();
        bp.Release();
    }

    [Fact]
    public async Task PermitGate_RecoveryAboveInitialMax_AllowsMoreThanInitialConcurrency()
    {
        // The adaptive cap is allowed to exceed InitialMax during recovery; the gate
        // must follow it rather than clamp to the semaphore's original capacity.
        var clock = new FakeTimeProvider();
        var bp = new BackpressureManager(
            new BackpressureConfig
            {
                InitialMax = 2,
                RecoveryStep = 3,
                DecayQuietMs = 1000,
            },
            NullLogger.Instance,
            clock);

        bp.RecordBackpressure();
        clock.Advance(TimeSpan.FromMilliseconds(1500));
        bp.RecordHealthy();
        Assert.True(bp.GetState().PermitsMax > 2);

        await bp.AcquireAsync();
        await bp.AcquireAsync();
        await bp.AcquireAsync(); // would deadlock against a fixed capacity of 2
        bp.Release();
        bp.Release();
        bp.Release();
    }

    // ---- 4. JobWorker: backpressure rejection is not an error ----

    [Fact]
    public async Task JobWorker_TreatsBackpressureRejectionAsBackoff_NotError()
    {
        // A 503 RESOURCE_EXHAUSTED on activation must not go through the generic
        // error path (error-level log + PollIntervalMs sleep). It should back off
        // quietly and keep polling.
        var handler = new MockHttpMessageHandler();
        var loggerFactory = new CapturingLoggerFactory();
        using var client = CreateClient(handler, new Dictionary<string, string>
        {
            ["CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS"] = "1",
        });
        var config = new JobWorkerConfig
        {
            JobType = "t",
            JobTimeoutMs = 1000,
            MaxConcurrentJobs = 1,
            PollIntervalMs = 25,
            AutoStart = false,
        };
        var worker = new JobWorker(
            client,
            config,
            (job, ct) => Task.FromResult<object?>(null),
            loggerFactory,
            client._jsonOptions,
            TimeProvider.System,
            CamundaRandomSource.Live);

        try
        {
            handler.Enqueue(HttpStatusCode.ServiceUnavailable, ResourceExhausted503Body);
            worker.Start();

            // Give the poll loop a bounded window to process the rejection.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (client.GetBackpressureState().Consecutive == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Assert.True(client.GetBackpressureState().Consecutive > 0,
                "the worker's activation rejection should have fed the backpressure manager");
            Assert.DoesNotContain(loggerFactory.Entries,
                e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        }
        finally
        {
            await worker.StopAsync();
            await worker.DisposeAsync();
        }
    }

    private sealed class CapturingLoggerFactory : Microsoft.Extensions.Logging.ILoggerFactory
    {
        private readonly List<(string Category, Microsoft.Extensions.Logging.LogLevel Level, string Message)> _entries = new();
        private readonly object _gate = new();

        public IReadOnlyList<(string Category, Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries
        {
            get { lock (_gate) return _entries.ToList(); }
        }

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
            => new CapturingLogger(categoryName, this);

        public void AddProvider(Microsoft.Extensions.Logging.ILoggerProvider provider) { }

        public void Dispose() { }

        private void Log(string category, Microsoft.Extensions.Logging.LogLevel level, string message)
        {
            lock (_gate)
                _entries.Add((category, level, message));
        }

        private sealed class CapturingLogger(string category, CapturingLoggerFactory owner) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel,
                Microsoft.Extensions.Logging.EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => owner.Log(category, logLevel, formatter(state, exception));
        }
    }
}
