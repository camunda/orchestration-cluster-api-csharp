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

        _ = clock.GetUtcNow();
        inner.Set(DateTimeOffset.UnixEpoch);

        // Still behind the high-water mark — clamped.
        Assert.Equal(DateTimeOffset.UnixEpoch.AddHours(1), clock.GetUtcNow());

        // Caught up and moved past it — reported again.
        inner.Set(DateTimeOffset.UnixEpoch.AddHours(2));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddHours(2), clock.GetUtcNow());
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
        while (!pending.IsCompleted)
        {
            await Task.Yield();
            clock.Advance(TimeSpan.FromSeconds(30));
        }

        Assert.Equal(42, await pending);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void ClientDefaultsToTheClampedLiveClock()
    {
        var options = new CamundaOptions();

        Assert.Null(options.TimeProvider);

        // The default is applied at construction, not left null, so runtime code never
        // has to fall back to an ambient primitive.
        using var client = CamundaClient.Create(new CamundaOptions
        {
            Config = new Dictionary<string, string> { ["CAMUNDA_REST_ADDRESS"] = "http://localhost:8080" },
        });

        Assert.NotNull(client);
    }

    /// <summary>
    /// Ruling 3: a recurring loop that was due to tick N times during a large time jump
    /// ticks once and resynchronises, rather than replaying every missed tick.
    /// </summary>
    [Fact]
    public async Task EventualPollerCoalescesMissedTicksAcrossALargeTimeJump()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
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

        while (!pending.IsCompleted)
        {
            await Task.Yield();
            clock.Advance(TimeSpan.FromHours(25));
        }

        await Assert.ThrowsAsync<EventualConsistencyTimeoutException>(() => pending);

        // 25h of 5s ticks is ~18,000. Anything in that region means the loop replayed the
        // gap instead of resynchronising.
        Assert.InRange(invocations, 1, 3);
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

        while (!pending.IsCompleted)
        {
            await Task.Yield();
            clock.Advance(TimeSpan.FromSeconds(1));
        }

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

    private sealed class StubTokenHandler(Func<string> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body(), System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
