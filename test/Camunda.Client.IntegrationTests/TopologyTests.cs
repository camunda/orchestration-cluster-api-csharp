using System.Text.Json;
using FluentAssertions;

namespace Camunda.Client.IntegrationTests;

/// <summary>
/// Smoke test: verify connection to the Camunda engine.
/// </summary>
[Collection("Camunda")]
[Trait("Category", "Integration")]
public class TopologyTests(CamundaFixture fixture)
{
    [Fact]
    public async Task GetTopology_ReturnsClusterInfo()
    {
        var topology = await fixture.Client.GetTopologyAsync();

        topology.Should().NotBeNull();
        topology.ClusterSize.Should().BeGreaterThan(0);
        topology.Brokers.Should().NotBeNullOrEmpty();
    }
}
