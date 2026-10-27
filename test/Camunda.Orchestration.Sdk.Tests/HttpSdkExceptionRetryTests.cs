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
        // and shrink the permit cap below the BALANCED initial 16. Each HTTP
        // response must feed the manager exactly once, so three calls accumulate
        // exactly three events.
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

    [Fact]
    public async Task SendAsync_RecordsEachBackpressureResponseExactlyOnce()
    {
        // MaxAttempts = 1: the single rejection is the final attempt. Recording it
        // both in the classifier and in the terminal catch would double-count one
        // HTTP response as two signals (and two permit reductions).
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler, new Dictionary<string, string>
        {
            ["CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS"] = "1",
        });

        handler.Enqueue(HttpStatusCode.TooManyRequests, RateLimited429Body);
        await Assert.ThrowsAsync<HttpSdkException>(() =>
            client.ActivateJobsAsync(new JobActivationRequest
            {
                Type = "t",
                MaxJobsToActivate = 1,
                Timeout = 1000,
            }));

        Assert.Equal(1, client.GetBackpressureState().Consecutive);
    }

    [Fact]
    public async Task SendAsync_RetriedExhaustion_RecordsOneSignalPerResponse()
    {
        // Default MaxAttempts = 3: three 429 responses must produce exactly three
        // signals — one per HTTP response, not three retried + one extra for the
        // final failure.
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

        Assert.Equal(3, client.GetBackpressureState().Consecutive);
    }

    [Fact]
    public async Task SendAsync_Plain503Outage_DoesNotShrinkPermitGate()
    {
        // An ordinary 503 (no RESOURCE_EXHAUSTED marker) is still retryable, but it
        // is not the broker shedding load: it must not shrink the adaptive gate.
        const string plain503 = """{"title":"SERVICE_UNAVAILABLE","status":503,"detail":"upstream restarting"}""";
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler, new Dictionary<string, string>
        {
            ["CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS"] = "1",
        });

        handler.Enqueue(HttpStatusCode.ServiceUnavailable, plain503);
        var ex = await Assert.ThrowsAsync<HttpSdkException>(() =>
            client.ActivateJobsAsync(new JobActivationRequest
            {
                Type = "t",
                MaxJobsToActivate = 1,
                Timeout = 1000,
            }));

        Assert.False(ex.IsBackpressure);
        var state = client.GetBackpressureState();
        Assert.Equal(0, state.Consecutive);
        Assert.Equal(16, state.PermitsMax);
    }

    [Fact]
    public async Task SendAsync_500ResourceExhaustedInDetail_RecordsBackpressure()
    {
        // The server also surfaces load shedding as a 500 whose detail carries
        // RESOURCE_EXHAUSTED; that signal must still feed the manager.
        const string body = """{"title":"Internal Server Error","status":500,"detail":"broker rejected: RESOURCE_EXHAUSTED"}""";
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler, new Dictionary<string, string>
        {
            ["CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS"] = "1",
        });

        handler.Enqueue(HttpStatusCode.InternalServerError, body);
        var ex = await Assert.ThrowsAsync<HttpSdkException>(() =>
            client.ActivateJobsAsync(new JobActivationRequest
            {
                Type = "t",
                MaxJobsToActivate = 1,
                Timeout = 1000,
            }));

        Assert.True(ex.IsBackpressure);
        Assert.Equal(1, client.GetBackpressureState().Consecutive);
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
    public async Task PermitGate_CancelledQueuedWaiter_DoesNotLeakPermit()
    {
        // Regression: a waiter cancelled while queued must never be granted a permit
        // later. With a cap of one, an abandoned grant is never returned and would
        // block every subsequent non-exempt acquisition indefinitely.
        var bp = new BackpressureManager(
            new BackpressureConfig { InitialMax = 1 },
            NullLogger.Instance,
            TimeProvider.System);

        await bp.AcquireAsync(); // holds the only permit

        using var cts = new CancellationTokenSource();
        var queued = bp.AcquireAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

        bp.Release(); // frees the holder's permit; must NOT hand it to the cancelled waiter

        using var reacquireCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await bp.AcquireAsync(reacquireCts.Token); // would hang forever if the permit leaked
        bp.Release();
    }

    [Fact]
    public async Task PermitGate_GrantWinningCancellationRace_IsNotLeaked()
    {
        // Race variant: the permit is granted at the same moment the caller's token
        // fires. The caller observes cancellation, but the granted permit must be
        // recycled — the next acquisition has to succeed.
        var bp = new BackpressureManager(
            new BackpressureConfig { InitialMax = 1 },
            NullLogger.Instance,
            TimeProvider.System);

        await bp.AcquireAsync(); // holds the only permit

        using var cts = new CancellationTokenSource();
        var queued = bp.AcquireAsync(cts.Token);

        bp.Release(); // grant the queued waiter
        cts.Cancel(); // cancel immediately after the grant

        try
        {
            await queued; // grant won the race — the waiter holds the permit now
            bp.Release(); // so it must be returned here
        }
        catch (OperationCanceledException)
        {
            // Cancellation won the observation race: the gate must have recycled the
            // permit internally instead of leaking it.
        }

        using var reacquireCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await bp.AcquireAsync(reacquireCts.Token); // would hang forever if the permit leaked
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

    [Fact]
    public async Task JobWorker_Plain503Outage_LogsError_NotQuietBackoff()
    {
        // A 503 without a RESOURCE_EXHAUSTED marker is an ordinary outage, not the
        // broker shedding load: the worker must keep it on the error path (error-level
        // log) and must not feed the backpressure manager.
        const string plain503 = """{"title":"SERVICE_UNAVAILABLE","status":503,"detail":"upstream restarting"}""";
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
            handler.Enqueue(HttpStatusCode.ServiceUnavailable, plain503);
            worker.Start();

            // Give the poll loop a bounded window to process the failure.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!loggerFactory.Entries.Any(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error)
                   && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Assert.Contains(loggerFactory.Entries,
                e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error);
            Assert.Equal(0, client.GetBackpressureState().Consecutive);
        }
        finally
        {
            await worker.StopAsync();
            await worker.DisposeAsync();
        }
    }

    // ---- 5. Multipart uploads: the body must survive across retry attempts ----

    [Fact]
    public async Task SendMultipartAsync_ReusedContentAcrossAttempts_RemainsSendable()
    {
        // Regression: InvokeWithRetryAsync re-invokes the multipart delegate with the
        // SAME caller-owned content on a retry. The first attempt's request disposal
        // (the mock disposes each request, as HttpClient does) must not destroy that
        // content — the retried attempt has to send the same bytes again.
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler);

        handler.Enqueue(HttpStatusCode.ServiceUnavailable, ResourceExhausted503Body);
        handler.Enqueue(HttpStatusCode.OK, """{"deployments":[]}""");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("test"), "resources", "test.bpmn");

        // First attempt fails...
        await Assert.ThrowsAsync<HttpSdkException>(() =>
            client.SendMultipartAsync<DeploymentResult>("deployments", content));
        // ...and the SAME content is still sendable on the retried attempt.
        var result = await client.SendMultipartAsync<DeploymentResult>("deployments", content);

        Assert.NotNull(result);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SendMultipartAsync_ReusedContent_SendsIdenticalBodyEachAttempt()
    {
        // Stronger variant: capture the body each attempt actually receives and require
        // the replay to be byte-identical (same parts AND same multipart boundary).
        var bodies = new List<string>();
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler);

        handler.Enqueue(async (request, ctoken) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ctoken));
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(ResourceExhausted503Body, System.Text.Encoding.UTF8, "application/json"),
            };
        });
        handler.Enqueue(async (request, ctoken) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ctoken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"deployments":[]}""", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("test"), "resources", "test.bpmn");

        await Assert.ThrowsAsync<HttpSdkException>(() =>
            client.SendMultipartAsync<DeploymentResult>("deployments", content));
        await client.SendMultipartAsync<DeploymentResult>("deployments", content);

        Assert.Equal(2, bodies.Count);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Contains("test", bodies[1]);
    }

    [Fact]
    public async Task SendMultipartAsync_Retry_PreservesPartHeaders()
    {
        // The replay must carry each part's headers (content type, disposition) onto
        // every attempt, and the request must stay a real multipart payload.
        var contentTypes = new List<string?>();
        var handler = new MockHttpMessageHandler();
        using var client = CreateClient(handler);

        handler.Enqueue(async (request, ctoken) =>
        {
            await request.Content!.ReadAsStringAsync(ctoken);
            var multipart = Assert.IsAssignableFrom<MultipartContent>(request.Content);
            contentTypes.Add(multipart.First().Headers.ContentType?.ToString());
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(ResourceExhausted503Body, System.Text.Encoding.UTF8, "application/json"),
            };
        });
        handler.Enqueue(async (request, ctoken) =>
        {
            await request.Content!.ReadAsStringAsync(ctoken);
            var multipart = Assert.IsAssignableFrom<MultipartContent>(request.Content);
            contentTypes.Add(multipart.First().Headers.ContentType?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"deployments":[]}""", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        using var content = new MultipartFormDataContent();
        var part = new StringContent("test");
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
        content.Add(part, "resources", "test.bpmn");

        await Assert.ThrowsAsync<HttpSdkException>(() =>
            client.SendMultipartAsync<DeploymentResult>("deployments", content));
        await client.SendMultipartAsync<DeploymentResult>("deployments", content);

        Assert.Equal(2, contentTypes.Count);
        Assert.All(contentTypes, ct => Assert.Equal("application/xml", ct));
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
