namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for backpressure management, mirroring the JS SDK's backpressure tests.
/// </summary>
public class BackpressureTests
{
    [Fact]
    public void InitialStateIsHealthy()
    {
        var bp = new BackpressureManager(new BackpressureConfig(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var state = bp.GetState();

        Assert.Equal("healthy", state.Severity);
        Assert.Equal(0, state.Consecutive);
        Assert.Equal(16, state.PermitsMax); // BALANCED default
    }

    [Fact]
    public void RecordBackpressureIncrementsConsecutive()
    {
        var bp = new BackpressureManager(new BackpressureConfig(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        bp.RecordBackpressure();
        Assert.Equal(1, bp.GetState().Consecutive);
        Assert.Equal("soft", bp.GetState().Severity);

        bp.RecordBackpressure();
        Assert.Equal(2, bp.GetState().Consecutive);
    }

    [Fact]
    public void SevereThresholdTriggersWhenExceeded()
    {
        var bp = new BackpressureManager(
            new BackpressureConfig { SevereThreshold = 2 },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        bp.RecordBackpressure();
        bp.RecordBackpressure();
        Assert.Equal("severe", bp.GetState().Severity);
    }

    [Fact]
    public void LegacyProfileIsObserveOnly()
    {
        var bp = new BackpressureManager(
            new BackpressureConfig { ObserveOnly = true, Enabled = false },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        bp.RecordBackpressure();
        Assert.Null(bp.GetState().PermitsMax);
    }

    [Fact]
    public async Task AcquireAndReleaseWorkWithSemaphore()
    {
        var bp = new BackpressureManager(
            new BackpressureConfig { InitialMax = 2 },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        await bp.AcquireAsync();
        await bp.AcquireAsync();
        bp.Release();
        bp.Release();
    }
}
