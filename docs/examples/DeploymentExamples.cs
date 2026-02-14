// Compilable usage examples for deployment operations.
// Region tags are referenced by DocFX overwrite files.
#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CA1861 // Constant array arguments

using Camunda.Orchestration.Sdk;
using Camunda.Orchestration.Sdk.Api;

namespace Camunda.Orchestration.Sdk.Examples;

internal static class DeploymentExamples
{
    // <DeployResourcesFromFiles>
    static async Task DeployResourcesFromFilesExample()
    {
        using var client = Camunda.CreateClient();

        var result = await client.DeployResourcesFromFilesAsync(
            new[] { "order-process.bpmn", "email-connector.bpmn" }
        );

        Console.WriteLine($"Deployment key: {result.DeploymentKey}");
        foreach (var process in result.Processes)
        {
            Console.WriteLine($"  Process: {process.ProcessDefinitionId} v{process.ProcessDefinitionVersion}");
        }
    }
    // </DeployResourcesFromFiles>

    // <DeleteResource>
    static async Task DeleteResourceExample()
    {
        using var client = Camunda.CreateClient();

        // Deploy a resource and get its key from the deployment response
        var deployment = await client.DeployResourcesFromFilesAsync(
            new[] { "order-process.bpmn" }
        );
        // ProcessDefinitionKey doubles as the resource key for deletion
        var resourceKey = ResourceKey.AssumeExists(deployment.Processes[0].ProcessDefinitionKey.Value);

        await client.DeleteResourceAsync(resourceKey, new DeleteResourceRequest());
    }
    // </DeleteResource>
}
