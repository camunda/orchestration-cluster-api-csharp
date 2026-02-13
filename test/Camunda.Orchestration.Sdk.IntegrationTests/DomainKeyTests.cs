using System.Text.Json;
using Camunda.Orchestration.Sdk.Api;
using FluentAssertions;

namespace Camunda.Orchestration.Sdk.IntegrationTests;

/// <summary>
/// Tests for strongly-typed domain key nominal types:
/// verifies that keys round-trip through API calls correctly.
/// </summary>
[Collection("Camunda")]
[Trait("Category", "Integration")]
public class DomainKeyTests(CamundaFixture fixture)
{
    [Fact]
    public void AssumeExists_CreatesValidKey()
    {
        var key = ProcessDefinitionKey.AssumeExists("2251799813686749");

        key.Value.Should().Be("2251799813686749");
        key.ToString().Should().Be("2251799813686749");
    }

    [Fact]
    public void AssumeExists_ValidatesConstraints()
    {
        // ProcessDefinitionKey has pattern ^-?[0-9]+$, minLength 1, maxLength 25
        var act = () => ProcessDefinitionKey.AssumeExists("not-a-number");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsValid_ChecksConstraints()
    {
        ProcessDefinitionKey.IsValid("12345").Should().BeTrue();
        ProcessDefinitionKey.IsValid("not-a-number").Should().BeFalse();
        ProcessDefinitionKey.IsValid("").Should().BeFalse();
    }

    [Fact]
    public void DifferentKeyTypes_AreNotInterchangeable()
    {
        // This is a compile-time guarantee — this test documents it.
        // The following would not compile:
        //   ProcessDefinitionKey key = UserTaskKey.AssumeExists("123");

        var pdk = ProcessDefinitionKey.AssumeExists("123");
        var utk = UserTaskKey.AssumeExists("456");

        // They are different types
        pdk.GetType().Should().NotBe(utk.GetType());
    }

    [Fact]
    public void DomainKey_SerializesAsPlainJsonString()
    {
        var key = ProcessDefinitionKey.AssumeExists("2251799813686749");
        var json = JsonSerializer.Serialize(key, fixture.JsonOptions);

        json.Should().Be("\"2251799813686749\"");
    }

    [Fact]
    public void DomainKey_DeserializesFromPlainJsonString()
    {
        var json = "\"2251799813686749\"";
        var key = JsonSerializer.Deserialize<ProcessDefinitionKey>(json, fixture.JsonOptions);

        key.Value.Should().Be("2251799813686749");
    }

    [Fact]
    public async Task DomainKeys_RoundTripThroughApiCalls()
    {
        // Deploy to get real keys
        var deployment = await fixture.DeployResourceAsync("test-process.bpmn");

        deployment.Should().NotBeNull();
        deployment.DeploymentKey.Should().NotBeNull();

        // Create & search to verify keys deserialize correctly
        var createResult = await fixture.CreateProcessInstanceAsync("integration-test-process");
        var processInstanceKey = createResult.ProcessInstanceKey;

        try
        {
            // Search — the response should contain typed ProcessInstanceKey values
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            SearchProcessInstancesResponse? searchResult = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                searchResult = await fixture.Client.SearchProcessInstancesAsync(
                    new SearchProcessInstancesRequest());

                if (searchResult?.Items?.Count > 0)
                    break;
                await Task.Delay(500);
            }

            searchResult.Should().NotBeNull();
            searchResult!.Items.Should().NotBeNullOrEmpty();
        }
        finally
        {
            await fixture.CancelProcessInstanceAsync(processInstanceKey);
        }
    }
}
