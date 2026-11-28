using System.Net;
using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk;

/// <summary>
/// HTTP retry with exponential backoff and jitter. Mirrors the JS SDK's executeWithHttpRetry.
/// </summary>
internal static class HttpRetryExecutor
{
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        HttpRetryConfig config,
        ILogger logger,
        TimeProvider timeProvider,
        IRandomSource randomSource,
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
                // A caller-initiated cancellation is not a transient failure: never
                // classify or retry it, or a cancelled operation would be re-run (and a
                // still-blocked attempt would hang forever). Rethrow it immediately.
                if (ct.IsCancellationRequested)
                    throw;

                lastError = ex;
                var decision = classifier?.Invoke(ex) ?? DefaultClassify(ex);

                if (!decision.Retryable || attempt + 1 >= config.MaxAttempts)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                        logger.LogDebug("Non-retryable or max retries exhausted: {Reason}", decision.Reason);
                    throw;
                }

                var delay = Math.Min(
                    config.BaseDelayMs * (int)Math.Pow(2, attempt),
                    config.MaxDelayMs);
                var jitter = (int)(delay * 0.2 * (randomSource.NextDouble() - 0.5));
                var sleepMs = delay + jitter;

                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Retry attempt {Attempt}/{Max} after {Delay}ms: {Reason}",
                        attempt + 1, config.MaxAttempts, sleepMs, decision.Reason);

                await Task.Delay(TimeSpan.FromMilliseconds(sleepMs), timeProvider, ct);
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

        // Non-2xx responses surface as HttpSdkException (see CamundaClient.BuildHttpException),
        // not HttpRequestException — without this branch they were never retried (#479).
        if (ex is HttpSdkException sdkEx)
        {
            if (sdkEx.Status is 429 or 503 or 500)
                return new RetryDecision(true, $"http-{sdkEx.Status}");
        }

        if (ex is TaskCanceledException or OperationCanceledException)
            return new RetryDecision(true, "timeout");

        return new RetryDecision(false, ex.GetType().Name);
    }

    /// <summary>
    /// Operation ids of the job commands. They are not idempotent: a retry of a command that
    /// was applied but whose response was lost gets 404. They are therefore retried only when
    /// the server says the command was not applied (#579).
    /// </summary>
    internal static readonly IReadOnlySet<string> JobCommandOperationIds =
        new HashSet<string>(StringComparer.Ordinal) { "completeJob", "failJob", "throwJobError" };

    /// <summary>
    /// Retry policy for job commands: retry only on 429, 503, or a connection failure before
    /// the request was sent (connect refused, DNS, TLS handshake). Never on 500/502/504,
    /// a client timeout, or a connection reset after sending: those leave the outcome unknown.
    /// </summary>
    public static RetryDecision ClassifyJobCommand(Exception ex)
    {
        if (IsNotApplied(ex))
        {
            var status = ex switch
            {
                HttpSdkException s => s.Status,
                HttpRequestException h => (int?)h.StatusCode,
                _ => null,
            };
            return new RetryDecision(true, status is { } code ? $"http-{code}" : "connect-failed");
        }
        return new RetryDecision(false, ex is HttpSdkException se ? $"http-{se.Status}" : ex.GetType().Name);
    }

    /// <summary>
    /// Whether the failure proves the command was not applied: HTTP 429/503, or a connection
    /// failure before the request was sent.
    /// </summary>
    internal static bool IsNotApplied(Exception ex) => ex switch
    {
        HttpSdkException s => s.Status is 429 or 503,
        HttpRequestException { StatusCode: { } code } => code is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable,
        HttpRequestException h => h.HttpRequestError is HttpRequestError.ConnectionError
            or HttpRequestError.NameResolutionError
            or HttpRequestError.SecureConnectionError,
        _ => false,
    };

    /// <summary>
    /// Whether a failed job command has an unknown outcome (it may have been applied):
    /// a 5xx other than 503, a client timeout, or a transport failure after the request was sent.
    /// </summary>
    internal static bool IsOutcomeUnknown(Exception ex) => ex switch
    {
        HttpSdkException s => s.Status is >= 500 and not 503,
        HttpRequestException { StatusCode: { } code } => (int)code >= 500 && code != HttpStatusCode.ServiceUnavailable,
        HttpRequestException => !IsNotApplied(ex),
        OperationCanceledException => true,
        _ => false,
    };
}

public readonly record struct RetryDecision(bool Retryable, string Reason);
