using System.Net;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Regression tests for https://github.com/camunda/orchestration-cluster-api-csharp/issues/470.
///
/// The upstream spec declares the cluster-admin operations under `/cluster/v2/...` with a
/// path-item-level `servers` override whose URL drops the document's `/v2` base path
/// (`{schema}://{host}:{port}` instead of `{schema}://{host}:{port}/v2`). These endpoints are
/// served on the bare root, so the SDK must request `GET /cluster/v2/topology` — not
/// `GET /v2/cluster/v2/topology`.
/// </summary>
public class ClusterAdminServerOverrideTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler = new();

    private CamundaClient CreateClient(string restAddress = "https://mock.local") =>
        new(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = restAddress,
            },
            HttpMessageHandler = _handler,
        });

    [Fact]
    public async Task GetClusterTopology_ResolvesAgainstBareRoot_NotV2Base()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{}");

        using var client = CreateClient();
        await client.GetClusterTopologyAsync();

        var uri = _handler.Requests.Single().RequestUri!;
        Assert.Equal("/cluster/v2/topology", uri.AbsolutePath);
    }

    /// <summary>
    /// Class-of-defect guard: every generated operation whose spec path lives under
    /// `/cluster/v2/` must resolve against the bare root (the path-item `servers` override in
    /// the upstream spec), and every other operation must keep resolving against the `/v2`
    /// base. This sweeps the whole surface rather than pinning a single instance.
    /// </summary>
    [Fact]
    public async Task AllClusterAdminPaths_ResolveAgainstBareRoot()
    {
        // A representative sweep across the HTTP verbs and path shapes declared by the
        // upstream cluster-admin.yaml (all of which carry the servers override).
        var clusterAdminCalls = new Dictionary<string, Func<CamundaClient, Task>>
        {
            ["/cluster/v2/topology"] = c => c.GetClusterTopologyAsync(),
            ["/cluster/v2/status"] = c => c.GetClusterStatusAsync(),
            ["/cluster/v2/status/upgrade"] = c => c.GetClusterUpgradeStatusAsync(),
            ["/cluster/v2/exporting"] = c => c.GetClusterExportingStatusAsync(),
            ["/cluster/v2/exporting/pause"] = c => c.PauseClusterExportingAsync(),
            ["/cluster/v2/exporting/resume"] = c => c.ResumeClusterExportingAsync(),
            ["/cluster/v2/mode"] = c => c.ChangeClusterModeAsClusterAdminAsync(Mode.PROCESSING),
            ["/cluster/v2/restore"] = c => c.RestoreAsClusterAdminAsync(new ClusterRestoreRequest()),
            ["/cluster/v2/rebalance"] = c => c.GetClusterRebalanceAsync(),
            ["/cluster/v2/backups/runtime"] = c => c.ListRuntimeBackupsAsClusterAdminAsync(),
            ["/cluster/v2/backups/runtime/state"] = c => c.GetRuntimeBackupStateAsClusterAdminAsync(),
            ["/cluster/v2/backups/runtime/123"] = c => c.GetRuntimeBackupAsClusterAdminAsync(BackupId.AssumeExists(123)),
            ["/cluster/v2/backups/history"] = c => c.ListHistoryBackupsAsClusterAdminAsync(),
            ["/cluster/v2/backups/history/123"] = c => c.GetHistoryBackupAsClusterAdminAsync(BackupId.AssumeExists(123)),
        };

        foreach (var (expectedPath, call) in clusterAdminCalls)
        {
            _handler.Enqueue(HttpStatusCode.OK, "{}");
            using var client = CreateClient();
            await call(client);

            var uri = _handler.Requests[^1].RequestUri!;
            Assert.True(
                uri.AbsolutePath == expectedPath,
                $"Expected '{expectedPath}' but got '{uri.AbsolutePath}' — the operation must resolve against the bare root, not the /v2 base address.");
        }
    }

    [Fact]
    public async Task RegularOperation_StillResolvesAgainstV2Base()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{}");

        using var client = CreateClient();
        await client.GetTopologyAsync();

        var uri = _handler.Requests.Single().RequestUri!;
        Assert.Equal("/v2/topology", uri.AbsolutePath);
    }

    /// <summary>
    /// Class-of-defect guard: a supplied <see cref="HttpClient"/> whose BaseAddress carries a
    /// query and/or fragment must not let those components swallow the appended operation
    /// path. Concatenating <see cref="Uri.AbsoluteUri"/> would produce
    /// <c>https://mock.local/v2/?routing=admin/cluster/v2/topology</c> — the path becomes part
    /// of the query and the request targets <c>/v2/</c>. The root-server override must resolve
    /// against the path-only left part of the base address.
    /// </summary>
    [Theory]
    [InlineData("https://mock.local/v2/?routing=admin")]
    [InlineData("https://mock.local/v2/#section")]
    [InlineData("https://mock.local/v2/?routing=admin#section")]
    public async Task ClusterAdminOperation_IgnoresQueryAndFragmentInBaseAddress(string baseAddress)
    {
        _handler.Enqueue(HttpStatusCode.OK, "{}");

        using var httpClient = new HttpClient(_handler) { BaseAddress = new Uri(baseAddress) };
        using var client = new CamundaClient(new CamundaOptions { HttpClient = httpClient });
        await client.GetClusterTopologyAsync();

        var uri = _handler.Requests.Single().RequestUri!;
        Assert.Equal("/cluster/v2/topology", uri.AbsolutePath);
    }

    [Fact]
    public async Task RegularOperation_IgnoresQueryAndFragmentInBaseAddress()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{}");

        using var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://mock.local/v2/?routing=admin") };
        using var client = new CamundaClient(new CamundaOptions { HttpClient = httpClient });
        await client.GetTopologyAsync();

        var uri = _handler.Requests.Single().RequestUri!;
        Assert.Equal("/v2/topology", uri.AbsolutePath);
    }

    [Fact]
    public async Task ClusterAdminOperation_WorksWithRestAddressExact()
    {
        // CAMUNDA_REST_ADDRESS_EXACT uses the address verbatim (no /v2 suffix appended).
        // The cluster-admin override strips only a trailing "/v2" segment, so a custom
        // prefix is preserved.
        _handler.Enqueue(HttpStatusCode.OK, "{}");

        using var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local/custom-prefix/v2",
                ["CAMUNDA_REST_ADDRESS_EXACT"] = "true",
            },
            HttpMessageHandler = _handler,
        });
        await client.GetClusterTopologyAsync();

        var uri = _handler.Requests.Single().RequestUri!;
        Assert.Equal("/custom-prefix/cluster/v2/topology", uri.AbsolutePath);
    }

    public void Dispose()
    {
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }
}
