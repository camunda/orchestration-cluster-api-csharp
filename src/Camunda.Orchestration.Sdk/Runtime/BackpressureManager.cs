using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Adaptive backpressure management. Mirrors the JS SDK's BackpressureManager.
/// Tracks HTTP 429/503 signals and limits concurrent requests.
/// </summary>
internal sealed class BackpressureManager : IDisposable, IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly BackpressureConfig _config;
    private readonly TimeProvider _timeProvider;

    // The permit gate is a counter plus a FIFO waiter queue rather than a
    // SemaphoreSlim: a semaphore's capacity is fixed at construction, so it could
    // never follow the adaptive _permitsMax as backpressure shrinks and recovery
    // grows it (#479). All mutable state below is guarded by _gate.
    // The waiter queue is a LinkedList so a cancelled acquisition can remove its own
    // node under _gate: with a plain Queue a cancelled waiter had to stay enqueued until
    // a release dequeued and discarded it, so repeated cancelled acquisitions under a
    // held permit grew the queue without bound — retaining completed waiter tasks and
    // their cancellation tokens after the callers had finished (#479 review).
    private readonly object _gate = new();
    private readonly LinkedList<TaskCompletionSource<bool>> _waiters = new();
    private readonly bool _gateEnabled;
    private int _permitsMax;
    private int _inFlight;
    private int _consecutive;
    private long _lastSignalMs;
    private long _lastRecoveryMs;
    private bool _disposed;

    public BackpressureManager(BackpressureConfig config, ILogger logger, TimeProvider timeProvider)
    {
        _config = config;
        _logger = logger;
        _timeProvider = timeProvider;

        if (config.Enabled && !config.ObserveOnly)
        {
            // A nonpositive cap can never admit a request: a zero InitialMax queues
            // every acquisition forever, and a zero Floor lets the cap degrade to
            // zero and stall. Reject the misconfiguration here rather than wedge the
            // gate at runtime (#479).
            ArgumentOutOfRangeException.ThrowIfLessThan(config.InitialMax, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(config.Floor, 1);

            _permitsMax = config.InitialMax;
            _gateEnabled = true;
        }
    }

    public async Task AcquireAsync(CancellationToken ct = default)
    {
        if (!_gateEnabled)
            return;

        TaskCompletionSource<bool>? tcs = null;
        LinkedListNode<TaskCompletionSource<bool>>? node = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Honor cancellation before granting or queuing a permit. The free-capacity
            // fast path below returns without touching the token, so a pre-cancelled
            // token would otherwise be handed a permit and InvokeWithRetryAsync would
            // start the operation (serializing its body) despite cancellation — the
            // previous SemaphoreSlim.WaitAsync(ct) rejected this case (#479).
            ct.ThrowIfCancellationRequested();

            if (_inFlight < _permitsMax)
            {
                _inFlight++;
                return;
            }

            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            node = _waiters.AddLast(tcs);
        }

        // Cancellation unqueues the waiter. Resolve the source's fate atomically under
        // _gate below: a waiter left in the list after its caller cancelled would be
        // granted a permit by a later release that nobody ever returns — a permit leak
        // that, at a cap of one, wedges every subsequent non-exempt acquisition. And a
        // cancelled waiter that simply stayed enqueued would accumulate without bound
        // under a held permit (#479 review).
        try
        {
            await tcs.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                // If the grant won the race (the permit was handed over before
                // cancellation was observed), the caller must not leak it: hand it
                // straight to the next waiter. Otherwise cancellation won, so remove the
                // node now and mark the source cancelled, so no later release can grant
                // it an unreturned permit.
                if (tcs.Task.IsCompletedSuccessfully)
                {
                    _inFlight--;
                    ReleasePermitLocked();
                }
                else
                {
                    // WaitAsync(ct)'s cancellation continuation is scheduled
                    // asynchronously, so Dispose() may already have drained the queue —
                    // detaching this node and faulting the source — before we re-acquire
                    // _gate. Removing a node that no longer belongs to the list throws
                    // InvalidOperationException, which would replace the intended
                    // cancellation exception (#480 review). Only remove a node that is
                    // still queued; a detached node has already had its fate resolved
                    // (faulted by Dispose), so there is nothing to clean up.
                    if (node!.List == _waiters)
                    {
                        _waiters.Remove(node);
                        tcs.TrySetCanceled(ct);
                    }
                }
            }

            throw;
        }
    }

    public void Release()
    {
        if (!_gateEnabled)
            return;

        lock (_gate)
        {
            if (_inFlight <= 0)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning(
                        "Backpressure: permit release overflow — a permit was released without a matching acquire. " +
                        "This is usually harmless (e.g. a request was exempt from backpressure but still released). " +
                        "If this appears frequently, check that backpressure-exempt operations are not inadvertently releasing permits.");
                return;
            }

            _inFlight--;
            ReleasePermitLocked();
        }
    }

    /// <summary>
    /// Hands freed capacity to queued waiters while there is room under the current
    /// adaptive cap. Caller must hold <see cref="_gate"/>.
    /// </summary>
    private void ReleasePermitLocked()
    {
        while (_inFlight < _permitsMax && _waiters.Count > 0)
        {
            var waiter = _waiters.First!.Value;
            _waiters.RemoveFirst();
            if (!waiter.TrySetResult(true))
                continue; // cancelled while queued — skip it and keep draining

            _inFlight++;
        }
    }

    public void RecordBackpressure()
    {
        lock (_gate)
        {
            _consecutive++;
            _lastSignalMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

            if (_gateEnabled)
            {
                var factor = _consecutive >= _config.SevereThreshold
                    ? _config.SevereFactor
                    : _config.SoftFactor;

                var newMax = Math.Max(_config.Floor, (int)(_permitsMax * factor));
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Backpressure: reducing permits {Old} -> {New} (consecutive={Consecutive})",
                        _permitsMax, newMax, _consecutive);
                _permitsMax = newMax;
            }
        }
    }

    public void RecordHealthy()
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

            // Severity decay is separate from permit recovery. Decay only clears the
            // consecutive-signal counter after a quiet period; it must not gate (or be
            // the only trigger for) recovery, or the cap would rise exactly once and
            // then stick (#479).
            if (_consecutive > 0 && now - _lastSignalMs > _config.DecayQuietMs)
                _consecutive = 0;

            // Recovery restores capacity on its own cadence (RecoveryIntervalMs),
            // independent of severity, so healthy completions keep raising the cap
            // back toward its initial size once the broker has recovered. But it must
            // only fire once the system has stayed quiet: a success recorded
            // immediately after a backpressure signal must not raise the cap while the
            // broker is still shedding load, or queued requests would be re-admitted
            // into an overloaded broker (README: recover "if the system stays quiet").
            // So require both the recovery interval AND a quiet period since the last
            // signal. _lastSignalMs starts at 0, so before any signal the quiet window
            // is trivially satisfied and recovery is unobstructed.
            var quiet = now - _lastSignalMs >= _config.DecayQuietMs;
            if (_gateEnabled && quiet && now - _lastRecoveryMs >= _config.RecoveryIntervalMs)
            {
                _lastRecoveryMs = now;
                var newMax = _permitsMax + _config.RecoveryStep;
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Backpressure: recovering permits {Old} -> {New}", _permitsMax, newMax);
                _permitsMax = newMax;
                // A raised cap may admit queued waiters immediately.
                ReleasePermitLocked();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;

            // Fault any queued waiters so a dispose during contention does not hang
            // them forever; in-flight holders release normally (Release stays a no-op
            // safe path after disposal).
            while (_waiters.Count > 0)
            {
                var waiter = _waiters.First!.Value;
                _waiters.RemoveFirst();
                waiter.TrySetException(new ObjectDisposedException(nameof(BackpressureManager)));
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public BackpressureState GetState()
    {
        lock (_gate)
        {
            return new()
            {
                Severity = _consecutive == 0 ? "healthy" : _consecutive >= _config.SevereThreshold ? "severe" : "soft",
                PermitsMax = _gateEnabled ? _permitsMax : null,
                Consecutive = _consecutive,
            };
        }
    }
}

public sealed class BackpressureState
{
    public string Severity { get; init; } = "healthy";
    public int? PermitsMax { get; init; }
    public int Consecutive { get; init; }
}
