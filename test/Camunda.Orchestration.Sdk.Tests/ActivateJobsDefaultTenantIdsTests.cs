using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Regression for camunda/orchestration-cluster-api-csharp#123 — the
/// `CAMUNDA_DEFAULT_TENANT_ID` (singular) value was not propagated to
/// `ActivateJobsAsync` because the generator's tenant-default injector
/// (`ITenantIdSettable`) only handled the singular `tenantId` body
/// property. `JobActivationRequest` exposes the plural `TenantIds`
/// (`List&lt;TenantId&gt;`), so workers using only the default-tenant env
/// var silently polled without `tenantIds` and missed jobs in
/// multi-tenant setups. Mirrors orchestration-cluster-api-js#170 / PR #171.
///
/// Class-of-defect: any request body schema with an optional
/// `tenantIds: array` property must, when the caller omits it, default to
/// `[_config.DefaultTenantId]`. The structural test scans the bundled
/// spec for every operation matching that shape so the same defect cannot
/// recur in a sibling operation without being caught.
/// </summary>
public class ActivateJobsDefaultTenantIdsTests : IDisposable
{
    private static readonly string[] DefaultTenantIdSentinel = new[] { "<default>" };
    private static readonly string[] CustomTenantSingle = new[] { "tenant-alpha" };
    private static readonly string[] ExplicitTenantPair = new[] { "tenant-beta", "tenant-gamma" };

    private readonly CamundaClient _client;
    private readonly CamundaClient _clientCustomTenant;
    private readonly MockHttpMessageHandler _handler;
    private readonly MockHttpMessageHandler _handlerCustom;

