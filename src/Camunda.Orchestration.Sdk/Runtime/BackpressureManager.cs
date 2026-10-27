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
    private readonly object _gate = new();
    private readonly Queue<TaskCompletionSource<bool>> _waiters = new();
    private readonly bool _gateEnabled;
    private int _permitsMax;
    private int _inFlight;
    private int _consecutive;
    private long _lastSignalMs;
    private bool _disposed;

    public BackpressureManager(BackpressureConfig config, ILogger logger, TimeProvider timeProvider)
    {
        _config = config;
        _logger = logger;
        _timeProvider = timeProvider;

        if (config.Enabled && !config.ObserveOnly)
        {
            _permitsMax = config.InitialMax;
            _gateEnabled = true;
        }
    }

    public async Task AcquireAsync(CancellationToken ct = default)
    {
        if (!_gateEnabled)
            return;

        TaskCompletionSource<bool>? tcs = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_inFlight < _permitsMax)
            {
                _inFlight++;
                return;
            }

            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(tcs);
        }

        // Cancellation unqueues the waiter. Queue<T> has no Remove, so a cancelled
        // waiter stays enqueued and is discarded lazily when it reaches the head
        // (see ReleasePermitLocked). WaitAsync(ct) cancels only the wait, not the
        // source: left untouched, the still-pending source would be granted a permit
        // by a later release that nobody ever returns — a permit leak that, at a cap
        // of one, wedges every subsequent non-exempt acquisition. So resolve the
        // source's fate atomically under _gate below.
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
                // straight to the next waiter. Otherwise cancellation won, so mark
                // the source cancelled while holding _gate — before any release can
                // dequeue it — so ReleasePermitLocked's TrySetResult fails and it is
                // skipped instead of being granted an unreturned permit.
                if (tcs.Task.IsCompletedSuccessfully)
                {
                    _inFlight--;
                    ReleasePermitLocked();
                }
                else
                {
                    tcs.TrySetCanceled(ct);
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
            var waiter = _waiters.Dequeue();
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
            if (_consecutive > 0 && now - _lastSignalMs > _config.DecayQuietMs)
            {
                _consecutive = 0;
                if (_gateEnabled)
                {
                    var newMax = _permitsMax + _config.RecoveryStep;
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("Backpressure: recovering permits {Old} -> {New}", _permitsMax, newMax);
                    _permitsMax = newMax;
                    // A raised cap may admit queued waiters immediately.
                    ReleasePermitLocked();
                }
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
                _waiters.Dequeue().TrySetException(new ObjectDisposedException(nameof(BackpressureManager)));
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
