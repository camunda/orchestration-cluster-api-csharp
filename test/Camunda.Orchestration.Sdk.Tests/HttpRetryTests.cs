using System.Net;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for HTTP retry logic, mirroring the JS SDK's http-retry.test.ts.
/// </summary>
public class HttpRetryTests
{
    [Fact]
    public async Task RetriesOnTransientFailureThenSucceeds()
    {
        var attempt = 0;
        var config = new HttpRetryConfig { MaxAttempts = 3, BaseDelayMs = 10, MaxDelayMs = 50 };

        var result = await HttpRetryExecutor.ExecuteWithRetryAsync(
            () =>
            {
                attempt++;
                if (attempt < 3)
                    throw new HttpRequestException("transient", null, HttpStatusCode.TooManyRequests);
                return Task.FromResult(42);
            },
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        Assert.Equal(42, result);
        Assert.Equal(3, attempt);
    }

    [Fact]
    public async Task ThrowsAfterMaxRetries()
    {
        var config = new HttpRetryConfig { MaxAttempts = 2, BaseDelayMs = 10, MaxDelayMs = 50 };

        var act = async () => await HttpRetryExecutor.ExecuteWithRetryAsync(
            () =>
            {
                throw new HttpRequestException("always fail", null, HttpStatusCode.TooManyRequests);
#pragma warning disable CS0162
                return Task.FromResult(0);
#pragma warning restore CS0162
            },
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(act);
    }

    [Fact]
    public async Task DoesNotRetryNonRetryableErrors()
    {
        var attempt = 0;
        var config = new HttpRetryConfig { MaxAttempts = 3, BaseDelayMs = 10, MaxDelayMs = 50 };

        var act = async () => await HttpRetryExecutor.ExecuteWithRetryAsync(
            () =>
            {
                attempt++;
                throw new HttpRequestException("not found", null, HttpStatusCode.NotFound);
#pragma warning disable CS0162
                return Task.FromResult(0);
#pragma warning restore CS0162
            },
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(act);
        Assert.Equal(1, attempt); // No retry for 404
    }

    [Fact]
    public void DefaultClassifierMarks429AsRetryable()
    {
        var ex = new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests);
        var decision = HttpRetryExecutor.DefaultClassify(ex);

        Assert.True(decision.Retryable);
        Assert.Contains("429", decision.Reason);
    }

    [Fact]
    public void DefaultClassifierMarks404AsNonRetryable()
    {
        var ex = new HttpRequestException("not found", null, HttpStatusCode.NotFound);
        var decision = HttpRetryExecutor.DefaultClassify(ex);

        Assert.False(decision.Retryable);
    }
}
