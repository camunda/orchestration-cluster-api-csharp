using FluentAssertions;
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

        result.Should().Be("immediate");
        callCount.Should().Be(1);
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

        result.Should().Be(42);
        callCount.Should().Be(1);
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

        result.Should().Be(3);
        callCount.Should().Be(3);
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

        var ex = (await act.Should().ThrowAsync<EventualConsistencyTimeoutException>()).Which;
        ex.OperationId.Should().Be("slowOp");
        ex.WaitedMs.Should().BeGreaterThanOrEqualTo(50);
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

        result.Should().Be("found");
        callCount.Should().Be(3);
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

        await act.Should().ThrowAsync<HttpSdkException>()
            .Where(e => e.Status == 404);
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

        result.Should().Be("data");
        callCount.Should().Be(2);
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

        await act.Should().ThrowAsync<OperationCanceledException>();
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

        await act.Should().ThrowAsync<HttpSdkException>()
            .Where(e => e.Status == 500);
    }
}
