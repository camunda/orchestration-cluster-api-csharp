// Compilable usage examples for client construction and configuration.
// Region tags are referenced by DocFX overwrite files.
#pragma warning disable CS8321 // Local function is declared but never used

using Camunda.Orchestration.Sdk;
using Camunda.Orchestration.Sdk.Api;
using Camunda.Orchestration.Sdk.Runtime;

namespace Camunda.Orchestration.Sdk.Examples;

internal static class ClientExamples
{
    // <CreateClient>
    static void CreateClientExample()
    {
        // Uses environment variables for configuration (ZEEBE_ADDRESS, etc.)
        using var client = Camunda.CreateClient();
    }
    // </CreateClient>

    // <CreateClientWithOptions>
    static void CreateClientWithOptionsExample()
    {
        // Override config via environment dictionary
        using var client = Camunda.CreateClient(new CamundaOptions
        {
            Env = new Dictionary<string, string?>
            {
                ["ZEEBE_ADDRESS"] = "http://localhost:26500",
                ["CAMUNDA_AUTH_STRATEGY"] = "BASIC",
                ["CAMUNDA_BASIC_AUTH_USERNAME"] = "demo",
                ["CAMUNDA_BASIC_AUTH_PASSWORD"] = "demo"
            }
        });
    }
    // </CreateClientWithOptions>

    // <GetTopology>
    static async Task GetTopologyExample()
    {
        using var client = Camunda.CreateClient();

        var topology = await client.GetTopologyAsync();

        Console.WriteLine($"Cluster size: {topology.ClusterSize}");
        Console.WriteLine($"Partitions: {topology.PartitionsCount}");
        foreach (var broker in topology.Brokers)
        {
            Console.WriteLine($"  Broker {broker.NodeId}: {broker.Host}:{broker.Port}");
        }
    }
    // </GetTopology>

    // <GetAuthentication>
    static async Task GetAuthenticationExample()
    {
        using var client = Camunda.CreateClient();

        var user = await client.GetAuthenticationAsync();
        Console.WriteLine($"Authenticated as: {user.Username}");
    }
    // </GetAuthentication>
}
