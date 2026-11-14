using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Conformance matrix for the cross-SDK default-tenant rule (issue #537), which mirrors
/// the Camunda Java client: every tenant-scoped command sends the effective default
/// tenant (<c>&lt;default&gt;</c> unless <c>CAMUNDA_DEFAULT_TENANT_ID</c> is configured)
/// when the caller passes none, an explicit tenant always wins, and searches never get
/// a tenant added.
/// </summary>
public class DefaultTenantConformanceTests
{
    private const string Explicit = "explicit-tenant";

    private static (CamundaClient Client, MockHttpMessageHandler Handler) CreateClient(string? defaultTenant)
    {
        var handler = new MockHttpMessageHandler();
        var config = new Dictionary<string, string> { ["CAMUNDA_REST_ADDRESS"] = "https://mock.local" };
        if (defaultTenant != null)
            config["CAMUNDA_DEFAULT_TENANT_ID"] = defaultTenant;
        var client = new CamundaClient(new CamundaOptions { Config = config, Env = new Dictionary<string, string?>(), HttpMessageHandler = handler });
        return (client, handler);
    }

    private static TenantId? Tenant(bool explicitTenant) => explicitTenant ? TenantId.AssumeExists(Explicit) : null;

    /// <summary>
    /// Every rule-1 operation, keyed by case name, invoked with or without an explicit tenant.
    /// Case names are the operationId, optionally followed by <c>:</c> and a body variant.
    /// <see cref="EveryTenantScopedOperation_IsInTheMatrix"/> fails if an operation is missing.
    /// </summary>
    private static readonly Dictionary<string, Func<CamundaClient, bool, Task>> TenantScopedOps = new()
    {
        ["createDeployment"] = async (c, e) =>
        {
            var content = new MultipartFormDataContent();
            var bytes = new ByteArrayContent("<bpmn/>"u8.ToArray());
            bytes.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
            content.Add(bytes, "resources", "p.bpmn");
            if (e)
                content.Add(new StringContent(Explicit), "tenantId");
            await c.CreateDeploymentAsync(content);
        },
        ["createProcessInstance:ById"] = (c, e) => c.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById { TenantId = Tenant(e) }),
        ["createProcessInstance:ByKey"] = (c, e) => c.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionByKey { TenantId = Tenant(e) }),
        ["createProcessInstance:ByVersionTag"] = (c, e) => c.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionByVersionTag { TenantId = Tenant(e) }),
        ["publishMessage"] = (c, e) => c.PublishMessageAsync(new MessagePublicationRequest { Name = "m", TenantId = Tenant(e) }),
        ["correlateMessage"] = (c, e) => c.CorrelateMessageAsync(new MessageCorrelationRequest { Name = "m", TenantId = Tenant(e) }),
        ["broadcastSignal"] = (c, e) => c.BroadcastSignalAsync(new SignalBroadcastRequest { SignalName = "s", TenantId = Tenant(e) }),
        ["evaluateDecision:ById"] = (c, e) => c.EvaluateDecisionAsync(new DecisionEvaluationById { TenantId = Tenant(e) }),
        ["evaluateDecision:ByKey"] = (c, e) => c.EvaluateDecisionAsync(new DecisionEvaluationByKey { TenantId = Tenant(e) }),
        ["evaluateExpression"] = (c, e) => c.EvaluateExpressionAsync(new ExpressionEvaluationRequest { Expression = "=1", TenantId = Tenant(e) }),
        ["evaluateConditionals"] = (c, e) => c.EvaluateConditionalsAsync(new ConditionalEvaluationInstruction { TenantId = Tenant(e) }),
    };

    public static TheoryData<string> Operations()
    {
        var data = new TheoryData<string>();
        foreach (var name in TenantScopedOps.Keys)
            data.Add(name);
        return data;
    }

    /// <summary>Invokes an op and returns the tenantId the request carried (null if absent).</summary>
    private static async Task<string?> CaptureTenantAsync(string op, string? configuredDefault, bool explicitTenant)
    {
        var (client, handler) = CreateClient(configuredDefault);
        using var _ = client;
        string? tenant = null;
        var captured = false;
        handler.Enqueue(async req =>
        {
            captured = true;
            if (req.Content is MultipartFormDataContent multipart)
            {
                var parts = multipart.Where(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "tenantId").ToList();
                Assert.True(parts.Count <= 1, "multipart request must carry at most one tenantId part");
                if (parts.Count == 1)
                    tenant = await parts[0].ReadAsStringAsync();
            }
            else
            {
                var body = await req.Content!.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("tenantId", out var t) && t.ValueKind == JsonValueKind.String)
                    tenant = t.GetString();
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        try
        {
            await TenantScopedOps[op](client, explicitTenant);
        }
        catch (Exception) when (captured)
        {
            // The mock's "{}" response may not satisfy every result type; only the request shape matters here.
        }

        Assert.True(captured, $"{op} did not send a request");
        return tenant;
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task SendsDefaultSentinel_WhenNoTenantConfiguredOrPassed(string op)
    {
        Assert.Equal("<default>", await CaptureTenantAsync(op, configuredDefault: null, explicitTenant: false));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task SendsConfiguredDefault_WhenNoTenantPassed(string op)
    {
        Assert.Equal("configured-tenant", await CaptureTenantAsync(op, configuredDefault: "configured-tenant", explicitTenant: false));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task ExplicitTenant_OverridesConfiguredDefault(string op)
    {
        Assert.Equal(Explicit, await CaptureTenantAsync(op, configuredDefault: "configured-tenant", explicitTenant: true));
    }

    /// <summary>
    /// Guards the matrix against new tenant-scoped operations. The SDK decides by the
    /// presence of an optional <c>tenantId</c> in the request body, so the expected set
    /// comes from two places: the bundler's <c>optionalTenantIdInBody</c> flag in
    /// <c>spec-metadata.json</c> (the flag the JS SDK uses), and every generated client
    /// method whose body type, or any concrete subtype of it, implements
    /// <see cref="ITenantIdSettable"/>.
    /// </summary>
    [Fact]
    public void EveryTenantScopedOperation_IsInTheMatrix()
    {
        var expected = new SortedSet<string>(StringComparer.Ordinal);

        using (var metadata = JsonDocument.Parse(File.ReadAllText(FindBundledFile("spec-metadata.json"))))
        {
            foreach (var op in metadata.RootElement.GetProperty("operations").EnumerateArray())
            {
                if (op.TryGetProperty("optionalTenantIdInBody", out var flag) && flag.ValueKind == JsonValueKind.True)
                    expected.Add(op.GetProperty("operationId").GetString()!);
            }
        }

        var sdkTypes = typeof(CamundaClient).Assembly.GetTypes();
        foreach (var method in typeof(CamundaClient).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!method.Name.EndsWith("Async", StringComparison.Ordinal))
                continue;
            var body = method.GetParameters().FirstOrDefault(p => p.Name == "body")?.ParameterType;
            // Untyped `object` bodies (schema-less request bodies) cannot carry a defaulted tenant.
            if (body == null || body == typeof(object))
                continue;
            var settable = sdkTypes.Any(t => !t.IsAbstract && body.IsAssignableFrom(t) && typeof(ITenantIdSettable).IsAssignableFrom(t));
            if (settable)
                expected.Add(char.ToLowerInvariant(method.Name[0]) + method.Name[1..^"Async".Length]);
        }

        var covered = new SortedSet<string>(TenantScopedOps.Keys.Select(k => k.Split(':')[0]), StringComparer.Ordinal);
        Assert.NotEmpty(expected);
        Assert.True(expected.SetEquals(covered), $"missing from matrix: [{string.Join(", ", expected.Except(covered))}]; not tenant-scoped: [{string.Join(", ", covered.Except(expected))}]");
    }

    private static string FindBundledFile(string name)
    {
        // The test process runs from test/.../bin/<config>/<tfm>/. Walk up to the repo root.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "external-spec", "bundled", name);
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"Could not locate external-spec/bundled/{name}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("configured-tenant")]
    public async Task ActivateJobs_SendsEffectiveDefault_WhenTenantFilterUnsetOrProvided(string? configured)
    {
        foreach (var filter in new TenantFilterEnum?[] { null, TenantFilterEnum.PROVIDED })
        {
            foreach (var ids in new List<TenantId>?[] { null, new List<TenantId>() })
            {
                var body = await CaptureActivateJobsAsync(configured, new JobActivationRequest
                {
                    Type = "t",
                    Timeout = 1000,
                    MaxJobsToActivate = 1,
                    TenantFilter = filter,
                    TenantIds = ids,
                });
                var tenantIds = body.GetProperty("tenantIds").EnumerateArray().Select(x => x.GetString()).ToList();
                Assert.Equal(new[] { configured ?? "<default>" }, tenantIds);
            }
        }
    }

    [Fact]
    public async Task ActivateJobs_AssignedFilter_LeavesTenantIdsAlone()
    {
        var body = await CaptureActivateJobsAsync(null, new JobActivationRequest
        {
            Type = "t",
            Timeout = 1000,
            MaxJobsToActivate = 1,
            TenantFilter = TenantFilterEnum.ASSIGNED,
        });
        Assert.False(body.TryGetProperty("tenantIds", out var t) && t.ValueKind == JsonValueKind.Array);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivateJobs_AssignedFilter_PreservesCallerTenantIds(bool explicitIds)
    {
        // Fixed expectations, independent of the mutable list handed to the SDK, so an
        // in-place clear or default-append under ASSIGNED is detected rather than masked.
        var expected = explicitIds ? new[] { Explicit } : Array.Empty<string>();
        var body = await CaptureActivateJobsAsync("configured-tenant", new JobActivationRequest
        {
            Type = "t",
            Timeout = 1000,
            MaxJobsToActivate = 1,
            TenantFilter = TenantFilterEnum.ASSIGNED,
            TenantIds = explicitIds ? new List<TenantId> { TenantId.AssumeExists(Explicit) } : new List<TenantId>(),
        });
        var tenantIds = body.GetProperty("tenantIds").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal(expected, tenantIds);
    }

    [Fact]
    public async Task ActivateJobs_ExplicitTenantIds_Win()
    {
        var body = await CaptureActivateJobsAsync("configured-tenant", new JobActivationRequest
        {
            Type = "t",
            Timeout = 1000,
            MaxJobsToActivate = 1,
            TenantIds = new List<TenantId> { TenantId.AssumeExists(Explicit) },
        });
        var tenantIds = body.GetProperty("tenantIds").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal(new[] { Explicit }, tenantIds);
    }

    [Fact]
    public async Task Searches_NeverGetATenantAdded()
    {
        var (client, handler) = CreateClient("configured-tenant");
        using var _ = client;
        string? body = null;
        handler.Enqueue(async req =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"items\":[],\"page\":{\"totalItems\":0}}", System.Text.Encoding.UTF8, "application/json"),
            };
        });
        try
        {
            await client.SearchProcessInstancesAsync(new ProcessInstanceSearchQuery());
        }
        catch (Exception) when (body != null)
        {
        }
        Assert.NotNull(body);
        AssertNoTenantProperty(JsonElement.Parse(body!));
    }

    private static void AssertNoTenantProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                Assert.False(
                    prop.Name is "tenantId" or "tenantIds",
                    $"Search request must not carry a '{prop.Name}' property, got: {prop.Value.GetRawText()}");
                AssertNoTenantProperty(prop.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                AssertNoTenantProperty(item);
        }
    }

    private static async Task<JsonElement> CaptureActivateJobsAsync(string? configured, JobActivationRequest request)
    {
        var (client, handler) = CreateClient(configured);
        using var _ = client;
        string? body = null;
        handler.Enqueue(async req =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jobs\":[]}", System.Text.Encoding.UTF8, "application/json"),
            };
        });
        try
        {
            await client.ActivateJobsAsync(request);
        }
        catch (Exception) when (body != null)
        {
        }
        Assert.NotNull(body);
        return JsonElement.Parse(body!);
    }
}
