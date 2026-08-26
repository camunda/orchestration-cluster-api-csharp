using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for the injected-clock contract (camunda/orchestration-cluster-api-js#450).
///
/// <para>Ruling 2 makes wall-clock time the single notion the SDK runtime resolves through,
/// and Ruling 2a requires the live clock to be non-decreasing so that discarding the
/// monotonic notion does not make deadlines vulnerable to backwards clock jumps.</para>
/// </summary>
public class CamundaTimeProviderTests
{
    /// <summary>
    /// A clock that can be moved backwards.
    /// <para><c>FakeTimeProvider</c> deliberately throws on backwards movement ("Cannot go
    /// back in time"), so it cannot express the hazard <see cref="CamundaTimeProvider"/>
    /// exists to absorb. The real system clock has no such scruples.</para>
    /// </summary>
    private sealed class SettableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private long _utcTicks = now.UtcTicks;

        public void Set(DateTimeOffset value) => Volatile.Write(ref _utcTicks, value.UtcTicks);

        public override DateTimeOffset GetUtcNow() => new(Volatile.Read(ref _utcTicks), TimeSpan.Zero);
    }

    [Fact]
    public void PassesThroughForwardMovement()
    {
        var inner = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var clock = new CamundaTimeProvider(inner);

        var first = clock.GetUtcNow();
        inner.Advance(TimeSpan.FromSeconds(30));
        var second = clock.GetUtcNow();

        Assert.Equal(DateTimeOffset.UnixEpoch, first);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(30), second);
    }

    [Fact]
    public void ClampsBackwardsJump()
    {
        var inner = new SettableTimeProvider(DateTimeOffset.UnixEpoch.AddHours(1));
        var clock = new CamundaTimeProvider(inner);

        var before = clock.GetUtcNow();

        // NTP correction, VM resume, or a manual adjustment moves the wall clock back.
        inner.Set(DateTimeOffset.UnixEpoch);
        var after = clock.GetUtcNow();

        Assert.Equal(before, after);
    }

    [Fact]
    public void ResumesAdvancingOnlyOncePastTheClampedHighWaterMark()
    {
        var inner = new SettableTimeProvider(DateTimeOffset.UnixEpoch.AddHours(1));
        var clock = new CamundaTimeProvider(inner);

        var before = clock.GetUtcNow();
        inner.Set(DateTimeOffset.UnixEpoch);

        // The backward step is absorbed, so the reading holds rather than going back.
        Assert.Equal(before, clock.GetUtcNow());

        // Forward movement is reported again, less the slice used to pay down the offset.
        inner.Set(DateTimeOffset.UnixEpoch.AddHours(2));
        var after = clock.GetUtcNow();
        Assert.True(after > before, "clock did not resume advancing");
        Assert.True(after < before.AddHours(2), "no correction was paid back");
    }

    /// <summary>
    /// Forward progress must resume immediately after a backward jump, not stall until the
    /// underlying clock catches back up.
    ///
    /// <para>A max-clamp satisfies "never decreases" but freezes logical time for the whole
    /// duration of the correction, so an hour-long NTP step would add an hour to every
    /// deadline in flight — the exact failure this class exists to prevent.</para>
    /// </summary>
    [Fact]
    public void PreservesForwardProgressAfterABackwardJump()
    {
        var inner = new SettableTimeProvider(DateTimeOffset.UnixEpoch.AddHours(1));
        var clock = new CamundaTimeProvider(inner);

        var before = clock.GetUtcNow();

        // An hour-long backward correction.
        inner.Set(DateTimeOffset.UnixEpoch);
        Assert.Equal(before, clock.GetUtcNow());

        // Five seconds of real forward movement, still far below the old high-water mark,
        // must show up as very nearly five seconds. Under a max-clamp this would report no
        // progress at all for the next hour.
        inner.Set(DateTimeOffset.UnixEpoch.AddSeconds(5));
        var advanced = clock.GetUtcNow() - before;

        Assert.InRange(advanced, TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// The absorbed correction must be paid back, not carried forever.
    ///
    /// <para>A permanent offset keeps reported time ahead of the true clock indefinitely, so
    /// handler timestamps and any comparison against a server-supplied absolute time — a job
    /// deadline, say — stay wrong for the life of the process. Slewing converges instead.</para>
    /// </summary>
    [Fact]
    public void ConvergesBackTowardTheUnderlyingClock()
    {
        var inner = new SettableTimeProvider(DateTimeOffset.UnixEpoch.AddMinutes(10));
        var clock = new CamundaTimeProvider(inner);

        _ = clock.GetUtcNow();
        inner.Set(DateTimeOffset.UnixEpoch);

        // Ten minutes of divergence, then a long stretch of ordinary forward progress.
        var divergence = clock.GetUtcNow() - inner.GetUtcNow();
        Assert.Equal(TimeSpan.FromMinutes(10), divergence);

        var t = DateTimeOffset.UnixEpoch;
        var previous = clock.GetUtcNow();
        for (var i = 0; i < 400; i++)
        {
            t = t.AddSeconds(30);
            inner.Set(t);

            var reported = clock.GetUtcNow();
            Assert.True(reported >= previous, "reported time went backwards while slewing");
            previous = reported;
        }

        Assert.Equal(TimeSpan.Zero, clock.GetUtcNow() - inner.GetUtcNow());
    }

    [Fact]
    public async Task NeverDecreasesUnderConcurrentReaders()
    {
        var inner = new SettableTimeProvider(DateTimeOffset.UnixEpoch);
        var clock = new CamundaTimeProvider(inner);
        var failures = 0;

        // Each reader asserts monotonicity against its own previous observation while a
        // writer shoves the underlying clock back and forth.
        var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            var previous = DateTimeOffset.MinValue;
            for (var i = 0; i < 2_000; i++)
            {
                var observed = clock.GetUtcNow();
                if (observed < previous)
                    Interlocked.Increment(ref failures);
                previous = observed;
            }
        })).ToArray();

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 2_000; i++)
                inner.Set(DateTimeOffset.UnixEpoch.AddSeconds(i % 2 == 0 ? i : -i));
        });

        await Task.WhenAll([.. readers, writer]);

        Assert.Equal(0, failures);
    }

    [Fact]
    public void LiveIsClampedAndShared()
    {
        Assert.Same(CamundaTimeProvider.Live, CamundaTimeProvider.Live);

        var first = CamundaTimeProvider.Live.GetUtcNow();
        var second = CamundaTimeProvider.Live.GetUtcNow();

        Assert.True(second >= first);
    }

    [Fact]
    public void DelegatesNonClockMembers()
    {
        var inner = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var clock = new CamundaTimeProvider(inner);

        Assert.Equal(inner.LocalTimeZone, clock.LocalTimeZone);
        Assert.Equal(inner.TimestampFrequency, clock.TimestampFrequency);
    }
    [Fact]
    public void RejectsNullInner()
    {
        Assert.Throws<ArgumentNullException>(() => new CamundaTimeProvider(null!));
    }
}