    public ActivateJobsDefaultTenantIdsTests()
    {
        _handler = new MockHttpMessageHandler();
        _client = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = _handler,
        });

        _handlerCustom = new MockHttpMessageHandler();
        _clientCustomTenant = new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
                ["CAMUNDA_DEFAULT_TENANT_ID"] = "tenant-alpha",
            },
            HttpMessageHandler = _handlerCustom,
        });
    }

    [Fact]
    public async Task ActivateJobs_InjectsDefaultTenantIdSentinel_WhenOmitted()
    {
        // ConfigurationHydrator defaults DefaultTenantId to "<default>" when
        // CAMUNDA_DEFAULT_TENANT_ID is not set (matches the singular tenantId
        // injection semantics already exercised by TenantEnrichmentTests).
        string? capturedBody = null;
        _handler.Enqueue(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jobs\":[]}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _client.ActivateJobsAsync(new JobActivationRequest
        {
            Type = "demo-task",
            Timeout = 30_000,
            MaxJobsToActivate = 1,
        });

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!);
        var tenantIds = doc.RootElement.GetProperty("tenantIds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(DefaultTenantIdSentinel, tenantIds);
    }

    [Fact]
    public async Task ActivateJobs_UsesCustomDefaultTenantId_WhenConfigured()
    {
        string? capturedBody = null;
        _handlerCustom.Enqueue(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jobs\":[]}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _clientCustomTenant.ActivateJobsAsync(new JobActivationRequest
        {
            Type = "demo-task",
            Timeout = 30_000,
            MaxJobsToActivate = 1,
        });

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!);
        var tenantIds = doc.RootElement.GetProperty("tenantIds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(CustomTenantSingle, tenantIds);
    }

    [Fact]
    public async Task ActivateJobs_PreservesExplicitTenantIds()
    {
        string? capturedBody = null;
        _handlerCustom.Enqueue(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jobs\":[]}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _clientCustomTenant.ActivateJobsAsync(new JobActivationRequest
        {
            Type = "demo-task",
            Timeout = 30_000,
            MaxJobsToActivate = 1,
            TenantIds = new List<TenantId>
            {
                TenantId.AssumeExists("tenant-beta"),
                TenantId.AssumeExists("tenant-gamma"),
            },
        });

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!);
        var tenantIds = doc.RootElement.GetProperty("tenantIds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(ExplicitTenantPair, tenantIds);
    }

    [Fact]
    public void EveryRequestSchemaWithOptionalTenantIdsArray_ImplementsITenantIdsSettable()
    {
        // Class-scoped guard: scan the bundled spec for any operation whose
        // request body schema (whether $ref'd or inline) has an optional
        // `tenantIds: array` property and assert the corresponding generated
        // class implements ITenantIdsSettable. Today only JobActivationRequest
        // matches; this guard prevents the same defect class from recurring in
        // a future sibling schema.
        var spec = LoadBundledSpec();

        var matches = FindRequestSchemasWithOptionalTenantIdsArray(spec);

        Assert.Contains("JobActivationRequest", matches); // Sanity: upstream spec changed if this fails.

        var sdkAssembly = typeof(CamundaClient).Assembly;
        var iface = sdkAssembly.GetType("Camunda.Orchestration.Sdk.ITenantIdsSettable");
        Assert.NotNull(iface);

        var missing = new List<string>();
        foreach (var schemaName in matches)
        {
            var typeName = $"Camunda.Orchestration.Sdk.{SanitizeTypeName(schemaName)}";
            var t = sdkAssembly.GetType(typeName);
            Assert.NotNull(t);
            if (!iface!.IsAssignableFrom(t))
                missing.Add(schemaName);
        }
        Assert.Empty(missing);
    }

    private static JsonNode LoadBundledSpec()
    {
        // The test process runs from test/.../bin/Debug/net8.0/. Walk up to repo root.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, "external-spec", "bundled", "rest-api.bundle.json");
            if (File.Exists(candidate))
                return JsonNode.Parse(File.ReadAllText(candidate))!;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Could not locate external-spec/bundled/rest-api.bundle.json");
    }

    private static List<string> FindRequestSchemasWithOptionalTenantIdsArray(JsonNode spec)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var schemas = spec["components"]!["schemas"]!.AsObject();
        var paths = spec["paths"]!.AsObject();

        foreach (var (_, pathItem) in paths)
        {
            if (pathItem is not JsonObject pathObj)
                continue;
            foreach (var (_, opNode) in pathObj)
            {
                if (opNode is not JsonObject op)
                    continue;
                var rb = op["requestBody"] as JsonObject;
                var content = rb?["content"] as JsonObject;
                if (content == null)
                    continue;
                var operationId = op["operationId"]?.GetValue<string>();
                foreach (var (ct, mt) in content)
                {
                    if (!ct.Contains("json", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (mt is not JsonObject mtObj)
                        continue;
                    var schema = mtObj["schema"];
                    var (resolved, refName) = Resolve(schema, schemas);
                    if (resolved == null)
                        continue;
                    if (!HasOptionalTenantIdsArray(resolved))
                        continue;
                    // For $ref bodies the SDK type is the referenced schema name.
                    // For inline bodies the generator names the class
                    // `{PascalCase(operationId)}Request` (see GetBodySchemaRef
                    // / `bodySchemaRef ?? opId + "Request"` in the generator).
                    if (refName != null)
                    {
                        result.Add(refName);
                    }
                    else if (!string.IsNullOrEmpty(operationId))
                    {
                        result.Add(PascalCaseFirst(operationId) + "Request");
                    }
                }
            }
        }
        return result.ToList();
    }

    private static string PascalCaseFirst(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static (JsonObject? resolved, string? refName) Resolve(JsonNode? schema, JsonObject schemas)
    {
        if (schema is not JsonObject obj)
            return (null, null);
        var dollarRef = obj["$ref"]?.GetValue<string>();
        if (dollarRef != null)
        {
            var name = dollarRef.Split('/').Last();
            if (schemas[name] is JsonObject resolved)
                return (resolved, name);
            return (null, null);
        }
        return (obj, null);
    }

    private static bool HasOptionalTenantIdsArray(JsonObject schema)
    {
        if (schema["type"]?.GetValue<string>() != "object")
            return false;
        var props = schema["properties"] as JsonObject;
        if (props == null)
            return false;
        var tenantIds = props["tenantIds"] as JsonObject;
        if (tenantIds == null)
            return false;
        var required = schema["required"] as JsonArray;
        if (required != null && required.Any(n => n?.GetValue<string>() == "tenantIds"))
            return false;
        return tenantIds["type"]?.GetValue<string>() == "array";
    }

    private static string SanitizeTypeName(string name) =>
        name.Replace("XML", "Xml").Replace("-", "").Replace(".", "").Replace("$", "").Replace(" ", "");

    public void Dispose()
    {
        _client.Dispose();
        _clientCustomTenant.Dispose();
        GC.SuppressFinalize(this);
    }
}
