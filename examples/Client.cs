// Compilable usage examples for client construction and topology.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class ClientExamples
{
    #region CreateClient
    public static async Task CreateClientExample()
    {
        using var client = CamundaClient.Create();

        var topology = await client.GetTopologyAsync();
        Console.WriteLine($"Cluster size: {topology.ClusterSize}");
    }
    #endregion CreateClient

    #region GetTopology
    public static async Task GetTopologyExample()
    {
        using var client = CamundaClient.Create();

        var topology = await client.GetTopologyAsync();
        Console.WriteLine($"Cluster size: {topology.ClusterSize}");
    }
    #endregion GetTopology
}
