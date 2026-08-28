namespace Camunda.Orchestration.Sdk;

/// <summary>
/// The engine clock operations <see cref="EngineTimeProvider"/> drives.
///
/// <para>Declared as an interface so the provider does not depend on the generated client
/// surface; <see cref="CamundaClient"/> implements it with its existing methods.</para>
/// </summary>
public interface IEngineClockTarget
{
    /// <summary>Pins the engine clock to an absolute instant.</summary>
    Task PinClockAsync(ClockPinRequest body, CancellationToken ct = default);

    /// <summary>Releases the engine clock back to real time.</summary>
    Task ResetClockAsync(CancellationToken ct = default);
}

/// <summary>
/// A <see cref="TimeProvider"/> bound to the engine's own clock, so client cadence and engine
/// time advance together.
///
/// <para>The engine has long been pinnable through <c>PUT /clock</c>, and since the injected
/// <see cref="TimeProvider"/> landed the client's cadence is virtualisable too — but the two
/// were separate. Pinning the engine left worker poll loops on their own timeline, so a worker
/// waiting on something that never became ready burned real seconds inside a test that was
/// otherwise deterministic.</para>
///
/// <para>This provider closes that. Every delay taken through it — the worker poll loop,
/// eventual-consistency polling, retry backoff, OAuth refresh — moves engine time forward
/// instead of waiting, so cadence <em>drives</em> the engine rather than racing it and a test
/// that would have taken a real minute finishes as fast as the requests complete.</para>
///
/// <para>Intended for tests and embedded scenarios that own the engine. Pinning is global to
/// the cluster, so never point one of these at a shared environment, and always
/// <see cref="ResetAsync"/> when finished.</para>
///
/// <para>The target must be a client that is <em>not</em> itself using this provider. HTTP
/// retry backs off on its client's clock, so a self-referential setup would have a failed pin
/// retry through a delay, which issues another pin.</para>
/// </summary>
public sealed class EngineTimeProvider : TimeProvider, IDisposable
{
    private readonly IEngineClockTarget _engine;
    private readonly Action<Exception>? _onFault;

    // Pins are serialised: the current reading is used to compute the next instant and then
    // replaced, so overlapping callers must not interleave or an earlier pin could land last
    // and drag engine time backwards.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _currentTicks;
    private bool _disposed;

