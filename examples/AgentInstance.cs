// Compilable usage examples for agent instance operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class AgentInstanceExamples
{
    #region GetAgentInstance
    // <GetAgentInstance>
    public static async Task GetAgentInstanceExample(AgentInstanceKey agentInstanceKey)
    {
        using var client = CamundaClient.Create();

        var result = await client.GetAgentInstanceAsync(agentInstanceKey);
        Console.WriteLine($"Agent instance: {result.AgentInstanceKey}, status: {result.Status}");
    }
    // </GetAgentInstance>
    #endregion GetAgentInstance

    #region SearchAgentInstances

    // <SearchAgentInstances>
    public static async Task SearchAgentInstancesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchAgentInstancesAsync(new AgentInstanceSearchQuery());

        foreach (var instance in result.Items)
        {
            Console.WriteLine($"Agent instance: {instance.AgentInstanceKey}, status: {instance.Status}");
        }
    }
    // </SearchAgentInstances>
    #endregion SearchAgentInstances
}
