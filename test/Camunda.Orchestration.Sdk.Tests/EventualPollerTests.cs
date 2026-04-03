using Microsoft.Extensions.Logging.Abstractions;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for EventualPoller: predicate handling, timeout, 404 retry, skip behavior.
/// </summary>
public class EventualPollerTests
{
    [Fact]
    public async Task PollAsync_SkipsPollingWhenWaitUpToMsIsZero()
    {
        var callCount = 0;
        var result = await EventualPoller.PollAsync(
            "testOp",
            isGet: false,
            invoke: () =>
            {
                callCount++;
                return Task.FromResult("immediate");
            },
            new ConsistencyOptions<string> { WaitUpToMs = 0 },
            NullLogger.Instance);

        Assert.Equal("immediate", result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task PollAsync_ReturnsImmediatelyWhenPredicateSatisfied()
    {
        var callCount = 0;
        var result = await EventualPoller.PollAsync(
            "testOp",
            isGet: false,
            invoke: () =>
            {
                callCount++;
                return Task.FromResult(42);
            },
            new ConsistencyOptions<int>
            {
                WaitUpToMs = 5000,
                PollIntervalMs = 50,
                IsConsistent = v => v == 42,
            },
            NullLogger.Instance);

        Assert.Equal(42, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task PollAsync_RetriesUntilPredicateSatisfied()
    {
        var callCount = 0;
        var result = await EventualPoller.PollAsync(
            "testOp",
            isGet: false,
            invoke: () =>
            {
                callCount++;
                return Task.FromResult(callCount);
            },
            new ConsistencyOptions<int>
            {
                WaitUpToMs = 5000,
                PollIntervalMs = 10,
                IsConsistent = v => v >= 3,
            },
            NullLogger.Instance);

        Assert.Equal(3, result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task PollAsync_ThrowsTimeoutWhenPredicateNeverSatisfied()
    {
        var act = async () => await EventualPoller.PollAsync(
            "slowOp",
            isGet: false,
            invoke: () => Task.FromResult(0),
            new ConsistencyOptions<int>
            {
                WaitUpToMs = 50,
                PollIntervalMs = 10,
                IsConsistent = _ => false, // Never satisfied
            },
            NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<EventualConsistencyTimeoutException>(act);
        Assert.Equal("slowOp", ex.OperationId);
        Assert.True(ex.WaitedMs >= 50);
    }

    [Fact]
    public async Task PollAsync_Retries404ForGetOperations()
    {
        var callCount = 0;
        var result = await EventualPoller.PollAsync(
            "getOp",
            isGet: true,
            invoke: () =>
            {
                callCount++;
                if (callCount < 3)
                    throw new HttpSdkException("Not Found", 404, "getOp");
                return Task.FromResult("found");
            },
            new ConsistencyOptions<string>
            {
                WaitUpToMs = 5000,
                PollIntervalMs = 10,
            },
            NullLogger.Instance);

        Assert.Equal("found", result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task PollAsync_DoesNotRetry404ForNonGetOperations()
    {
        var act = async () => await EventualPoller.PollAsync(
            "searchOp",
            isGet: false,
            invoke: () =>
            {
                throw new HttpSdkException("Not Found", 404, "searchOp");
#pragma warning disable CS0162
                return Task.FromResult("never");
#pragma warning restore CS0162
            },
            new ConsistencyOptions<string>
            {
                WaitUpToMs = 5000,
                PollIntervalMs = 10,
            },
            NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<HttpSdkException>(act);

        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task PollAsync_WithoutPredicate_AcceptsAnyNonNullResult()
    {
        var callCount = 0;
        var result = await EventualPoller.PollAsync<string?>(
            "testOp",
            isGet: false,
            invoke: () =>
            {
                callCount++;
                return Task.FromResult<string?>(callCount >= 2 ? "data" : null);
            },
            new ConsistencyOptions<string?>
            {
                WaitUpToMs = 5000,
                PollIntervalMs = 10,
            },
            NullLogger.Instance);

        Assert.Equal("data", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task PollAsync_RespectsCanellationToken()
    {
        using var cts = new CancellationTokenSource(50);

        var act = async () => await EventualPoller.PollAsync(
            "cancelOp",
            isGet: false,
            invoke: () => Task.FromResult(0),
            new ConsistencyOptions<int>
            {
                WaitUpToMs = 30000,
                PollIntervalMs = 10,
                IsConsistent = _ => false,
            },
            NullLogger.Instance,
            cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    [Fact]
    public async Task PollAsync_PropagatesNon404Exceptions()
    {
        var act = async () => await EventualPoller.PollAsync(
            "errorOp",
            isGet: true,
            invoke: () =>
            {
                throw new HttpSdkException("Server Error", 500, "errorOp");
#pragma warning disable CS0162
                return Task.FromResult("never");
#pragma warning restore CS0162
            },
            new ConsistencyOptions<string>
            {
                WaitUpToMs = 5000,
                PollIntervalMs = 10,
            },
            NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<HttpSdkException>(act);

        Assert.Equal(500, ex.Status);
    }
}
