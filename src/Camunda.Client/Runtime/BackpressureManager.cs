using Microsoft.Extensions.Logging;

namespace Camunda.Client.Runtime;

/// <summary>
/// Adaptive backpressure management. Mirrors the JS SDK's BackpressureManager.
/// Tracks HTTP 429/503 signals and limits concurrent requests.
/// </summary>
internal sealed class BackpressureManager
{
    private readonly ILogger _logger;
    private readonly BackpressureConfig _config;
    private readonly SemaphoreSlim? _semaphore;
    private int _permitsMax;
    private int _consecutive;
    private long _lastSignalMs;

    public BackpressureManager(BackpressureConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;

        if (config.Enabled && !config.ObserveOnly)
        {
            _permitsMax = config.InitialMax;
            _semaphore = new SemaphoreSlim(config.InitialMax, config.InitialMax);
        }
    }

    public async Task AcquireAsync(CancellationToken ct = default)
    {
        if (_semaphore != null)
            await _semaphore.WaitAsync(ct);
    }

    public void Release()
    {
        if (_semaphore != null)
        {
            try
            { _semaphore.Release(); }
            catch (SemaphoreFullException) { /* ignore */ }
        }
    }

    public void RecordBackpressure()
    {
        _consecutive++;
        _lastSignalMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (_config.Enabled && !_config.ObserveOnly && _semaphore != null)
        {
            var factor = _consecutive >= _config.SevereThreshold
                ? _config.SevereFactor
                : _config.SoftFactor;

            var newMax = Math.Max(_config.Floor, (int)(_permitsMax * factor));
            _logger.LogDebug("Backpressure: reducing permits {Old} -> {New} (consecutive={Consecutive})",
                _permitsMax, newMax, _consecutive);
            _permitsMax = newMax;
        }
    }

    public void RecordHealthy()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_consecutive > 0 && now - _lastSignalMs > _config.DecayQuietMs)
        {
            _consecutive = 0;
            if (_config.Enabled && !_config.ObserveOnly)
            {
                var newMax = _permitsMax + _config.RecoveryStep;
                _logger.LogDebug("Backpressure: recovering permits {Old} -> {New}", _permitsMax, newMax);
                _permitsMax = newMax;
            }
        }
    }

    public BackpressureState GetState() => new()
    {
        Severity = _consecutive == 0 ? "healthy" : _consecutive >= _config.SevereThreshold ? "severe" : "soft",
        PermitsMax = _config.Enabled && !_config.ObserveOnly ? _permitsMax : null,
        Consecutive = _consecutive,
    };
}

public sealed class BackpressureState
{
    public string Severity { get; init; } = "healthy";
    public int? PermitsMax { get; init; }
    public int Consecutive { get; init; }
}
