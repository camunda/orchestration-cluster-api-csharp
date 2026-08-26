namespace Camunda.Orchestration.Sdk;

/// <summary>
/// A <see cref="TimeProvider"/> decorator whose <see cref="GetUtcNow"/> never moves backwards.
///
/// <para>The SDK resolves all runtime cadence — worker poll loops, eventual consistency
/// polling, retry backoff, backpressure decay, and OAuth refresh — through wall-clock time
/// rather than a monotonic source, so that pinning the clock in a test also pins the
/// client's own timing. Wall clocks can jump backwards (NTP correction, VM suspend and
/// resume, manual adjustment), and a deadline computed against a backwards-moving clock
/// waits longer than it was asked to. Clamping recovers that safety without reintroducing
/// a second notion of time.</para>
///
/// <para>This decorator wraps the <em>live</em> clock only. A test clock such as
/// <c>FakeTimeProvider</c> is used as supplied, so a test remains free to move time
/// backwards deliberately.</para>
/// </summary>
public sealed class CamundaTimeProvider : TimeProvider
{
    private readonly TimeProvider _inner;
    private long _lastUtcTicks;

    /// <summary>
    /// The default live clock: the system clock, clamped so it cannot move backwards.
    /// </summary>
    public static CamundaTimeProvider Live { get; } = new(TimeProvider.System);

    /// <summary>
    /// Wraps <paramref name="inner"/> so that observed time never decreases.
    /// </summary>
    public CamundaTimeProvider(TimeProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        var candidateTicks = _inner.GetUtcNow().UtcTicks;
        var last = Volatile.Read(ref _lastUtcTicks);

        while (candidateTicks > last)
        {
            var observed = Interlocked.CompareExchange(ref _lastUtcTicks, candidateTicks, last);
            if (observed == last)
                return new DateTimeOffset(candidateTicks, TimeSpan.Zero);
            last = observed;
        }

        return new DateTimeOffset(last, TimeSpan.Zero);
    }

    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    /// <inheritdoc />
    public override long TimestampFrequency => _inner.TimestampFrequency;

    /// <summary>
    /// Delegates to the wrapped provider.
    /// </summary>
    /// <remarks>
    /// This is the monotonic API. SDK runtime code resolves time through
    /// <see cref="GetUtcNow"/> instead, so that pinning the clock affects it; the
    /// override exists only so the decorator stays a faithful <see cref="TimeProvider"/>.
    /// </remarks>
    public override long GetTimestamp() => _inner.GetTimestamp();

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => _inner.CreateTimer(callback, state, dueTime, period);
}
