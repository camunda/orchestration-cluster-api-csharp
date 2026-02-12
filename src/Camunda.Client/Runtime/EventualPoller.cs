using Microsoft.Extensions.Logging;

namespace Camunda.Client.Runtime;

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
        CancellationToken ct = default)
    {
        if (options.WaitUpToMs <= 0)
            return await invoke();

        var elapsed = 0;
        var interval = options.PollIntervalMs > 0 ? options.PollIntervalMs : 500;

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
                        logger.LogDebug("Eventual consistency satisfied for {Op} after {Elapsed}ms", operationId, elapsed);
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
                logger.LogDebug("Eventual consistency: 404 for GET {Op}, will retry", operationId);
            }

            elapsed += interval;
            if (elapsed >= options.WaitUpToMs)
            {
                throw new EventualConsistencyTimeoutException(
                    $"Eventual consistency timeout after {elapsed}ms for {operationId}",
                    operationId,
                    elapsed);
            }

            await Task.Delay(interval, ct);
        }
    }
}
