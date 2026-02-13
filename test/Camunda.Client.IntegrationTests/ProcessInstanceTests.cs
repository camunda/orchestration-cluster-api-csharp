using Camunda.Client.Api;
using FluentAssertions;

namespace Camunda.Client.IntegrationTests;

/// <summary>
/// Tests for the process instance lifecycle:
/// deploy → create instance → search → cancel.
/// </summary>
[Collection("Camunda")]
[Trait("Category", "Integration")]
public class ProcessInstanceTests(CamundaFixture fixture)
{
    [Fact]
    public async Task CanCreateAndFindProcessInstance()
    {
        // Deploy the test process
        await fixture.DeployResourceAsync("test-process.bpmn");

        // Create a process instance using the typed SDK API
        var createResult = await fixture.CreateProcessInstanceAsync(
            "integration-test-process",
            new Dictionary<string, object> { ["testVar"] = "hello" });

        var processInstanceKey = createResult.ProcessInstanceKey;
        processInstanceKey.ToString().Should().NotBeNullOrEmpty();

        // Search for the created instance (with retry for eventual consistency)
        SearchProcessInstancesResponse? searchResult = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            searchResult = await fixture.Client.SearchProcessInstancesAsync(
                new SearchProcessInstancesRequest());

            if (searchResult?.Items != null && HasItemWithKey(searchResult.Items, processInstanceKey.ToString()))
                break;

            await Task.Delay(500);
        }

        searchResult.Should().NotBeNull();
        HasItemWithKey(searchResult!.Items, processInstanceKey.ToString()).Should().BeTrue();

        // Clean up: cancel the process instance
        await fixture.CancelProcessInstanceAsync(processInstanceKey);
    }

    [Fact]
    public async Task CreateAndCancelProcessInstance()
    {
        await fixture.DeployResourceAsync("test-process.bpmn");

        var createResult = await fixture.CreateProcessInstanceAsync("integration-test-process");
        var processInstanceKey = createResult.ProcessInstanceKey;

        // Cancel
        await fixture.CancelProcessInstanceAsync(processInstanceKey);

        // Verify it's no longer ACTIVE (search with retry)
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        bool stillActive = true;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var search = await fixture.Client.SearchProcessInstancesAsync(
                new SearchProcessInstancesRequest());

            stillActive = search?.Items != null &&
                HasItemWithKeyAndState(search.Items, processInstanceKey.ToString(), "ACTIVE");

            if (!stillActive)
                break;
            await Task.Delay(500);
        }

        stillActive.Should().BeFalse("process instance should no longer be ACTIVE after cancel");
    }

    private static bool HasItemWithKey(List<ProcessInstanceResult> items, string processInstanceKey) =>
        items.Any(i => i.ProcessInstanceKey.ToString() == processInstanceKey);

    private static bool HasItemWithKeyAndState(List<ProcessInstanceResult> items, string processInstanceKey, string state) =>
        items.Any(i => i.ProcessInstanceKey.ToString() == processInstanceKey
            && i.State.ToString() == state);
}
