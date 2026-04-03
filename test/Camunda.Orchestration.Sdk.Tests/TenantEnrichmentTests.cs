using System.Net;
using System.Text.Json;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests that methods with optional tenantId in the request body
/// automatically inject the default tenant ID when none is supplied.
/// </summary>
public class TenantEnrichmentTests : IDisposable
{
    private readonly CamundaClient _client;
    private readonly CamundaClient _clientCustomTenant;
    private readonly MockHttpMessageHandler _handler;
    private readonly MockHttpMessageHandler _handlerCustom;

    public TenantEnrichmentTests()
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
                ["CAMUNDA_DEFAULT_TENANT_ID"] = "custom-tenant",
            },
            HttpMessageHandler = _handlerCustom,
        });
    }

    [Fact]
    public async Task BroadcastSignal_InjectsDefaultTenantId_WhenNotSet()
    {
        string? capturedBody = null;
        _handler.Enqueue(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _client.BroadcastSignalAsync(new SignalBroadcastRequest
        {
            SignalName = "test-signal",
        });

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("<default>", doc.RootElement.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task BroadcastSignal_PreservesExplicitTenantId()
    {
        string? capturedBody = null;
        _handler.Enqueue(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _client.BroadcastSignalAsync(new SignalBroadcastRequest
        {
            SignalName = "test-signal",
            TenantId = TenantId.AssumeExists("my-tenant"),
        });

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("my-tenant", doc.RootElement.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task BroadcastSignal_UsesCustomDefaultTenantId()
    {
        string? capturedBody = null;
        _handlerCustom.Enqueue(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _clientCustomTenant.BroadcastSignalAsync(new SignalBroadcastRequest
        {
            SignalName = "test-signal",
        });

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("custom-tenant", doc.RootElement.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task CorrelateMessage_InjectsDefaultTenantId()
    {
        string? capturedBody = null;
        _handler.Enqueue(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _client.CorrelateMessageAsync(new MessageCorrelationRequest
        {
            Name = "test-msg",
        });

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("<default>", doc.RootElement.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task PublishMessage_InjectsDefaultTenantId()
    {
        string? capturedBody = null;
        _handler.Enqueue(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _client.PublishMessageAsync(new MessagePublicationRequest
        {
            Name = "test-msg",
        });

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("<default>", doc.RootElement.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task EvaluateExpression_InjectsDefaultTenantId_AsString()
    {
        string? capturedBody = null;
        _handler.Enqueue(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"expression\":\"=1+1\",\"result\":\"2\"}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _client.EvaluateExpressionAsync(new ExpressionEvaluationRequest
        {
            Expression = "=1+1",
        });

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("<default>", doc.RootElement.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task CreateProcessInstance_OneOf_InjectsDefaultTenantId()
    {
        string? capturedBody = null;
        _handler.Enqueue(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"processDefinitionKey\":\"1\"}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionByKey
        {
            ProcessDefinitionKey = ProcessDefinitionKey.AssumeExists("1"),
        });

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("<default>", doc.RootElement.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task CreateProcessInstance_OneOf_PreservesExplicitTenantId()
    {
        string? capturedBody = null;
        _handler.Enqueue(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"processDefinitionKey\":\"1\"}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionByKey
        {
            ProcessDefinitionKey = ProcessDefinitionKey.AssumeExists("1"),
            TenantId = TenantId.AssumeExists("explicit-tenant"),
        });

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("explicit-tenant", doc.RootElement.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task EvaluateDecision_OneOf_InjectsDefaultTenantId()
    {
        string? capturedBody = null;
        _handler.Enqueue(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"decisionDefinitionKey\":\"1\",\"decisionId\":\"d\",\"decisionName\":\"n\",\"decisionVersion\":1,\"decisionRequirementsId\":\"r\",\"decisionRequirementsKey\":\"1\",\"output\":\"x\",\"evaluatedDecisions\":[]}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        await _client.EvaluateDecisionAsync(new DecisionEvaluationByKey
        {
            DecisionDefinitionKey = DecisionDefinitionKey.AssumeExists("1"),
        });

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("<default>", doc.RootElement.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task DeployResources_InjectsDefaultTenantId()
    {
        string? capturedContent = null;
        _handler.Enqueue(async req =>
        {
            capturedContent = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"deploymentKey\":\"1\",\"tenantId\":\"<default>\",\"deployments\":[]}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        // Create a temp file
        var tmpFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmpFile, "<bpmn/>");
            var renamed = Path.ChangeExtension(tmpFile, ".bpmn");
            File.Move(tmpFile, renamed);
            tmpFile = renamed;

            await _client.DeployResourcesFromFilesAsync(new[] { tmpFile });

            Assert.NotNull(capturedContent);
            Assert.Contains("<default>", capturedContent);
        }
        finally
        {
            if (File.Exists(tmpFile))
                File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task DeployResources_PreservesExplicitTenantId()
    {
        string? capturedContent = null;
        _handler.Enqueue(async req =>
        {
            capturedContent = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"deploymentKey\":\"1\",\"tenantId\":\"my-tenant\",\"deployments\":[]}", System.Text.Encoding.UTF8, "application/json"),
            };
        });

        var tmpFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmpFile, "<bpmn/>");
            var renamed = Path.ChangeExtension(tmpFile, ".bpmn");
            File.Move(tmpFile, renamed);
            tmpFile = renamed;

            await _client.DeployResourcesFromFilesAsync(new[] { tmpFile }, tenantId: "my-tenant");

            Assert.NotNull(capturedContent);
            Assert.Contains("my-tenant", capturedContent);
            Assert.DoesNotContain("<default>", capturedContent);
        }
        finally
        {
            if (File.Exists(tmpFile))
                File.Delete(tmpFile);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _clientCustomTenant.Dispose();
        GC.SuppressFinalize(this);
    }
}
