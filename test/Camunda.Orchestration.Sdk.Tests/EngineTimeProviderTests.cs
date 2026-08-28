namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Stands in for the engine, recording the instants it was pinned to.
/// </summary>
internal sealed class FakeEngine : IEngineClockTarget
{
    private readonly Func<long, Task>? _onPin;

    public FakeEngine(Func<long, Task>? onPin = null) => _onPin = onPin;

    public List<long> Pins { get; } = [];

    public int Resets { get; private set; }

    public async Task PinClockAsync(ClockPinRequest body, CancellationToken ct = default)
    {
        if (_onPin is not null)
        {
            await _onPin(body.Timestamp).ConfigureAwait(false);
        }

        lock (Pins)
        {
            Pins.Add(body.Timestamp);
        }
    }

    public Task ResetClockAsync(CancellationToken ct = default)
    {
        Resets++;
        return Task.CompletedTask;
    }
}

public class EngineTimeProviderTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task AdvanceAsync_MovesEngineTimeInsteadOfWaiting()
    {
        var engine = new FakeEngine();
        using var provider = new EngineTimeProvider(engine, Start);

        await provider.AdvanceAsync(TimeSpan.FromMinutes(1));

        Assert.Equal(Start.AddMinutes(1), provider.GetUtcNow());
        Assert.Equal([Start.AddMinutes(1).ToUnixTimeMilliseconds()], engine.Pins);
    }

    // The whole point: existing call sites already say Task.Delay(d, timeProvider, ct), so
    // routing them through this provider makes them engine-bound without any code change.
    [Fact]
    public async Task TaskDelay_ThroughTheProvider_AdvancesEngineTimeAndReturnsAtOnce()
    {
        var engine = new FakeEngine();
        using var provider = new EngineTimeProvider(engine, Start);

        var startedReal = Environment.TickCount64;
        await Task.Delay(TimeSpan.FromMinutes(1), provider, CancellationToken.None);

        Assert.Equal(Start.AddMinutes(1), provider.GetUtcNow());
        Assert.True(Environment.TickCount64 - startedReal < 5_000);
    }

    // The shape that burned a real 60s in CI: poll something that never becomes ready.
    [Fact]
    public async Task PollLoop_CoversAMinuteOfEngineTimeInRealMilliseconds()
    {
        var engine = new FakeEngine();
        using var provider = new EngineTimeProvider(engine, Start);

        var startedReal = Environment.TickCount64;
        var polls = 0;
        while (provider.GetUtcNow() < Start.AddMinutes(1))
        {
            polls++;
            await Task.Delay(TimeSpan.FromSeconds(1), provider, CancellationToken.None);
        }

        Assert.Equal(60, polls);
        Assert.Equal(Start.AddMinutes(1), provider.GetUtcNow());
        Assert.True(Environment.TickCount64 - startedReal < 10_000);
    }

    [Fact]
    public async Task PinAsync_SetsAnAbsoluteInstant()
    {
        var engine = new FakeEngine();
        using var provider = new EngineTimeProvider(engine, Start);
        var target = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

        await provider.PinAsync(target);

        Assert.Equal(target, provider.GetUtcNow());
        Assert.Equal([target.ToUnixTimeMilliseconds()], engine.Pins);
    }

    [Fact]
    public async Task PinAsync_NeverMovesTimeBackwards()
    {
        var engine = new FakeEngine();
        using var provider = new EngineTimeProvider(engine, Start.AddSeconds(5));

        await provider.PinAsync(Start.AddSeconds(1));

        Assert.Equal(Start.AddSeconds(5), provider.GetUtcNow());
        Assert.Empty(engine.Pins);
    }

    [Fact]
    public async Task AdvanceAsync_RejectsNegativeDurations()
    {
        var engine = new FakeEngine();
        using var provider = new EngineTimeProvider(engine, Start);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => provider.AdvanceAsync(TimeSpan.FromSeconds(-1)));
    }

    // Without serialisation both callers compute from the same reading, so a late-resolving
    // earlier pin can land last and drag engine time backwards.
    [Fact]
    public async Task ConcurrentAdvances_AreSerialisedAndNeverGoBackwards()
    {
        var engine = new FakeEngine(async ts =>
        {
            // The longer jump answers faster, so an unserialised version settles on the
            // earlier instant.
            await Task.Delay(ts > 2_000 ? 1 : 30).ConfigureAwait(false);
        });
        using var provider = new EngineTimeProvider(engine, Start);

        await Task.WhenAll(
            provider.AdvanceAsync(TimeSpan.FromSeconds(5)),
            provider.AdvanceAsync(TimeSpan.FromSeconds(1)));

        // Serialised, the two advances compose: 5s then a further 1s.
        Assert.Equal(Start.AddSeconds(6), provider.GetUtcNow());
        Assert.Equal(engine.Pins.OrderBy(p => p), engine.Pins);
    }

    // Delays are not advances: each fixes its wake instant when scheduled, so two that overlap
    // settle on the later of the two rather than stacking, exactly as on a real clock.
    [Fact]
    public async Task ConcurrentDelays_SettleOnTheLaterWakePointRatherThanSumming()
    {
        // The pin has to be slow enough that both delays are in flight before either lands.
        // With an instant engine the first completes before the second is even constructed,
        // and 5s + 1s from the new reading would be the correct answer.
        var engine = new FakeEngine(_ => Task.Delay(50));
        using var provider = new EngineTimeProvider(engine, Start);

        var first = Task.Delay(TimeSpan.FromSeconds(5), provider, CancellationToken.None);
        var second = Task.Delay(TimeSpan.FromSeconds(1), provider, CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.Equal(Start.AddSeconds(5), provider.GetUtcNow());
    }

    [Fact]
    public async Task ResetAsync_ReleasesTheEngine()
    {
        var engine = new FakeEngine();
        using var provider = new EngineTimeProvider(engine, Start);

        await provider.ResetAsync();

        Assert.Equal(1, engine.Resets);
    }

    [Fact]
    public async Task AdvanceAsync_SurfacesAFailedPinAndDoesNotAdoptTheTime()
    {
        var engine = new FakeEngine(_ => throw new InvalidOperationException("engine unreachable"));
        using var provider = new EngineTimeProvider(engine, Start);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AdvanceAsync(TimeSpan.FromSeconds(1)));

        Assert.Equal(Start, provider.GetUtcNow());
    }

    // A timer cannot hand an exception to its awaiter, so a failed pin completes the delay
    // and is reported instead. Hanging the caller forever would be strictly worse.
    [Fact]
    public async Task TaskDelay_CompletesAndReportsWhenAPinFails()
    {
        var failures = new List<Exception>();
        var engine = new FakeEngine(_ => throw new InvalidOperationException("engine unreachable"));
        using var provider = new EngineTimeProvider(
            engine, Start, ex =>
            {
                lock (failures)
                {
                    failures.Add(ex);
                }
            });

        await Task.Delay(TimeSpan.FromSeconds(1), provider, CancellationToken.None);

        Assert.Single(failures);
        Assert.Equal(Start, provider.GetUtcNow());
    }

    // Nothing observes the timer loop's task, so an escaping callback exception would surface
    // later as an unobserved fault and stop the timer with no explanation.
    [Fact]
    public async Task ThrowingCallback_IsReportedRatherThanLostAsAnUnobservedFault()
    {
        var failures = new List<Exception>();
        var engine = new FakeEngine();
        using var provider = new EngineTimeProvider(
            engine, Start, ex =>
            {
                lock (failures)
                {
                    failures.Add(ex);
                }
            });

        var timer = provider.CreateTimer(
            _ => throw new InvalidOperationException("callback exploded"), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        await Task.Delay(200);
        timer.Dispose();

        lock (failures)
        {
            Assert.Single(failures);
            Assert.Equal("callback exploded", failures[0].Message);
        }
    }

    // An epoch default would silently pin a real engine back to 1970 the first time anything
    // advanced, for a caller who simply did not pass a start.
    [Fact]
    public void DefaultStart_IsLiveTimeNotTheEpoch()
    {
        var engine = new FakeEngine();
        using var provider = new EngineTimeProvider(engine);

        var now = provider.GetUtcNow();

        Assert.True(
            now > DateTimeOffset.UtcNow.AddMinutes(-5),
            $"default start should track live time, got {now:O}");
    }

    // Fault reporting is the last line of defence. If it can throw, it takes down the loop it
    // exists to keep alive.
    [Fact]
    public async Task ThrowingFaultHandler_DoesNotEscapeTheTimerLoop()
    {
        var engine = new FakeEngine(_ => throw new InvalidOperationException("engine unreachable"));
        using var provider = new EngineTimeProvider(
            engine, Start, _ => throw new InvalidOperationException("reporter exploded"));

        // Completes rather than hanging or faulting.
        await Task.Delay(TimeSpan.FromSeconds(1), provider, CancellationToken.None);

        Assert.Equal(Start, provider.GetUtcNow());
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(-2)]
    public void CreateTimer_RejectsNegativeSchedules(int ms)
    {
        var engine = new FakeEngine();
        using var provider = new EngineTimeProvider(engine, Start);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => provider.CreateTimer(_ => { }, null, TimeSpan.FromMilliseconds(ms), Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => provider.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(ms)));
    }

    [Fact]
    public void Change_RejectsNegativeSchedules()
    {
        var engine = new FakeEngine();
        using var provider = new EngineTimeProvider(engine, Start);
        using var timer = provider.CreateTimer(
            _ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => timer.Change(TimeSpan.FromSeconds(-1), Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-1)));
    }

    // Timeout.InfiniteTimeSpan is negative but means "never", so it must stay legal.
    [Fact]
    public void InfiniteTimeSpan_RemainsAValidSchedule()
    {
        var engine = new FakeEngine();
        using var provider = new EngineTimeProvider(engine, Start);

        using var timer = provider.CreateTimer(
            _ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        Assert.True(timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan));
    }

    // A failed pin leaves time where it was, so the next wake point is identical and nothing in
    // the loop waits on real time. Without stopping, a periodic timer spins against an
    // unreachable engine.
    [Fact]
    public async Task PeriodicTimer_StopsAfterAFailedPinRatherThanSpinning()
    {
        var faults = 0;
        var fired = 0;
        var engine = new FakeEngine(_ => throw new InvalidOperationException("engine unreachable"));
        using var provider = new EngineTimeProvider(
            engine, Start, _ => Interlocked.Increment(ref faults));

        using var timer = provider.CreateTimer(
            _ => Interlocked.Increment(ref fired), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        await Task.Delay(250);

        // One tick: the awaiter is released, the fault is reported, and the loop stops.
        Assert.Equal(1, Volatile.Read(ref faults));
        Assert.Equal(1, Volatile.Read(ref fired));
        Assert.Equal(Start, provider.GetUtcNow());
    }

    [Fact]
    public void CamundaClient_SatisfiesTheEngineClockTarget()
    {
        // The interface exists so the provider does not depend on the generated surface. That
        // only pays off if a real client still satisfies it.
        Assert.True(typeof(IEngineClockTarget).IsAssignableFrom(typeof(CamundaClient)));
    }

    public class TimerRescheduling
    {
        private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

        // Change supersedes the previous schedule. Left running, an old loop keeps firing the
        // callback and advancing engine time behind a caller that has moved the deadline.
        [Fact]
        public async Task Change_SupersedesThePreviousSchedule()
        {
            var engine = new FakeEngine(_ => Task.Delay(20));
            using var provider = new EngineTimeProvider(engine, Start);
            var fired = 0;

            var timer = provider.CreateTimer(
                _ => Interlocked.Increment(ref fired), null,
                TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);

            timer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
            await Task.Delay(300);
            timer.Dispose();

            Assert.Equal(1, Volatile.Read(ref fired));
        }

        // Rescheduling to Infinite disables the timer. It must stop the loop, not merely
        // decline to start another one.
        //
        // A pin already in flight when Change is called still lands: the engine has been told,
        // and pinning back would move time backwards. What must stop is the callback and any
        // further advancement.
        [Fact]
        public async Task Change_ToInfinite_StopsTheTimerAdvancingTime()
        {
            var engine = new FakeEngine(_ => Task.Delay(20));
            using var provider = new EngineTimeProvider(engine, Start);
            var fired = 0;

            var timer = provider.CreateTimer(
                _ => Interlocked.Increment(ref fired), null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            await Task.Delay(150);
            var settled = provider.GetUtcNow();
            await Task.Delay(150);
            var later = provider.GetUtcNow();
            timer.Dispose();

            Assert.Equal(0, Volatile.Read(ref fired));
            Assert.Equal(settled, later);
            // At most the one pin that was already in flight.
            Assert.True(
                later <= Start.AddSeconds(1), $"time ran on after the timer was disabled: {later}");
        }

        [Fact]
        public async Task PeriodicTimer_KeepsFiringUntilDisposed()
        {
            // The pin must cost something. With an instant engine a periodic timer advances
            // engine time as fast as it can pin, which is the documented fast-forward but makes
            // for an unbounded test.
            var engine = new FakeEngine(_ => Task.Delay(20));
            using var provider = new EngineTimeProvider(engine, Start);
            var fired = 0;

            var timer = provider.CreateTimer(
                _ => Interlocked.Increment(ref fired), null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            // Wait for the behaviour rather than for a duration: a fixed sleep makes the tick
            // count a function of machine speed, which is how this failed on one TFM and not
            // the other. The timeout is a safety net, not the assertion.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Volatile.Read(ref fired) < 2 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.True(
                Volatile.Read(ref fired) >= 2,
                $"expected repeated firing, got {Volatile.Read(ref fired)}");

            timer.Dispose();
            var afterDispose = Volatile.Read(ref fired);
            await Task.Delay(150);

            Assert.Equal(afterDispose, Volatile.Read(ref fired));
        }
    }

    // An in-flight pin completing after the reset would leave the engine pinned even though
    // the caller awaited a reset.
    [Fact]
    public async Task ResetAsync_IsSerialisedWithPinning()
    {
        var events = new List<string>();
        var engine = new FakeEngine(async _ =>
        {
            await Task.Delay(50).ConfigureAwait(false);
            lock (events)
            {
                events.Add("pin");
            }
        });
        using var provider = new EngineTimeProvider(engine, Start);

        var advancing = provider.AdvanceAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(10);
        await provider.ResetAsync();
        lock (events)
        {
            events.Add("reset");
        }

        await advancing;

        Assert.Equal(["pin", "reset"], events);
    }
}
