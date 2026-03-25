// Compilable usage examples for batch operation management.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class BatchOperationExamples
{
    #region GetBatchOperation
    public static async Task GetBatchOperationExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetBatchOperationAsync(
            new BatchOperationKey("123456"));

        Console.WriteLine($"Batch operation: {result.BatchOperationKey}");
    }
    #endregion GetBatchOperation

    #region SearchBatchOperations
    public static async Task SearchBatchOperationsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchBatchOperationsAsync(
            new BatchOperationSearchQuery());

        foreach (var op in result.Items)
        {
            Console.WriteLine($"Batch operation: {op.BatchOperationKey}");
        }
    }
    #endregion SearchBatchOperations

    #region SearchBatchOperationItems
    public static async Task SearchBatchOperationItemsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchBatchOperationItemsAsync(
            new BatchOperationItemSearchQuery());

        foreach (var item in result.Items)
        {
            Console.WriteLine($"Item: {item.ItemKey}");
        }
    }
    #endregion SearchBatchOperationItems

    #region CancelBatchOperation
    public static async Task CancelBatchOperationExample()
    {
        using var client = CamundaClient.Create();

        await client.CancelBatchOperationAsync(new BatchOperationKey("123456"));
    }
    #endregion CancelBatchOperation

    #region SuspendBatchOperation
    public static async Task SuspendBatchOperationExample()
    {
        using var client = CamundaClient.Create();

        await client.SuspendBatchOperationAsync(new BatchOperationKey("123456"));
    }
    #endregion SuspendBatchOperation

    #region ResumeBatchOperation
    public static async Task ResumeBatchOperationExample()
    {
        using var client = CamundaClient.Create();

        await client.ResumeBatchOperationAsync(new BatchOperationKey("123456"));
    }
    #endregion ResumeBatchOperation

    #region CancelProcessInstancesBatchOperation
    public static async Task CancelProcessInstancesBatchOperationExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CancelProcessInstancesBatchOperationAsync(
            new ProcessInstanceCancellationBatchOperationRequest());

        Console.WriteLine($"Batch operation key: {result.BatchOperationKey}");
    }
    #endregion CancelProcessInstancesBatchOperation

    #region DeleteProcessInstancesBatchOperation
    public static async Task DeleteProcessInstancesBatchOperationExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.DeleteProcessInstancesBatchOperationAsync(
            new ProcessInstanceDeletionBatchOperationRequest());

        Console.WriteLine($"Batch operation key: {result.BatchOperationKey}");
    }
    #endregion DeleteProcessInstancesBatchOperation

    #region MigrateProcessInstancesBatchOperation
    public static async Task MigrateProcessInstancesBatchOperationExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.MigrateProcessInstancesBatchOperationAsync(
            new ProcessInstanceMigrationBatchOperationRequest
            {
                TargetProcessDefinitionKey = "456789",
            });

        Console.WriteLine($"Batch operation key: {result.BatchOperationKey}");
    }
    #endregion MigrateProcessInstancesBatchOperation

    #region ModifyProcessInstancesBatchOperation
    public static async Task ModifyProcessInstancesBatchOperationExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.ModifyProcessInstancesBatchOperationAsync(
            new ProcessInstanceModificationBatchOperationRequest());

        Console.WriteLine($"Batch operation key: {result.BatchOperationKey}");
    }
    #endregion ModifyProcessInstancesBatchOperation

    #region ResolveIncidentsBatchOperation
    public static async Task ResolveIncidentsBatchOperationExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.ResolveIncidentsBatchOperationAsync(
            new ProcessInstanceIncidentResolutionBatchOperationRequest());

        Console.WriteLine($"Batch operation key: {result.BatchOperationKey}");
    }
    #endregion ResolveIncidentsBatchOperation

    #region DeleteDecisionInstancesBatchOperation
    public static async Task DeleteDecisionInstancesBatchOperationExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.DeleteDecisionInstancesBatchOperationAsync(
            new DecisionInstanceDeletionBatchOperationRequest());

        Console.WriteLine($"Batch operation key: {result.BatchOperationKey}");
    }
    #endregion DeleteDecisionInstancesBatchOperation
}