/// <summary>
/// Verifies that the runtime subsystems resolve time through the injected clock rather
/// than the ambient wall clock — i.e. that pinning the clock actually pins them.
/// </summary>
public class InjectedClockRuntimeTests
{
    [Fact]
    public void BackpressureDecayUsesInjectedClock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var config = new BackpressureConfig { DecayQuietMs = 10_000 };
        var bp = new BackpressureManager(config, NullLogger.Instance, clock);

        bp.RecordBackpressure();
        Assert.Equal(1, bp.GetState().Consecutive);

        // Not yet past the quiet window: no decay.
        clock.Advance(TimeSpan.FromMilliseconds(9_000));
        bp.RecordHealthy();
        Assert.Equal(1, bp.GetState().Consecutive);

        // Past it: decays. Real time has not moved at all during this test.
        clock.Advance(TimeSpan.FromMilliseconds(2_000));
        bp.RecordHealthy();
        Assert.Equal(0, bp.GetState().Consecutive);
    }

    [Fact]
    public async Task RetryBackoffSleepsOnInjectedClock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var config = new HttpRetryConfig { MaxAttempts = 3, BaseDelayMs = 30_000, MaxDelayMs = 120_000 };
        var attempts = 0;

        var pending = HttpRetryExecutor.ExecuteWithRetryAsync(
            () =>
            {
                attempts++;
                if (attempts < 3)
                    throw new HttpRequestException("transient", null, System.Net.HttpStatusCode.TooManyRequests);
                return Task.FromResult(42);
            },
            config,
            NullLogger.Instance,
            clock);

        // The backoff is minutes long, but no real time passes: the operation only makes
        // progress when the injected clock is advanced.
        await DriveVirtualTime(pending, clock.Advance, TimeSpan.FromSeconds(30));

        Assert.Equal(42, await pending);
        Assert.Equal(3, attempts);
    }

    /// <summary>
    /// Advance <paramref name="advance"/> until <paramref name="pending"/> completes,
    /// bounded in real time so a regression that stalls the task fails the test rather
    /// than hanging the run.
    /// </summary>
    private static async Task DriveVirtualTime(Task pending, Action<TimeSpan> advance, TimeSpan step, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!pending.IsCompleted)
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException($"Task did not complete within {timeoutMs}ms of driving virtual time.");
            await Task.Yield();
            advance(step);
        }
    }

    [Fact]
    public void CamundaOptionsLeavesTheClockUnsetSoTheClientCanApplyTheDefault()
    {
        // The effective default is asserted end-to-end in
        // InjectedClockWorkerTests.HandlerReceivesTheLiveClockWhenNoneIsConfigured, which
        // observes it on the handler's ActivatedJob.Clock rather than inferring it here.
        Assert.Null(new CamundaOptions().TimeProvider);
    }

    /// <summary>
    /// Ruling 3: a recurring loop that was due to tick N times during a large time jump
    /// ticks once and resynchronises, rather than replaying every missed tick.
    /// </summary>
    [Fact]
    public async Task EventualPollerCoalescesMissedTicksAcrossALargeTimeJump()
    {
        var clock = new InstrumentedFakeTimeProvider(DateTimeOffset.UnixEpoch);
        var invocations = 0;

        var pending = EventualPoller.PollAsync(
            "coalesceOp",
            isGet: false,
            invoke: () =>
            {
                invocations++;
                return Task.FromResult(0);
            },
            new ConsistencyOptions<int>
            {
                WaitUpToMs = (int)TimeSpan.FromHours(25).TotalMilliseconds,
                PollIntervalMs = 5_000,
                IsConsistent = _ => false,
            },
            NullLogger.Instance,
            clock);

        // Advance only once the poller is parked on the clock, so the jump is observed
        // exactly once and the resulting poll count is deterministic.
        await clock.WaitForTimersAsync(1);

        clock.Advance(TimeSpan.FromHours(25));

        await Assert.ThrowsAsync<EventualConsistencyTimeoutException>(() => pending);

        // One poll before the jump, one after it. 25h of 5s ticks is ~18,000, so anything
        // above two means the loop replayed part of the gap instead of resynchronising.
        Assert.Equal(2, invocations);
    }

    /// <summary>
    /// Ruling 3, resynchronisation branch: after an invoke that outlasts the poll interval,
    /// the next poll is a full interval away — not immediate.
    ///
    /// <para>This is the case the deadline-driven tests cannot reach, because they expire
    /// before the branch runs. Resynchronising to <c>now</c> rather than <c>now + interval</c>
    /// would make the loop poll again the instant a slow request returned, which is the
    /// busy-polling pathology the ruling exists to prevent — and every other test here would
    /// still pass.</para>
    /// </summary>
    [Fact]
    public async Task EventualPollerWaitsAFullIntervalAfterAnInvokeSlowerThanTheInterval()
    {
        var clock = new InstrumentedFakeTimeProvider(DateTimeOffset.UnixEpoch);
        using var cts = new CancellationTokenSource();
        var invocations = 0;

        // Deadline far enough out that it never ends the loop; the interval is what matters.
        var pending = EventualPoller.PollAsync(
            "slowRequestOp",
            isGet: false,
            invoke: () =>
            {
                if (Interlocked.Increment(ref invocations) == 1)
                    clock.Advance(TimeSpan.FromSeconds(5)); // five times the poll interval
                return Task.FromResult(0);
            },
            new ConsistencyOptions<int>
            {
                WaitUpToMs = (int)TimeSpan.FromHours(1).TotalMilliseconds,
                PollIntervalMs = 1_000,
                IsConsistent = _ => false,
            },
            NullLogger.Instance,
            clock,
            cts.Token);

        try
        {
            // Real time passes but the clock does not. A loop that resynchronised to `now`
            // would have a zero wait here and spin, so this count would be far above one.
            await Task.Delay(200);
            Assert.Equal(1, Volatile.Read(ref invocations));

            await clock.WaitForTimersAsync(1);

            // Just short of a full interval: still parked.
            clock.Advance(TimeSpan.FromMilliseconds(900));
            await Task.Delay(100);
            Assert.Equal(1, Volatile.Read(ref invocations));

            // Completing the interval releases exactly one more poll.
            clock.Advance(TimeSpan.FromMilliseconds(100));
            await clock.WaitForTimersAsync(2);
            Assert.Equal(2, Volatile.Read(ref invocations));
        }
        finally
        {
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
    }

    /// <summary>
    /// The poller previously tracked elapsed time by counting intervals
    /// (<c>elapsed += interval</c>), which ignored how long each invoke took, so the
    /// reported wait understated the real one. Elapsed is now measured from the clock.
    /// </summary>
    [Fact]
    public async Task EventualPollerReportsMeasuredElapsedNotCountedIntervals()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);

        var pending = EventualPoller.PollAsync(
            "slowInvokeOp",
            isGet: false,
            invoke: () =>
            {
                // Each attempt takes far longer than the poll interval.
                clock.Advance(TimeSpan.FromSeconds(30));
                return Task.FromResult(0);
            },
            new ConsistencyOptions<int>
            {
                WaitUpToMs = 10_000,
                PollIntervalMs = 1_000,
                IsConsistent = _ => false,
            },
            NullLogger.Instance,
            clock);

        await DriveVirtualTime(pending, clock.Advance, TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<EventualConsistencyTimeoutException>(() => pending);

        // A single 30s attempt already blew the 10s budget; interval-counting would have
        // reported 10000 or less regardless of how long the attempt actually took.
        Assert.True(
            ex.WaitedMs >= 30_000,
            $"expected measured elapsed of at least 30000ms, got {ex.WaitedMs}ms");
    }

    /// <summary>
    /// Token expiry is evaluated against the injected clock, so a test can age a token
    /// past its lifetime without waiting for it.
    /// </summary>
    [Fact]
    public async Task OAuthRefreshIsDrivenByTheInjectedClock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var issued = 0;

        var handler = new StubTokenHandler(() =>
        {
            issued++;
            // 1h lifetime; the manager subtracts a skew buffer of max(30s, 5%) = 3m.
            return $$"""{"access_token":"token-{{issued}}","expires_in":3600}""";
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.mock/") };

        var config = new CamundaConfig
        {
            OAuth = new OAuthConfig
            {
                ClientId = "id",
                ClientSecret = "secret",
                OAuthUrl = "https://auth.mock/token",
                Retry = new OAuthRetryConfig { Max = 1, BaseDelayMs = 10 },
                TimeoutMs = 5000,
            },
            TokenAudience = "aud",
        };

        using var oauth = new OAuthManager(config, NullLogger.Instance, clock);

        Assert.Equal("token-1", await oauth.GetTokenAsync(client));

        // Still well inside the lifetime: served from cache.
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal("token-1", await oauth.GetTokenAsync(client));
        Assert.Equal(1, issued);

        // Past effective expiry (1h minus the 3m skew buffer): refreshed. No real time
        // has passed at any point.
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal("token-2", await oauth.GetTokenAsync(client));
        Assert.Equal(2, issued);
    }

    /// <summary>
    /// The OAuth retry backoff is cadence, so it must be virtual: with the clock held the
    /// retry stays pending, and only an advance lets it proceed.
    /// </summary>
    [Fact]
    public async Task OAuthRetryBackoffWaitsOnTheInjectedClock()
    {
        var clock = new InstrumentedFakeTimeProvider(DateTimeOffset.UnixEpoch);
        var attempts = 0;

        var handler = new StubTokenHandler(() =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new HttpRequestException("transient");
            return """{"access_token":"token","expires_in":3600}""";
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.mock/") };

        // A 60s backoff: on a real clock this test would take a minute.
        using var oauth = new OAuthManager(
            CreateOAuthConfig(retryMax: 3, baseDelayMs: 60_000), NullLogger.Instance, clock);

        var pending = oauth.GetTokenAsync(client);

        // Parked on the backoff after the first failure, and going nowhere on its own.
        await clock.WaitForTimersAsync(1);
        await Task.Delay(150);
        Assert.False(pending.IsCompleted, "retry proceeded without the clock advancing");
        Assert.Equal(1, Volatile.Read(ref attempts));

        // Past the backoff by a clear margin rather than exactly 60s: the delay carries
        // ±10% jitter from Random.Shared, which is not clock-controlled (deferred to the
        // seeded-RNG follow-up, camunda/sdk-infra#50). Advancing exactly one nominal
        // backoff would therefore fire the timer only when the jitter came out negative.
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal("token", await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, Volatile.Read(ref attempts));
    }

    /// <summary>
    /// Regression guard for the OAuth request-timeout liveness exemption: the timeout must
    /// fire on real time even while the injected clock is held, or a pinned clock would let
    /// an unresponsive token endpoint hang the caller forever.
    /// </summary>
    [Fact]
    public async Task OAuthRequestTimeoutFiresWhileTheClockIsHeld()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);

        using var unresponsive = new NeverRespondingHandler();
        using var client = new HttpClient(unresponsive) { BaseAddress = new Uri("https://auth.mock/") };

        using var oauth = new OAuthManager(
            CreateOAuthConfig(retryMax: 1, baseDelayMs: 10, timeoutMs: 200), NullLogger.Instance, clock);

        // The clock is never advanced. Only the real-time liveness bound can end this.
        var ex = await Assert.ThrowsAsync<CamundaAuthException>(
            () => oauth.GetTokenAsync(client).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(CamundaAuthErrorCode.TokenFetchFailed, ex.Code);
    }

    private static CamundaConfig CreateOAuthConfig(int retryMax, int baseDelayMs, int timeoutMs = 5000) => new()
    {
        OAuth = new OAuthConfig
        {
            ClientId = "id",
            ClientSecret = "secret",
            OAuthUrl = "https://auth.mock/token",
            Retry = new OAuthRetryConfig { Max = retryMax, BaseDelayMs = baseDelayMs },
            TimeoutMs = timeoutMs,
        },
        TokenAudience = "aud",
    };

    private sealed class StubTokenHandler(Func<string> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body(), System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private sealed class NeverRespondingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
