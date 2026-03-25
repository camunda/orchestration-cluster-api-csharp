// Compilable usage examples for process instance operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class ProcessInstanceExamples
{
    #region CreateProcessInstanceById
    // <CreateProcessInstanceById>
    public static async Task CreateProcessInstanceByIdExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = ProcessDefinitionId.AssumeExists("my-process"),
        });

        Console.WriteLine($"Process instance key: {result.ProcessInstanceKey}");
    }
    // </CreateProcessInstanceById>
    #endregion CreateProcessInstanceById

    #region CreateProcessInstanceByKey

    // <CreateProcessInstanceByKey>
    public static async Task CreateProcessInstanceByKeyExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionByKey
        {
            ProcessDefinitionKey = ProcessDefinitionKey.AssumeExists("123456"),
        });

        Console.WriteLine($"Process instance key: {result.ProcessInstanceKey}");
    }
    // </CreateProcessInstanceByKey>
    #endregion CreateProcessInstanceByKey

    #region GetProcessInstance

    // <GetProcessInstance>
    public static async Task GetProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessInstanceAsync(ProcessInstanceKey.AssumeExists("123456"));
        Console.WriteLine($"Process instance: {result.ProcessDefinitionId}");
    }
    // </GetProcessInstance>
    #endregion GetProcessInstance

    #region SearchProcessInstances

    // <SearchProcessInstances>
    public static async Task SearchProcessInstancesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchProcessInstancesAsync(new ProcessInstanceSearchQuery());

        foreach (var instance in result.Items)
        {
            Console.WriteLine($"Process instance: {instance.ProcessInstanceKey}");
        }
    }
    // </SearchProcessInstances>
    #endregion SearchProcessInstances

    #region CancelProcessInstance

    // <CancelProcessInstance>
    public static async Task CancelProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        await client.CancelProcessInstanceAsync(
            ProcessInstanceKey.AssumeExists("123456"),
            new CancelProcessInstanceRequest());
    }
    // </CancelProcessInstance>
    #endregion CancelProcessInstance

    #region DeleteProcessInstance

    // <DeleteProcessInstance>
    public static async Task DeleteProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteProcessInstanceAsync(
            ProcessInstanceKey.AssumeExists("123456"),
            new DeleteProcessInstanceRequest());
    }
    // </DeleteProcessInstance>
    #endregion DeleteProcessInstance

    #region MigrateProcessInstance

    // <MigrateProcessInstance>
    public static async Task MigrateProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        await client.MigrateProcessInstanceAsync(
            ProcessInstanceKey.AssumeExists("123456"),
            new ProcessInstanceMigrationInstruction
            {
                TargetProcessDefinitionKey = ProcessDefinitionKey.AssumeExists("789012"),
            });
    }
    // </MigrateProcessInstance>
    #endregion MigrateProcessInstance

    #region ModifyProcessInstance

    // <ModifyProcessInstance>
    public static async Task ModifyProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        await client.ModifyProcessInstanceAsync(
            ProcessInstanceKey.AssumeExists("123456"),
            new ProcessInstanceModificationInstruction());
    }
    // </ModifyProcessInstance>
    #endregion ModifyProcessInstance

    #region GetProcessInstanceStatistics

    // <GetProcessInstanceStatistics>
    public static async Task GetProcessInstanceStatisticsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessInstanceStatisticsAsync(
            ProcessInstanceKey.AssumeExists("123456"));

        foreach (var stat in result.Items)
        {
            Console.WriteLine($"Element: {stat.ElementId}");
        }
    }
    // </GetProcessInstanceStatistics>
    #endregion GetProcessInstanceStatistics

    #region GetProcessInstanceSequenceFlows

    // <GetProcessInstanceSequenceFlows>
    public static async Task GetProcessInstanceSequenceFlowsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessInstanceSequenceFlowsAsync(
            ProcessInstanceKey.AssumeExists("123456"));

        foreach (var flow in result.Items)
        {
            Console.WriteLine($"Sequence flow: {flow}");
        }
    }
    // </GetProcessInstanceSequenceFlows>
    #endregion GetProcessInstanceSequenceFlows

    #region GetProcessInstanceCallHierarchy

    // <GetProcessInstanceCallHierarchy>
    public static async Task GetProcessInstanceCallHierarchyExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessInstanceCallHierarchyAsync(
            ProcessInstanceKey.AssumeExists("123456"));

        Console.WriteLine($"Call hierarchy: {result}");
    }
    // </GetProcessInstanceCallHierarchy>
    #endregion GetProcessInstanceCallHierarchy

    #region SearchProcessInstanceIncidents

    // <SearchProcessInstanceIncidents>
    public static async Task SearchProcessInstanceIncidentsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchProcessInstanceIncidentsAsync(
            ProcessInstanceKey.AssumeExists("123456"),
            new IncidentSearchQuery());

        foreach (var incident in result.Items)
        {
            Console.WriteLine($"Incident: {incident.IncidentKey}");
        }
    }
    // </SearchProcessInstanceIncidents>
    #endregion SearchProcessInstanceIncidents

    #region ResolveProcessInstanceIncidents

    // <ResolveProcessInstanceIncidents>
    public static async Task ResolveProcessInstanceIncidentsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.ResolveProcessInstanceIncidentsAsync(
            ProcessInstanceKey.AssumeExists("123456"));

        Console.WriteLine($"Batch operation key: {result.BatchOperationKey}");
    }
    // </ResolveProcessInstanceIncidents>
    #endregion ResolveProcessInstanceIncidents
}
