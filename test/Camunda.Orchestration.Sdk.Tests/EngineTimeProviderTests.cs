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

    [Fact]
    public void CamundaClient_SatisfiesTheEngineClockTarget()
    {
        // The interface exists so the provider does not depend on the generated surface. That
        // only pays off if a real client still satisfies it.
        Assert.True(typeof(IEngineClockTarget).IsAssignableFrom(typeof(CamundaClient)));
    }
}
