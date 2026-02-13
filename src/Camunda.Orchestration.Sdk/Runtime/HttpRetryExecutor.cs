using System.Net;
using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk.Runtime;

/// <summary>
/// HTTP retry with exponential backoff and jitter. Mirrors the JS SDK's executeWithHttpRetry.
/// </summary>
internal static class HttpRetryExecutor
{
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        HttpRetryConfig config,
        ILogger logger,
        Func<Exception, RetryDecision>? classifier = null,
        CancellationToken ct = default)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < config.MaxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex)
            {
                lastError = ex;
                var decision = classifier?.Invoke(ex) ?? DefaultClassify(ex);

                if (!decision.Retryable || attempt + 1 >= config.MaxAttempts)
                {
                    logger.LogDebug("Non-retryable or max retries exhausted: {Reason}", decision.Reason);
                    throw;
                }

                var delay = Math.Min(
                    config.BaseDelayMs * (int)Math.Pow(2, attempt),
                    config.MaxDelayMs);
                var jitter = (int)(delay * 0.2 * (Random.Shared.NextDouble() - 0.5));
                var sleepMs = delay + jitter;

                logger.LogDebug("Retry attempt {Attempt}/{Max} after {Delay}ms: {Reason}",
                    attempt + 1, config.MaxAttempts, sleepMs, decision.Reason);

                await Task.Delay(sleepMs, ct);
            }
        }

        throw lastError ?? new InvalidOperationException("Unexpected retry exhaustion");
    }

    public static RetryDecision DefaultClassify(Exception ex)
    {
        if (ex is HttpRequestException httpEx)
        {
            var status = httpEx.StatusCode;
            if (status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                return new RetryDecision(true, $"http-{(int)status}");
            if (status == HttpStatusCode.InternalServerError)
                return new RetryDecision(true, "http-500");
        }

        if (ex is TaskCanceledException or OperationCanceledException)
            return new RetryDecision(true, "timeout");

        return new RetryDecision(false, ex.GetType().Name);
    }
}

public readonly record struct RetryDecision(bool Retryable, string Reason);
