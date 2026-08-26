namespace Camunda.Orchestration.Sdk;

/// <summary>
/// A <see cref="TimeProvider"/> decorator whose <see cref="GetUtcNow"/> never moves backwards.
///
/// <para>The SDK resolves all runtime cadence — worker poll loops, eventual consistency
/// polling, retry backoff, backpressure decay, and OAuth refresh — through wall-clock time
/// rather than a monotonic source, so that pinning the clock in a test also pins the
/// client's own timing. Wall clocks can jump backwards (NTP correction, VM suspend and
/// resume, manual adjustment), and a deadline computed against a backwards-moving clock
/// waits longer than it was asked to. Absorbing the jump recovers that safety without
/// reintroducing a second notion of time.</para>
///
/// <para>This decorator wraps the <em>live</em> clock only. A test clock such as
/// <c>FakeTimeProvider</c> is used as supplied, so a test remains free to move time
/// backwards deliberately.</para>
/// </summary>
public sealed class CamundaTimeProvider : TimeProvider
{
    private readonly TimeProvider _inner;
    private readonly object _gate = new();
    private long _lastInnerTicks;
    private long _offsetTicks;

    /// <summary>
    /// The default live clock: the system clock, made non-decreasing.
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
        lock (_gate)
        {
            var innerTicks = _inner.GetUtcNow().UtcTicks;

            // A backward step is absorbed into a running offset rather than clamped to the
            // previous high-water mark. Clamping would hold logical time still until the
            // underlying clock caught back up, so an hour-long correction would add an hour
            // to every deadline in flight — the very failure this class exists to avoid.
            // Carrying the offset keeps time non-decreasing while preserving the rate of
            // forward progress, so a deadline set before the jump still expires on time.
            if (innerTicks < _lastInnerTicks)
                _offsetTicks += _lastInnerTicks - innerTicks;

            _lastInnerTicks = innerTicks;
            return new DateTimeOffset(innerTicks + _offsetTicks, TimeSpan.Zero);
        }
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
