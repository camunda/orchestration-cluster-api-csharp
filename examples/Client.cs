// Compilable usage examples for client construction and topology.
// These examples are type-checked during build to guard against API regressions.
using Camunda.Orchestration.Sdk;

public static class ClientExamples
{
    #region CreateClient
    // <CreateClient>
    public static async Task CreateClientExample()
    {
        using var client = CamundaClient.Create();

        var topology = await client.GetTopologyAsync();
        Console.WriteLine($"Cluster size: {topology.ClusterSize}");
    }
    // </CreateClient>
    #endregion CreateClient

    #region GetTopology

    // <GetTopology>
    public static async Task GetTopologyExample()
    {
        using var client = CamundaClient.Create();

        var topology = await client.GetTopologyAsync();
        Console.WriteLine($"Cluster size: {topology.ClusterSize}");
    }
    // </GetTopology>
    #endregion GetTopology

    #region ChangeClusterMode

    // <ChangeClusterMode>
    public static async Task ChangeClusterModeExample()
    {
        using var client = CamundaClient.Create();

        // Pass dryRun: true to validate the request and inspect the resulting plan
        // without applying it. Omit it (or set it to false) to trigger the transition.
        var change = await client.ChangeClusterModeAsync(Mode.RECOVERING, dryRun: true);

        // Operations are grouped by physical tenant; a null tenant means the operation
        // is not scoped to one, such as a broker lifecycle operation.
        Console.WriteLine($"Cluster change {change.ChangeId}:");
        foreach (var group in change.PlannedChanges)
        {
            var tenant = group.PhysicalTenantId is null ? "cluster-wide" : group.PhysicalTenantId;
            Console.WriteLine($"  {tenant}:");
            foreach (var operation in group.Operations)
            {
                var suffix = operation.Mode is null ? "" : $" -> {operation.Mode}";
                Console.WriteLine($"    {operation.Operation}{suffix}");
            }
        }
    }
    // </ChangeClusterMode>
    #endregion ChangeClusterMode

    #region GetClusterStatus

    // <GetClusterStatus>
    public static async Task GetClusterStatusExample()
    {
        using var client = CamundaClient.Create();

        var status = await client.GetClusterStatusAsync();

        Console.WriteLine($"Cluster status: {status.Status}");
    }
    // </GetClusterStatus>
    #endregion GetClusterStatus

    #region GetRestoreStatus

    // <GetRestoreStatus>
    public static async Task GetRestoreStatusExample()
    {
        using var client = CamundaClient.Create();

        // Poll this endpoint while the cluster is in recovery mode to track progress.
        var status = await client.GetRestoreStatusAsync();

        Console.WriteLine($"Restore {status.ChangeId}: {status.Status}");
    }
    // </GetRestoreStatus>
    #endregion GetRestoreStatus

    #region Restore

    // <Restore>
    public static async Task RestoreExample()
    {
        using var client = CamundaClient.Create();

        // The cluster must be in recovery mode before a restore is accepted.
        // Provide either a list of backup IDs (one per partition) or a time
        // range (From/To) that selects the backups to restore, but not both.
        var change = await client.RestoreAsync(new RestoreRequest
        {
            BackupIds = new List<long> { 100, 101 },
        });

        Console.WriteLine($"Cluster change {change.ChangeId}:");
        foreach (var group in change.PlannedChanges)
        {
            var tenant = group.PhysicalTenantId is null ? "cluster-wide" : group.PhysicalTenantId;
            Console.WriteLine($"  {tenant}:");
            foreach (var operation in group.Operations)
            {
                var suffix = operation.Mode is null ? "" : $" -> {operation.Mode}";
                Console.WriteLine($"    {operation.Operation}{suffix}");
            }
        }
    }
    // </Restore>
    #endregion Restore
}
