// Compilable usage examples for process instance operations.
// Region tags are referenced by DocFX overwrite files.
#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CA1861 // Constant array arguments

using Camunda.Orchestration.Sdk;
using Camunda.Orchestration.Sdk.Api;

namespace Camunda.Orchestration.Sdk.Examples;

internal static class ProcessInstanceExamples
{
    // <CreateProcessInstance>
    private static async Task CreateProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        // Deploy a process and retrieve its ProcessDefinitionId
        var deployment = await client.DeployResourcesFromFilesAsync(
            new[] { "order-process.bpmn" }
        );
        var processDefinitionId = deployment.Processes[0].ProcessDefinitionId;

        // Start a new instance using the deployed process definition
        var result = await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = processDefinitionId,
        });

        Console.WriteLine($"Started process instance: {result.ProcessInstanceKey}");
    }
    // </CreateProcessInstance>

    // <CreateProcessInstanceWithVariables>
    private static async Task CreateProcessInstanceWithVariablesExample()
    {
        using var client = CamundaClient.Create();

        // Deploy and get the ProcessDefinitionId from the deployment response
        var deployment = await client.DeployResourcesFromFilesAsync(
            new[] { "order-process.bpmn" }
        );
        var processDefinitionId = deployment.Processes[0].ProcessDefinitionId;

        var result = await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = processDefinitionId,
            Variables = new Dictionary<string, object>
            {
                ["orderId"] = "ORD-12345",
                ["amount"] = 99.95,
                ["priority"] = "high"
            }
        });

        Console.WriteLine($"Started: {result.ProcessInstanceKey}");
    }
    // </CreateProcessInstanceWithVariables>

    // <CancelProcessInstance>
    private static async Task CancelProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        // Create a process instance and get its key from the response
        var created = await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = ProcessDefinitionId.AssumeExists("order-process"),
        });

        // Cancel the process instance using the key from the creation response
        await client.CancelProcessInstanceAsync(created.ProcessInstanceKey, new CancelProcessInstanceRequest());
    }
    // </CancelProcessInstance>

    // <GetProcessInstance>
    private static async Task GetProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        // The ProcessInstanceKey is returned from CreateProcessInstanceAsync
        var created = await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = ProcessDefinitionId.AssumeExists("order-process"),
        });

        var instance = await client.GetProcessInstanceAsync(created.ProcessInstanceKey);

        Console.WriteLine($"State: {instance.State}");
    }
    // </GetProcessInstance>

    // <SearchProcessInstances>
    private static async Task SearchProcessInstancesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchProcessInstancesAsync(new ProcessInstanceSearchQuery());

        foreach (var instance in result.Items)
        {
            Console.WriteLine($"{instance.ProcessInstanceKey} — {instance.State}");
        }
    }
    // </SearchProcessInstances>

    // <MigrateProcessInstance>
    private static async Task MigrateProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        // Create an instance to migrate
        var created = await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = ProcessDefinitionId.AssumeExists("order-process"),
        });

        // Deploy the updated process version and get its ProcessDefinitionKey
        var v2 = await client.DeployResourcesFromFilesAsync(
            new[] { "order-process-v2.bpmn" }
        );
        var targetProcessDefinitionKey = v2.Processes[0].ProcessDefinitionKey;

        await client.MigrateProcessInstanceAsync(created.ProcessInstanceKey, new ProcessInstanceMigrationInstruction
        {
            TargetProcessDefinitionKey = targetProcessDefinitionKey,
            MappingInstructions = new List<MigrateProcessInstanceMappingInstruction>
            {
                new()
                {
                    SourceElementId = ElementId.AssumeExists("taskA"),
                    TargetElementId = ElementId.AssumeExists("taskB"),
                }
            }
        });
    }
    // </MigrateProcessInstance>

    // <ModifyProcessInstance>
    private static async Task ModifyProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        // Get a ProcessInstanceKey from the creation response
        var created = await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = ProcessDefinitionId.AssumeExists("order-process"),
        });

        await client.ModifyProcessInstanceAsync(created.ProcessInstanceKey, new ProcessInstanceModificationInstruction
        {
            ActivateInstructions = new List<ProcessInstanceModificationActivateInstruction>
            {
                new() { ElementId = ElementId.AssumeExists("taskB") }
            }
        });
    }
    // </ModifyProcessInstance>
}
