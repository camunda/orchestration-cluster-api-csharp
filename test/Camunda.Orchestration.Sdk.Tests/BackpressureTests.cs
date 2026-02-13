using Camunda.Orchestration.Sdk.Runtime;
using FluentAssertions;

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

        state.Severity.Should().Be("healthy");
        state.Consecutive.Should().Be(0);
        state.PermitsMax.Should().Be(16); // BALANCED default
    }

    [Fact]
    public void RecordBackpressureIncrementsConsecutive()
    {
        var bp = new BackpressureManager(new BackpressureConfig(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        bp.RecordBackpressure();
        bp.GetState().Consecutive.Should().Be(1);
        bp.GetState().Severity.Should().Be("soft");

        bp.RecordBackpressure();
        bp.GetState().Consecutive.Should().Be(2);
    }

    [Fact]
    public void SevereThresholdTriggersWhenExceeded()
    {
        var bp = new BackpressureManager(
            new BackpressureConfig { SevereThreshold = 2 },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        bp.RecordBackpressure();
        bp.RecordBackpressure();
        bp.GetState().Severity.Should().Be("severe");
    }

    [Fact]
    public void LegacyProfileIsObserveOnly()
    {
        var bp = new BackpressureManager(
            new BackpressureConfig { ObserveOnly = true, Enabled = false },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        bp.RecordBackpressure();
        bp.GetState().PermitsMax.Should().BeNull();
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
