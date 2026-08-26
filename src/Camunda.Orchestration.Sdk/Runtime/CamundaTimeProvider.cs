namespace Camunda.Orchestration.Sdk;

/// <summary>
/// A <see cref="TimeProvider"/> decorator whose <see cref="GetUtcNow"/> never moves backwards.
///
/// <para>The SDK resolves all runtime cadence — worker poll loops, eventual consistency
/// polling, retry backoff, backpressure decay, and OAuth refresh — through wall-clock time
/// rather than a monotonic source, so that pinning the clock in a test also pins the
/// client's own timing.</para>
///
/// <para>Wall clocks can jump backwards (NTP correction, VM suspend and resume, manual
/// adjustment), and a deadline computed against a backwards-moving clock waits longer than
/// it was asked to. A backward step is absorbed and then paid back gradually out of forward
/// progress, so readings never decrease, keep advancing immediately after a jump, and
/// converge back to the underlying clock rather than staying ahead of it forever.</para>
///
/// <para>This decorator wraps the <em>live</em> clock only. A test clock such as
/// <c>FakeTimeProvider</c> is used as supplied, so a test remains free to move time
/// backwards deliberately.</para>
/// </summary>
public sealed class CamundaTimeProvider : TimeProvider
{
    /// <summary>
    /// Fraction of forward progress used to pay down an absorbed backward correction:
    /// 1/16, so reported time runs at 15/16 of the true rate until it has converged.
    /// </summary>
    private const int SlewDivisor = 16;

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

            if (innerTicks < _lastInnerTicks)
            {
                // Absorb the backward step instead of clamping to the previous high-water
                // mark. Clamping holds logical time still until the underlying clock catches
                // back up, so an hour-long correction would add an hour to every deadline in
                // flight — the failure this class exists to avoid.
                _offsetTicks += _lastInnerTicks - innerTicks;
            }
            else if (_offsetTicks > 0)
            {
                // ...but do not carry the correction forever, or reported time would stay
                // permanently ahead of the true clock and disagree with server-supplied
                // timestamps. Pay it back out of forward progress instead, the way NTP slews
                // rather than steps: still strictly non-decreasing, still within a slice of
                // the true rate, and converging back to the underlying clock.
                var forwardTicks = innerTicks - _lastInnerTicks;
                _offsetTicks -= Math.Min(_offsetTicks, forwardTicks / SlewDivisor);
            }

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
