using Microsoft.Extensions.Time.Testing;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that also reports how many timers have been registered
/// against it.
///
/// <para>Advancing a fake clock is only meaningful once the code under test is actually
/// parked on it. Waiting for an observable side effect instead — a request having been
/// issued, say — races: the code typically registers its timer only after finishing that
/// work, so an advance can land first, fire nothing, and leave the test waiting on a clock
/// that will never move again.</para>
///
/// <para><see cref="TimersCreated"/> is the precise signal. <c>Task.Delay(TimeSpan,
/// TimeProvider, CancellationToken)</c> registers through <see cref="CreateTimer"/>, so a
/// test can wait for the count to rise, advance once, and know the advance was observed.</para>
/// </summary>
internal sealed class InstrumentedFakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly FakeTimeProvider _inner = new(start);
    private int _timersCreated;

    /// <summary>Number of timers registered so far.</summary>
    public int TimersCreated => Volatile.Read(ref _timersCreated);

    /// <summary>Move the clock forward, firing any timers that fall in the interval.</summary>
    public void Advance(TimeSpan delta) => _inner.Advance(delta);

    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    public override long TimestampFrequency => _inner.TimestampFrequency;

    public override long GetTimestamp() => _inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _timersCreated);
        return _inner.CreateTimer(callback, state, dueTime, period);
    }
}
