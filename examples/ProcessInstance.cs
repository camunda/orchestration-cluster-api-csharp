// Compilable usage examples for process instance operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class ProcessInstanceExamples
{
    #region CreateProcessInstanceById
    public static async Task CreateProcessInstanceByIdExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstruction
        {
            ProcessDefinitionId = "my-process",
        });

        Console.WriteLine($"Process instance key: {result.ProcessInstanceKey}");
    }
    #endregion CreateProcessInstanceById

    #region CreateProcessInstanceByKey
    public static async Task CreateProcessInstanceByKeyExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstruction
        {
            ProcessDefinitionKey = "123456",
        });

        Console.WriteLine($"Process instance key: {result.ProcessInstanceKey}");
    }
    #endregion CreateProcessInstanceByKey

    #region GetProcessInstance
    public static async Task GetProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessInstanceAsync(new ProcessInstanceKey("123456"));
        Console.WriteLine($"Process instance: {result.ProcessDefinitionId}");
    }
    #endregion GetProcessInstance

    #region SearchProcessInstances
    public static async Task SearchProcessInstancesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchProcessInstancesAsync(new ProcessInstanceSearchQuery());

        foreach (var instance in result.Items)
        {
            Console.WriteLine($"Process instance: {instance.ProcessInstanceKey}");
        }
    }
    #endregion SearchProcessInstances

    #region CancelProcessInstance
    public static async Task CancelProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        await client.CancelProcessInstanceAsync(
            new ProcessInstanceKey("123456"),
            new CancelProcessInstanceRequest());
    }
    #endregion CancelProcessInstance

    #region DeleteProcessInstance
    public static async Task DeleteProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteProcessInstanceAsync(
            new ProcessInstanceKey("123456"),
            new DeleteProcessInstanceRequest());
    }
    #endregion DeleteProcessInstance

    #region MigrateProcessInstance
    public static async Task MigrateProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        await client.MigrateProcessInstanceAsync(
            new ProcessInstanceKey("123456"),
            new ProcessInstanceMigrationInstruction
            {
                TargetProcessDefinitionKey = "789012",
            });
    }
    #endregion MigrateProcessInstance

    #region ModifyProcessInstance
    public static async Task ModifyProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        await client.ModifyProcessInstanceAsync(
            new ProcessInstanceKey("123456"),
            new ProcessInstanceModificationInstruction());
    }
    #endregion ModifyProcessInstance

    #region GetProcessInstanceStatistics
    public static async Task GetProcessInstanceStatisticsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessInstanceStatisticsAsync(
            new ProcessInstanceKey("123456"));

        foreach (var stat in result.Items)
        {
            Console.WriteLine($"Element: {stat.ElementId}");
        }
    }
    #endregion GetProcessInstanceStatistics

    #region GetProcessInstanceSequenceFlows
    public static async Task GetProcessInstanceSequenceFlowsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessInstanceSequenceFlowsAsync(
            new ProcessInstanceKey("123456"));

        foreach (var flow in result.Items)
        {
            Console.WriteLine($"Sequence flow: {flow}");
        }
    }
    #endregion GetProcessInstanceSequenceFlows

    #region GetProcessInstanceCallHierarchy
    public static async Task GetProcessInstanceCallHierarchyExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessInstanceCallHierarchyAsync(
            new ProcessInstanceKey("123456"));

        Console.WriteLine($"Call hierarchy: {result}");
    }
    #endregion GetProcessInstanceCallHierarchy

    #region SearchProcessInstanceIncidents
    public static async Task SearchProcessInstanceIncidentsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchProcessInstanceIncidentsAsync(
            new ProcessInstanceKey("123456"),
            new IncidentSearchQuery());

        foreach (var incident in result.Items)
        {
            Console.WriteLine($"Incident: {incident.IncidentKey}");
        }
    }
    #endregion SearchProcessInstanceIncidents

    #region ResolveProcessInstanceIncidents
    public static async Task ResolveProcessInstanceIncidentsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.ResolveProcessInstanceIncidentsAsync(
            new ProcessInstanceKey("123456"));

        Console.WriteLine($"Batch operation key: {result.BatchOperationKey}");
    }
    #endregion ResolveProcessInstanceIncidents
}
