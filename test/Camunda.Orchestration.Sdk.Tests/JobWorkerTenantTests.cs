using System.Net;
using System.Text.Json;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Regression for camunda/orchestration-cluster-api-csharp#120 — workers
/// created via <c>CamundaClient.CreateJobWorker</c> must support
/// per-worker <c>TenantIds</c> / <c>TenantId</c> on
/// <see cref="JobWorkerConfig"/>, and must fall back to the configured
/// <c>DefaultTenantId</c> when neither is set explicitly.
///
/// <para>Class-of-defect scope: every public <c>CreateJobWorker</c>
/// overload routes through the same merged config and the same activation
/// poll body, so each tenant-resolution path (default, singular, plural,
/// explicit-overrides-default) is exercised end-to-end against the
/// activation HTTP request body.</para>
/// </summary>
public class JobWorkerTenantTests
{
    private static readonly string[] DefaultSentinel = new[] { "<default>" };
    private static readonly string[] AcmeOnly = new[] { "acme" };
    private static readonly string[] AlphaOnly = new[] { "alpha" };
    private static readonly string[] BetaGamma = new[] { "beta", "gamma" };
    private static readonly string[] Explicit12 = new[] { "explicit-1", "explicit-2" };
    private static readonly string[] BetaOnly = new[] { "beta" };

    private static async Task<List<string>> RunOnePollAndCaptureTenantIdsAsync(
        Dictionary<string, string> config,
        string? tenantId = null,
        IReadOnlyList<string>? tenantIds = null)
    {
        var handler = new MockHttpMessageHandler();
        var firstRequestBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        handler.Enqueue(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            firstRequestBody.TrySetResult(body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jobs\":[]}", System.Text.Encoding.UTF8, "application/json"),
            };
        });
        // Subsequent polls (if the loop spins faster than we tear it down) get an empty response.
        for (var i = 0; i < 8; i++)
            handler.Enqueue(HttpStatusCode.OK, "{\"jobs\":[]}");

        using var client = new CamundaClient(new CamundaOptions
        {
            Config = config,
            HttpMessageHandler = handler,
        });

        var workerConfig = new JobWorkerConfig
        {
            JobType = "tenant-test",
            JobTimeoutMs = 30_000,
            MaxConcurrentJobs = 1,
            AutoStart = false,
            TenantId = tenantId,
            TenantIds = tenantIds,
        };

        var worker = client.CreateJobWorker(workerConfig, (_, _) => Task.FromResult<object?>(null));
        worker.Start();

        var capturedJson = await firstRequestBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        using var doc = JsonDocument.Parse(capturedJson);
        if (!doc.RootElement.TryGetProperty("tenantIds", out var arr))
            return new List<string>();
        return arr.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
    }

    [Fact]
    public async Task PollLoop_FallsBackTo_DefaultTenantIdSentinel_WhenNothingConfigured()
    {
        var tenantIds = await RunOnePollAndCaptureTenantIdsAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
        });

        Assert.Equal(DefaultSentinel, tenantIds);
    }

    [Fact]
    public async Task PollLoop_UsesEnvDefaultTenantId_WhenWorkerOmitsTenantConfig()
    {
        var tenantIds = await RunOnePollAndCaptureTenantIdsAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            ["CAMUNDA_DEFAULT_TENANT_ID"] = "acme",
        });

        Assert.Equal(AcmeOnly, tenantIds);
    }

    [Fact]
    public async Task PollLoop_UsesSingularTenantId_FromWorkerConfig()
    {
        var tenantIds = await RunOnePollAndCaptureTenantIdsAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
        }, tenantId: "alpha");

        Assert.Equal(AlphaOnly, tenantIds);
    }

    [Fact]
    public async Task PollLoop_UsesPluralTenantIds_FromWorkerConfig()
    {
        var tenantIds = await RunOnePollAndCaptureTenantIdsAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
        }, tenantIds: BetaGamma);

        Assert.Equal(BetaGamma, tenantIds);
    }

    [Fact]
    public async Task PollLoop_ExplicitTenantOverrides_EnvDefault()
    {
        var tenantIds = await RunOnePollAndCaptureTenantIdsAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            ["CAMUNDA_DEFAULT_TENANT_ID"] = "ignored-default",
        }, tenantIds: Explicit12);

        Assert.Equal(Explicit12, tenantIds);
    }

    [Fact]
    public void CreateJobWorker_RejectsBoth_TenantId_And_TenantIds()
    {
        using var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = new MockHttpMessageHandler(),
        });

        var config = new JobWorkerConfig
        {
            JobType = "conflict",
            JobTimeoutMs = 30_000,
            AutoStart = false,
            TenantId = "alpha",
            TenantIds = BetaOnly,
        };

        var ex = Assert.Throws<ArgumentException>(
            () => client.CreateJobWorker(config, (_, _) => Task.FromResult<object?>(null)));
        Assert.Contains("mutually exclusive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateJobWorker_RejectsEmpty_TenantIds()
    {
        using var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = new MockHttpMessageHandler(),
        });

        var config = new JobWorkerConfig
        {
            JobType = "empty-tenants",
            JobTimeoutMs = 30_000,
            AutoStart = false,
            TenantIds = Array.Empty<string>(),
        };

        var ex = Assert.Throws<ArgumentException>(
            () => client.CreateJobWorker(config, (_, _) => Task.FromResult<object?>(null)));
        Assert.Contains("must not be empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void CreateJobWorker_RejectsEmptyOrWhitespace_TenantId(string badTenantId)
    {
        using var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = new MockHttpMessageHandler(),
        });

        var config = new JobWorkerConfig
        {
            JobType = "empty-tenant",
            JobTimeoutMs = 30_000,
            AutoStart = false,
            TenantId = badTenantId,
        };

        var ex = Assert.Throws<ArgumentException>(
            () => client.CreateJobWorker(config, (_, _) => Task.FromResult<object?>(null)));
        Assert.Contains("must not be empty or whitespace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("bad tenant!")]
    public void CreateJobWorker_RejectsMalformedEntries_InTenantIds(string badEntry)
    {
        using var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = new MockHttpMessageHandler(),
        });

        var config = new JobWorkerConfig
        {
            JobType = "malformed-tenants",
            JobTimeoutMs = 30_000,
            AutoStart = false,
            TenantIds = new[] { "valid-tenant", badEntry },
        };

        Assert.ThrowsAny<ArgumentException>(
            () => client.CreateJobWorker(config, (_, _) => Task.FromResult<object?>(null)));
    }
}
