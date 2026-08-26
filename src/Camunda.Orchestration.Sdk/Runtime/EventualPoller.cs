using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk;

/// <summary>
/// Options for eventual consistency polling behavior.
/// </summary>
public sealed class ConsistencyOptions<T>
{
    /// <summary>
    /// Maximum time to wait for the data to become consistent, in milliseconds.
    /// Set to 0 to skip eventual consistency handling.
    /// </summary>
    public int WaitUpToMs { get; init; }

    /// <summary>
    /// Poll interval in milliseconds (default: 500).
    /// </summary>
    public int PollIntervalMs { get; init; } = 500;

    /// <summary>
    /// Optional predicate: when true, the response is considered consistent.
    /// If not set, any non-null response with items (where applicable) is accepted.
    /// </summary>
    public Func<T, bool>? IsConsistent { get; init; }
}

/// <summary>
/// Handles eventual consistency polling for search endpoints.
/// </summary>
internal static class EventualPoller
{
    public static async Task<T> PollAsync<T>(
        string operationId,
        bool isGet,
        Func<Task<T>> invoke,
        ConsistencyOptions<T> options,
        ILogger logger,
        TimeProvider timeProvider,
        CancellationToken ct = default)
    {
        if (options.WaitUpToMs <= 0)
            return await invoke();

        var interval = TimeSpan.FromMilliseconds(options.PollIntervalMs > 0 ? options.PollIntervalMs : 500);
        var started = timeProvider.GetUtcNow();
        var deadline = started + TimeSpan.FromMilliseconds(options.WaitUpToMs);
        var nextPoll = started;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await invoke();

                if (options.IsConsistent != null)
                {
                    if (options.IsConsistent(result))
                    {
                        if (logger.IsEnabled(LogLevel.Debug))
                            logger.LogDebug("Eventual consistency satisfied for {Op} after {Elapsed}ms",
                                operationId, ElapsedMs(timeProvider, started));
                        return result;
                    }
                }
                else if (result != null)
                {
                    return result;
                }
            }
            catch (HttpSdkException ex) when (ex.Status == 404 && isGet)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Eventual consistency: 404 for GET {Op}, will retry", operationId);
            }

            var now = timeProvider.GetUtcNow();
            if (now >= deadline)
            {
                var elapsed = ElapsedMs(timeProvider, started);
                throw new EventualConsistencyTimeoutException(
                    $"Eventual consistency timeout after {elapsed}ms for {operationId}",
                    operationId,
                    elapsed);
            }

            // Schedule against the previous tick rather than "now + interval" so a slow
            // invoke does not stretch the cadence. If ticks were missed entirely — a slow
            // invoke, or the clock jumping forward — resynchronise to one interval from now
            // rather than replaying every tick that fell in the gap.
            nextPoll += interval;
            if (nextPoll <= now)
                nextPoll = now + interval;
            if (nextPoll > deadline)
                nextPoll = deadline;

            var wait = nextPoll - now;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, timeProvider, ct);
        }
    }

    private static int ElapsedMs(TimeProvider timeProvider, DateTimeOffset started)
        => (int)(timeProvider.GetUtcNow() - started).TotalMilliseconds;
}
