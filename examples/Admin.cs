// Compilable usage examples for admin, system, and statistics operations.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class AdminExamples
{
    #region GetGlobalClusterVariable
    public static async Task GetGlobalClusterVariableExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetGlobalClusterVariableAsync("my-variable");
        Console.WriteLine($"Variable: {result.Name} = {result.Value}");
    }
    #endregion GetGlobalClusterVariable

    #region CreateGlobalClusterVariable
    public static async Task CreateGlobalClusterVariableExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateGlobalClusterVariableAsync(
            new CreateClusterVariableRequest
            {
                Name = "my-variable",
                Value = "my-value",
            });

        Console.WriteLine($"Created variable: {result.Name}");
    }
    #endregion CreateGlobalClusterVariable

    #region UpdateGlobalClusterVariable
    public static async Task UpdateGlobalClusterVariableExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.UpdateGlobalClusterVariableAsync(
            "my-variable",
            new UpdateClusterVariableRequest
            {
                Value = "updated-value",
            });

        Console.WriteLine($"Updated variable: {result.Name}");
    }
    #endregion UpdateGlobalClusterVariable

    #region DeleteGlobalClusterVariable
    public static async Task DeleteGlobalClusterVariableExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteGlobalClusterVariableAsync("my-variable");
    }
    #endregion DeleteGlobalClusterVariable

    #region GetTenantClusterVariable
    public static async Task GetTenantClusterVariableExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetTenantClusterVariableAsync(
            new TenantId("acme-corp"),
            "my-variable");

        Console.WriteLine($"Variable: {result.Name} = {result.Value}");
    }
    #endregion GetTenantClusterVariable

    #region CreateTenantClusterVariable
    public static async Task CreateTenantClusterVariableExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateTenantClusterVariableAsync(
            new TenantId("acme-corp"),
            new CreateClusterVariableRequest
            {
                Name = "my-variable",
                Value = "tenant-value",
            });

        Console.WriteLine($"Created variable: {result.Name}");
    }
    #endregion CreateTenantClusterVariable

    #region UpdateTenantClusterVariable
    public static async Task UpdateTenantClusterVariableExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.UpdateTenantClusterVariableAsync(
            new TenantId("acme-corp"),
            "my-variable",
            new UpdateClusterVariableRequest
            {
                Value = "updated-tenant-value",
            });

        Console.WriteLine($"Updated variable: {result.Name}");
    }
    #endregion UpdateTenantClusterVariable

    #region DeleteTenantClusterVariable
    public static async Task DeleteTenantClusterVariableExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteTenantClusterVariableAsync(
            new TenantId("acme-corp"),
            "my-variable");
    }
    #endregion DeleteTenantClusterVariable

    #region SearchClusterVariables
    public static async Task SearchClusterVariablesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchClusterVariablesAsync(
            new ClusterVariableSearchQueryRequest());

        foreach (var variable in result.Items)
        {
            Console.WriteLine($"Variable: {variable.Name}");
        }
    }
    #endregion SearchClusterVariables

    #region CreateGlobalTaskListener
    public static async Task CreateGlobalTaskListenerExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateGlobalTaskListenerAsync(
            new CreateGlobalTaskListenerRequest
            {
                EventType = "complete",
                JobType = "my-task-listener",
            });

        Console.WriteLine($"Task listener: {result.Id}");
    }
    #endregion CreateGlobalTaskListener

    #region GetGlobalTaskListener
    public static async Task GetGlobalTaskListenerExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetGlobalTaskListenerAsync(
            new GlobalListenerId("listener-123"));

        Console.WriteLine($"Task listener: {result.EventType}");
    }
    #endregion GetGlobalTaskListener

    #region UpdateGlobalTaskListener
    public static async Task UpdateGlobalTaskListenerExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.UpdateGlobalTaskListenerAsync(
            new GlobalListenerId("listener-123"),
            new UpdateGlobalTaskListenerRequest
            {
                EventType = "complete",
                JobType = "updated-task-listener",
            });

        Console.WriteLine($"Updated listener: {result.Id}");
    }
    #endregion UpdateGlobalTaskListener

    #region DeleteGlobalTaskListener
    public static async Task DeleteGlobalTaskListenerExample()
    {
        using var client = CamundaClient.Create();

        await client.DeleteGlobalTaskListenerAsync(
            new GlobalListenerId("listener-123"));
    }
    #endregion DeleteGlobalTaskListener

    #region SearchGlobalTaskListeners
    public static async Task SearchGlobalTaskListenersExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchGlobalTaskListenersAsync(
            new GlobalTaskListenerSearchQueryRequest());

        foreach (var listener in result.Items)
        {
            Console.WriteLine($"Listener: {listener.Id}");
        }
    }
    #endregion SearchGlobalTaskListeners

    #region GetLicense
    public static async Task GetLicenseExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetLicenseAsync();
        Console.WriteLine($"License type: {result.LicenseType}");
    }
    #endregion GetLicense

    #region GetSystemConfiguration
    public static async Task GetSystemConfigurationExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetSystemConfigurationAsync();
        Console.WriteLine($"System config: {result}");
    }
    #endregion GetSystemConfiguration

    #region GetStatus
    public static async Task GetStatusExample()
    {
        using var client = CamundaClient.Create();

        await client.GetStatusAsync();
        Console.WriteLine("Cluster is healthy");
    }
    #endregion GetStatus

    #region PinClock
    public static async Task PinClockExample()
    {
        using var client = CamundaClient.Create();

        await client.PinClockAsync(new ClockPinRequest
        {
            Timestamp = 1700000000000,
        });
    }
    #endregion PinClock

    #region ResetClock
    public static async Task ResetClockExample()
    {
        using var client = CamundaClient.Create();

        await client.ResetClockAsync();
    }
    #endregion ResetClock

    #region EvaluateConditionals
    public static async Task EvaluateConditionalsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.EvaluateConditionalsAsync(
            new ConditionalEvaluationInstruction());

        Console.WriteLine($"Result: {result}");
    }
    #endregion EvaluateConditionals

    #region EvaluateExpression
    public static async Task EvaluateExpressionExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.EvaluateExpressionAsync(
            new ExpressionEvaluationRequest
            {
                Expression = "= 1 + 2",
            });

        Console.WriteLine($"Result: {result.Result}");
    }
    #endregion EvaluateExpression

    #region GetResource
    public static async Task GetResourceExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetResourceAsync(new ResourceKey("123456"));
        Console.WriteLine($"Resource: {result.ResourceName}");
    }
    #endregion GetResource

    #region GetResourceContent
    public static async Task GetResourceContentExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetResourceContentAsync(new ResourceKey("123456"));
        Console.WriteLine($"Content: {result}");
    }
    #endregion GetResourceContent

    #region GetUsageMetrics
    public static async Task GetUsageMetricsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetUsageMetricsAsync(
            startTime: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            endTime: new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero));

        Console.WriteLine($"Metrics: {result}");
    }
    #endregion GetUsageMetrics

    #region GetAuditLog
    public static async Task GetAuditLogExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetAuditLogAsync(new AuditLogKey("123456"));
        Console.WriteLine($"Audit log: {result.AuditLogKey}");
    }
    #endregion GetAuditLog

    #region SearchAuditLogs
    public static async Task SearchAuditLogsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.SearchAuditLogsAsync(
            new AuditLogSearchQueryRequest());

        foreach (var log in result.Items)
        {
            Console.WriteLine($"Audit log: {log.AuditLogKey}");
        }
    }
    #endregion SearchAuditLogs

    #region GetProcessInstanceStatisticsByError
    public static async Task GetProcessInstanceStatisticsByErrorExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessInstanceStatisticsByErrorAsync(
            new IncidentProcessInstanceStatisticsByErrorQuery());

        foreach (var stat in result.Items)
        {
            Console.WriteLine($"Error type: {stat.ErrorType}");
        }
    }
    #endregion GetProcessInstanceStatisticsByError

    #region GetProcessInstanceStatisticsByDefinition
    public static async Task GetProcessInstanceStatisticsByDefinitionExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetProcessInstanceStatisticsByDefinitionAsync(
            new IncidentProcessInstanceStatisticsByDefinitionQuery());

        foreach (var stat in result.Items)
        {
            Console.WriteLine($"Definition: {stat.ProcessDefinitionKey}");
        }
    }
    #endregion GetProcessInstanceStatisticsByDefinition

    #region GetJobErrorStatistics
    public static async Task GetJobErrorStatisticsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetJobErrorStatisticsAsync(
            new JobErrorStatisticsQuery());

        foreach (var stat in result.Items)
        {
            Console.WriteLine($"Error: {stat.ErrorCode}");
        }
    }
    #endregion GetJobErrorStatistics

    #region GetJobTimeSeriesStatistics
    public static async Task GetJobTimeSeriesStatisticsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetJobTimeSeriesStatisticsAsync(
            new JobTimeSeriesStatisticsQuery());

        foreach (var stat in result.Items)
        {
            Console.WriteLine($"Time series: {stat}");
        }
    }
    #endregion GetJobTimeSeriesStatistics

    #region GetJobTypeStatistics
    public static async Task GetJobTypeStatisticsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetJobTypeStatisticsAsync(
            new JobTypeStatisticsQuery());

        foreach (var stat in result.Items)
        {
            Console.WriteLine($"Job type: {stat.JobType}");
        }
    }
    #endregion GetJobTypeStatistics

    #region GetJobWorkerStatistics
    public static async Task GetJobWorkerStatisticsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetJobWorkerStatisticsAsync(
            new JobWorkerStatisticsQuery());

        foreach (var stat in result.Items)
        {
            Console.WriteLine($"Worker: {stat.Worker}");
        }
    }
    #endregion GetJobWorkerStatistics

    #region GetGlobalJobStatistics
    public static async Task GetGlobalJobStatisticsExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.GetGlobalJobStatisticsAsync(
            from: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            to: new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero));

        Console.WriteLine($"Global job stats: {result}");
    }
    #endregion GetGlobalJobStatistics
}
