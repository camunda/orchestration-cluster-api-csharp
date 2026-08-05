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
    private static readonly string[] FuncTenantOnly = new[] { "func-tenant" };

    private static async Task<List<string>> RunOnePollAndCaptureTenantIdsAsync(
        Dictionary<string, string> config,
        string? tenantId = null,
        IReadOnlyList<string>? tenantIds = null)
    {
        var capturedJson = await RunOnePollAndCaptureBodyAsync(config, tenantId, tenantIds);

        using var doc = JsonDocument.Parse(capturedJson);
        if (!doc.RootElement.TryGetProperty("tenantIds", out var arr))
            return new List<string>();
        return arr.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
    }

    private static async Task<string> RunOnePollAndCaptureBodyAsync(
        Dictionary<string, string> config,
        string? tenantId = null,
        IReadOnlyList<string>? tenantIds = null,
        TenantFilterEnum? tenantFilter = null)
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
            TenantFilter = tenantFilter,
        };

        var worker = client.CreateJobWorker(workerConfig, (_, _) => Task.FromResult<object?>(null));
        worker.Start();

        var capturedJson = await firstRequestBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        return capturedJson;
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
    public async Task PollLoop_EmptyTenantIds_FallsBackToDefault()
    {
        var tenantIds = await RunOnePollAndCaptureTenantIdsAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
        }, tenantIds: Array.Empty<string>());

        Assert.Equal(DefaultSentinel, tenantIds);
    }

    /// <summary>
    /// Regression for camunda/orchestration-cluster-api-csharp#122 — the plural
    /// <c>CAMUNDA_TENANT_IDS</c> env var hydrates into the client config and feeds
    /// worker activation when the worker declares no tenants of its own.
    /// </summary>
    [Fact]
    public async Task PollLoop_UsesEnvTenantIds_WhenWorkerOmitsTenantConfig()
    {
        var tenantIds = await RunOnePollAndCaptureTenantIdsAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            ["CAMUNDA_TENANT_IDS"] = "beta,gamma",
        });

        Assert.Equal(BetaGamma, tenantIds);
    }

    [Fact]
    public async Task PollLoop_EnvTenantIds_TolerateWhitespaceAndBlanks()
    {
        var tenantIds = await RunOnePollAndCaptureTenantIdsAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            ["CAMUNDA_TENANT_IDS"] = " beta , , gamma ",
        });

        Assert.Equal(BetaGamma, tenantIds);
    }

    [Fact]
    public async Task PollLoop_EnvTenantIds_OverrideEnvDefaultTenantId()
    {
        var tenantIds = await RunOnePollAndCaptureTenantIdsAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            ["CAMUNDA_DEFAULT_TENANT_ID"] = "ignored-default",
            ["CAMUNDA_TENANT_IDS"] = "beta,gamma",
        });

        Assert.Equal(BetaGamma, tenantIds);
    }

    [Fact]
    public async Task PollLoop_ExplicitTenantIds_OverrideEnvTenantIds()
    {
        var tenantIds = await RunOnePollAndCaptureTenantIdsAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            ["CAMUNDA_TENANT_IDS"] = "ignored-1,ignored-2",
        }, tenantIds: Explicit12);

        Assert.Equal(Explicit12, tenantIds);
    }

    [Fact]
    public async Task PollLoop_ExplicitSingularTenantId_OverridesEnvTenantIds()
    {
        var tenantIds = await RunOnePollAndCaptureTenantIdsAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            ["CAMUNDA_TENANT_IDS"] = "ignored-1,ignored-2",
        }, tenantId: "alpha");

        Assert.Equal(AlphaOnly, tenantIds);
    }

    [Fact]
    public async Task PollLoop_EmptyExplicitTenantIds_FallsBackToEnvTenantIds()
    {
        // An empty list is "unset", so it must reach the env var rather than skip
        // straight to the single default tenant.
        var tenantIds = await RunOnePollAndCaptureTenantIdsAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            ["CAMUNDA_TENANT_IDS"] = "beta,gamma",
        }, tenantIds: Array.Empty<string>());

        Assert.Equal(BetaGamma, tenantIds);
    }

    [Fact]
    public async Task PollLoop_AssignedTenantFilter_IgnoresEnvTenantIds()
    {
        var body = await RunOnePollAndCaptureBodyAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            ["CAMUNDA_TENANT_IDS"] = "beta,gamma",
        }, tenantFilter: TenantFilterEnum.ASSIGNED);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("ASSIGNED", doc.RootElement.GetProperty("tenantFilter").GetString());
        Assert.False(doc.RootElement.TryGetProperty("tenantIds", out _));
    }

    /// <summary>
    /// The <c>Func&lt;ActivatedJob, CancellationToken, Task&gt;</c> overload delegates to
    /// the primary overload — the env-var tenant set must reach it too.
    /// </summary>
    [Fact]
    public async Task FuncOverload_UsesEnvTenantIds()
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
        for (var i = 0; i < 8; i++)
            handler.Enqueue(HttpStatusCode.OK, "{\"jobs\":[]}");

        using var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
                ["CAMUNDA_TENANT_IDS"] = "beta,gamma",
            },
            HttpMessageHandler = handler,
        });

        var worker = client.CreateJobWorker(
            new JobWorkerConfig
            {
                JobType = "func-env-tenants",
                JobTimeoutMs = 30_000,
                MaxConcurrentJobs = 1,
                AutoStart = false,
            },
            (Func<ActivatedJob, CancellationToken, Task>)((_, _) => Task.CompletedTask));
        worker.Start();

        var capturedJson = await firstRequestBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        using var doc = JsonDocument.Parse(capturedJson);
        var tenantIds = doc.RootElement.GetProperty("tenantIds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(BetaGamma, tenantIds);
    }

    /// <summary>
    /// Regression for camunda/orchestration-cluster-api-csharp#376 — a worker
    /// configured with <c>TenantFilter = ASSIGNED</c> must send the filter and no
    /// tenant IDs at all, so the server activates jobs for whichever tenants are
    /// currently assigned to the authenticated client.
    /// </summary>
    [Fact]
    public async Task PollLoop_AssignedTenantFilter_SendsFilter_AndOmitsTenantIds()
    {
        // A configured default tenant would otherwise be injected — proves the
        // injection stands down rather than merely being absent by accident.
        var body = await RunOnePollAndCaptureBodyAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            ["CAMUNDA_DEFAULT_TENANT_ID"] = "ignored-default",
        }, tenantFilter: TenantFilterEnum.ASSIGNED);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("ASSIGNED", doc.RootElement.GetProperty("tenantFilter").GetString());
        Assert.False(doc.RootElement.TryGetProperty("tenantIds", out _));
    }

    [Fact]
    public async Task PollLoop_ProvidedTenantFilter_SendsFilter_AndKeepsTenantIds()
    {
        var body = await RunOnePollAndCaptureBodyAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
        }, tenantIds: BetaGamma, tenantFilter: TenantFilterEnum.PROVIDED);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("PROVIDED", doc.RootElement.GetProperty("tenantFilter").GetString());
        var tenantIds = doc.RootElement.GetProperty("tenantIds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(BetaGamma, tenantIds);
    }

    [Fact]
    public async Task PollLoop_ProvidedTenantFilter_StillFallsBackToDefaultTenant()
    {
        var body = await RunOnePollAndCaptureBodyAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            ["CAMUNDA_DEFAULT_TENANT_ID"] = "acme",
        }, tenantFilter: TenantFilterEnum.PROVIDED);

        using var doc = JsonDocument.Parse(body);
        var tenantIds = doc.RootElement.GetProperty("tenantIds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(AcmeOnly, tenantIds);
    }

    [Fact]
    public async Task PollLoop_OmitsTenantFilter_WhenUnset()
    {
        // Back-compat: workers that never touch TenantFilter must keep sending the
        // exact same body as before.
        var body = await RunOnePollAndCaptureBodyAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
        });

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.TryGetProperty("tenantFilter", out _));
    }

    [Fact]
    public void CreateJobWorker_RejectsAssignedTenantFilter_With_TenantId()
    {
        var ex = AssertCreateJobWorkerThrows(new JobWorkerConfig
        {
            JobType = "assigned-conflict",
            JobTimeoutMs = 30_000,
            AutoStart = false,
            TenantId = "alpha",
            TenantFilter = TenantFilterEnum.ASSIGNED,
        });
        Assert.Contains("ASSIGNED cannot be combined", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateJobWorker_RejectsAssignedTenantFilter_With_TenantIds()
    {
        var ex = AssertCreateJobWorkerThrows(new JobWorkerConfig
        {
            JobType = "assigned-conflict",
            JobTimeoutMs = 30_000,
            AutoStart = false,
            TenantIds = BetaGamma,
            TenantFilter = TenantFilterEnum.ASSIGNED,
        });
        Assert.Contains("ASSIGNED cannot be combined", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssignedTenantFilter_AcceptsEmptyTenantIds()
    {
        // An empty list is "unset" everywhere else (ResolveTenantIds, the env-var
        // fallback), so it must not trip the ASSIGNED conflict check.
        var body = await RunOnePollAndCaptureBodyAsync(new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
        }, tenantIds: Array.Empty<string>(), tenantFilter: TenantFilterEnum.ASSIGNED);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("ASSIGNED", doc.RootElement.GetProperty("tenantFilter").GetString());
        Assert.False(doc.RootElement.TryGetProperty("tenantIds", out _));
    }

    private static ArgumentException AssertCreateJobWorkerThrows(JobWorkerConfig config)
    {
        using var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = new MockHttpMessageHandler(),
        });

        return Assert.Throws<ArgumentException>(
            () => client.CreateJobWorker(config, (_, _) => Task.FromResult<object?>(null)));
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

    /// <summary>
    /// The <c>Func&lt;ActivatedJob, CancellationToken, Task&gt;</c> overload delegates
    /// to the primary overload — verify tenant resolution works end-to-end through it.
    /// </summary>
    [Fact]
    public async Task FuncOverload_UsesSingularTenantId()
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
        for (var i = 0; i < 8; i++)
            handler.Enqueue(HttpStatusCode.OK, "{\"jobs\":[]}");

        using var client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = handler,
        });

        var workerConfig = new JobWorkerConfig
        {
            JobType = "func-overload-test",
            JobTimeoutMs = 30_000,
            MaxConcurrentJobs = 1,
            AutoStart = false,
            TenantId = "func-tenant",
        };

        // Use the Func<ActivatedJob, CancellationToken, Task> overload
        var worker = client.CreateJobWorker(workerConfig, (_, _) => Task.CompletedTask);
        worker.Start();

        var capturedJson = await firstRequestBody.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(TimeSpan.FromSeconds(1));

        using var doc = JsonDocument.Parse(capturedJson);
        Assert.True(doc.RootElement.TryGetProperty("tenantIds", out var arr));
        var tenantIds = arr.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
        Assert.Equal(FuncTenantOnly, tenantIds);
    }
}