    /// <summary>
    /// Creates a provider that pins <paramref name="engine"/> as time advances.
    /// </summary>
    /// <param name="engine">The client used to pin and reset the engine clock.</param>
    /// <param name="start">
    /// The instant to start from. Defaults to the current live time, because the first advance
    /// pins a real engine to whatever this is: an epoch default would silently send it to 1970.
    /// </param>
    /// <param name="onFault">
    /// Invoked when a tick cannot complete: the engine rejected a pin, or the timer callback
    /// threw. A timer cannot surface an exception to its awaiter without hanging it, so the
    /// failure is reported here instead. Explicit <see cref="PinAsync"/> and
    /// <see cref="AdvanceAsync"/> calls throw.
    /// </param>
    public EngineTimeProvider(
        IEngineClockTarget engine,
        DateTimeOffset? start = null,
        Action<Exception>? onFault = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _onFault = onFault;
        _currentTicks = (start ?? CamundaTimeProvider.Live.GetUtcNow()).UtcTicks;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() =>
        new(Interlocked.Read(ref _currentTicks), TimeSpan.Zero);

    /// <summary>
    /// Pins the engine to an absolute instant. Instants already passed are ignored, so time
    /// never moves backwards.
    /// </summary>
    public Task PinAsync(DateTimeOffset instant, CancellationToken ct = default) =>
        PinCoreAsync(_ => instant.UtcTicks, ct);

    /// <summary>
    /// Moves engine time forward by <paramref name="duration"/>.
    ///
    /// <para>Advances compose: two concurrent calls move time by the sum, because each asked
    /// for its own increment. Delays do not — see <see cref="CreateTimer"/>.</para>
    /// </summary>
    public Task AdvanceAsync(TimeSpan duration, CancellationToken ct = default)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration), duration, "Engine time only moves forward.");
        }

        return PinCoreAsync(current => current + duration.Ticks, ct);
    }

    /// <summary>
    /// Releases the engine clock back to real time. The local reading stays where it was.
    /// </summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        // Serialised with pinning: an in-flight pin completing after the reset would leave the
        // engine pinned even though the caller awaited a reset.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _engine.ResetClockAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PinCoreAsync(Func<long, long> target, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Resolved inside the gate: a caller that computed against a reading taken before
            // an earlier pin completed must not overwrite the later one.
            var targetTicks = target(_currentTicks);
            if (targetTicks <= _currentTicks)
            {
                return;
            }

            var instant = new DateTimeOffset(targetTicks, TimeSpan.Zero);
            await _engine
                .PinClockAsync(
                    new ClockPinRequest { Timestamp = instant.ToUnixTimeMilliseconds() }, ct)
                .ConfigureAwait(false);

            Interlocked.Exchange(ref _currentTicks, targetTicks);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task PinAtLeastAsync(long instantTicks, CancellationToken ct) =>
        PinCoreAsync(_ => instantTicks, ct);

    /// <summary>
    /// Creates a timer that advances engine time rather than waiting for it. This is what
    /// makes every existing <c>Task.Delay(..., timeProvider, ct)</c> in the runtime
    /// engine-bound without touching a single call site.
    ///
    /// <para>A delay fixes its wake instant when it is scheduled, so overlapping delays settle
    /// on the later of their wake points rather than summing — exactly as they would on a real
    /// clock.</para>
    ///
    /// <para>A <em>periodic</em> timer has no real-time throttle here: it advances engine time
    /// by its period on every tick, as fast as the engine can be pinned. That is the intended
    /// fast-forward, but it means a periodic timer left running will race ahead. Prefer
    /// one-shot delays, and dispose periodic timers promptly.</para>
    /// </summary>
    /// <inheritdoc />
    public override ITimer CreateTimer(
        TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        RequireSchedule(dueTime, nameof(dueTime));
        RequireSchedule(period, nameof(period));
        return new EnginePinTimer(this, callback, state, dueTime, period);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private void ReportFault(Exception ex)
    {
        try
        {
            _onFault?.Invoke(ex);
        }
        catch
        {
            // Deliberately swallowed. This is the last line of fault reporting, so a throw here
            // has nowhere to go, and letting it escape would fault the very timer loop the
            // reporting exists to keep alive.
        }
    }

    /// <summary>
    /// <see cref="Timeout.InfiniteTimeSpan"/> means never; anything else must be non-negative.
    /// The usual upper bound does not apply, because time here is virtual: a very long delay is
    /// a legitimate fast-forward rather than an overflow.
    /// </summary>
    private static void RequireSchedule(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                name, value, "Must be non-negative, or Timeout.InfiniteTimeSpan.");
        }
    }

    /// <summary>
    /// A timer that advances engine time rather than waiting for it. This is what makes every
    /// existing <c>Task.Delay(..., timeProvider, ct)</c> in the runtime engine-bound without
    /// touching a single call site.
    /// </summary>
    private sealed class EnginePinTimer : ITimer
    {
        private readonly EngineTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private readonly CancellationTokenSource _cts = new();
        private readonly object _sync = new();
        private CancellationTokenSource? _schedule;
        private TimeSpan _period;
        private bool _disposed;

        internal EnginePinTimer(
            EngineTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            _period = period;
            Change(dueTime, period);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            // Framework code reschedules through here, so the same contract applies as on
            // CreateTimer: bad arguments are rejected rather than silently firing at once.
            RequireSchedule(dueTime, nameof(dueTime));
            RequireSchedule(period, nameof(period));

            if (_disposed)
            {
                return false;
            }

            // Each Change supersedes the previous schedule. Without this a caller that
            // reschedules -- CancellationTokenSource.CancelAfter adjusting its deadline, say --
            // would leave the old loop running: two callbacks, and a disabled timer still
            // advancing engine time.
            CancellationTokenSource? superseded;
            CancellationTokenSource? next = null;

            lock (_sync)
            {
                superseded = _schedule;
                _period = period;
                _schedule = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : next = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            }

            // Cancel but do not dispose: the loop that owns this schedule disposes it on the
            // way out. Disposing here races the loop's own token reads.
            superseded?.Cancel();

            if (next is not null)
            {
                _ = RunAsync(dueTime, next);
            }

            return true;
        }

        private async Task RunAsync(TimeSpan dueTime, CancellationTokenSource schedule)
        {
            // This loop owns the schedule, so it disposes it. Change only cancels, which keeps
            // disposal off the path where this loop is still reading the token.
            try
            {
                var due = dueTime;
                while (!_disposed && !schedule.IsCancellationRequested)
                {
                    // Fixed when the delay is scheduled, so overlapping delays settle on the
                    // later wake point instead of stacking their durations.
                    var wakeAt = _owner.GetUtcNow().UtcTicks + due.Ticks;

                    try
                    {
                        await _owner.PinAtLeastAsync(wakeAt, schedule.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Swallowing would hang the awaiter forever, which is strictly worse
                        // than letting it proceed against an engine that is evidently unwell.
                        // Report and fire, so the caller fails on its next request instead of
                        // never returning.
                        _owner.ReportFault(ex);
                    }

                    if (_disposed || schedule.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        _callback(_state);
                    }
                    catch (Exception ex)
                    {
                        // Nothing observes this task, so an escaping callback exception would
                        // surface later as an unobserved fault and stop the timer with no
                        // explanation. Report it and stop deliberately.
                        _owner.ReportFault(ex);
                        return;
                    }

                    TimeSpan period;
                    lock (_sync)
                    {
                        // A Change from inside the callback has already superseded this loop.
                        if (!ReferenceEquals(_schedule, schedule))
                        {
                            return;
                        }

                        period = _period;
                    }

                    if (period == Timeout.InfiniteTimeSpan || period <= TimeSpan.Zero)
                    {
                        return;
                    }

                    // Nothing here waits on real time, so without a yield a periodic timer
                    // whose pin completes synchronously would monopolise its thread.
                    await Task.Yield();

                    due = period;
                }
            }
            catch (ObjectDisposedException)
            {
                // The timer was disposed underneath us. That is a cancellation, not a fault.
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_schedule, schedule))
                    {
                        _schedule = null;
                    }
                }

                schedule.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            CancellationTokenSource? schedule;
            lock (_sync)
            {
                schedule = _schedule;
                _schedule = null;
            }

            // Cancel only. The running loop owns its schedule and disposes it on exit, so
            // disposing here would race its token reads.
            schedule?.Cancel();
            _cts.Cancel();
            _cts.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
