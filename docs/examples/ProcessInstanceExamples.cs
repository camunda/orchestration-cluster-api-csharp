// Compilable usage examples for process instance operations.
// Region tags are referenced by DocFX overwrite files.
#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CA1861 // Constant array arguments

using Camunda.Orchestration.Sdk;

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
    private static async Task CancelProcessInstanceExample(ProcessInstanceKey processInstanceKey)
    {
        using var client = CamundaClient.Create();

        await client.CancelProcessInstanceAsync(processInstanceKey, new CancelProcessInstanceRequest());
    }
    // </CancelProcessInstance>

    // <GetProcessInstance>
    private static async Task GetProcessInstanceExample(ProcessInstanceKey processInstanceKey)
    {
        using var client = CamundaClient.Create();

        var instance = await client.GetProcessInstanceAsync(processInstanceKey);

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
    private static async Task MigrateProcessInstanceExample(
        ProcessInstanceKey processInstanceKey,
        ProcessDefinitionKey targetProcessDefinitionKey,
        ElementId sourceElementId,
        ElementId targetElementId)
    {
        using var client = CamundaClient.Create();

        await client.MigrateProcessInstanceAsync(processInstanceKey, new ProcessInstanceMigrationInstruction
        {
            TargetProcessDefinitionKey = targetProcessDefinitionKey,
            MappingInstructions = new List<MigrateProcessInstanceMappingInstruction>
            {
                new()
                {
                    SourceElementId = sourceElementId,
                    TargetElementId = targetElementId,
                }
            }
        });
    }
    // </MigrateProcessInstance>

    // <ModifyProcessInstance>
    private static async Task ModifyProcessInstanceExample(ProcessInstanceKey processInstanceKey, ElementId elementId)
    {
        using var client = CamundaClient.Create();

        await client.ModifyProcessInstanceAsync(processInstanceKey, new ProcessInstanceModificationInstruction
        {
            ActivateInstructions = new List<ProcessInstanceModificationActivateInstruction>
            {
                new() { ElementId = elementId }
            }
        });
    }
    // </ModifyProcessInstance>
}
