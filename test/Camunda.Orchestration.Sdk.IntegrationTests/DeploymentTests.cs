using System.Text.Json;
using Camunda.Orchestration.Sdk.Api;
using FluentAssertions;

namespace Camunda.Orchestration.Sdk.IntegrationTests;

/// <summary>
/// Tests for deploying BPMN resources and verifying the deployment response.
/// </summary>
[Collection("Camunda")]
[Trait("Category", "Integration")]
public class DeploymentTests(CamundaFixture fixture)
{
    [Fact]
    public async Task CreateDeployment_DeploysBpmnAndReturnsProcessInfo()
    {
        var result = await fixture.DeployResourceAsync("test-process.bpmn");

        result.Should().NotBeNull();
        result.Deployments.Should().NotBeNullOrEmpty();

        // The deployment key should be present (serialized as a number or string)
        result.DeploymentKey.Should().NotBeNull();
    }
}
