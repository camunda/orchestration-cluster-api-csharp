using System.Net;
using System.Text.Json;

namespace Camunda.Orchestration.Sdk.Tests;

public class DeploymentTests : IDisposable
{
    private readonly CamundaClient _client;
    private readonly MockHttpMessageHandler _handler;
    private readonly string _tempDir;

    public DeploymentTests()
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
        _tempDir = Path.Combine(Path.GetTempPath(), $"camunda-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _client.Dispose();
        try
        { Directory.Delete(_tempDir, true); }
        catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string CreateTempFile(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    #region ExtendedDeploymentResponse tests

    [Fact]
    public void ExtendedDeploymentResponse_SortsIntoBuckets()
    {
        var raw = new DeploymentResult
        {
            DeploymentKey = DeploymentKey.AssumeExists("123"),
            TenantId = TenantId.AssumeExists("test-tenant"),
            Deployments =
            [
                new DeploymentMetadataResult
                {
                    ProcessDefinition = new DeploymentProcessResult
                    {
                        ProcessDefinitionId = ProcessDefinitionId.AssumeExists("proc-1"),
                        ProcessDefinitionKey = ProcessDefinitionKey.AssumeExists("1"),
                        ProcessDefinitionVersion = 1,
                        ResourceName = "test.bpmn",
                        TenantId = TenantId.AssumeExists("test-tenant"),
                    },
                },
                new DeploymentMetadataResult
                {
                    DecisionDefinition = new DeploymentDecisionResult
                    {
                        DecisionDefinitionId = DecisionDefinitionId.AssumeExists("dec-1"),
                        DecisionDefinitionKey = DecisionDefinitionKey.AssumeExists("2"),
                    },
                },
                new DeploymentMetadataResult
                {
                    DecisionRequirements = new DeploymentDecisionRequirementsResult
                    {
                        DecisionRequirementsKey = DecisionRequirementsKey.AssumeExists("3"),
                    },
                },
                new DeploymentMetadataResult
                {
                    Form = new DeploymentFormResult
                    {
                        FormId = FormId.AssumeExists("form-1"),
                        FormKey = FormKey.AssumeExists("4"),
                    },
                },
                new DeploymentMetadataResult
                {
                    Resource = new DeploymentResourceResult
                    {
                        ResourceId = "res-1",
                        ResourceKey = ResourceKey.AssumeExists("5"),
                    },
                },
            ],
        };

        var extended = new ExtendedDeploymentResponse(raw);

        Assert.Equal("123", extended.DeploymentKey.ToString());
        Assert.Equal("test-tenant", extended.TenantId.ToString());
        Assert.Equal(5, extended.Deployments.Count);
        Assert.Single(extended.Processes);
        Assert.Equal("proc-1", extended.Processes[0].ProcessDefinitionId.ToString());
        Assert.Single(extended.Decisions);
        Assert.Equal("dec-1", extended.Decisions[0].DecisionDefinitionId.ToString());
        Assert.Single(extended.DecisionRequirements);
        Assert.Equal("3", extended.DecisionRequirements[0].DecisionRequirementsKey.ToString());
        Assert.Single(extended.Forms);
        Assert.Equal("form-1", extended.Forms[0].FormId.ToString());
        Assert.Single(extended.Resources);
        Assert.Equal("res-1", extended.Resources[0].ResourceId);
    }

    [Fact]
    public void ExtendedDeploymentResponse_HandlesEmptyDeployments()
    {
        var raw = new DeploymentResult
        {
            DeploymentKey = DeploymentKey.AssumeExists("1"),
            TenantId = TenantId.AssumeExists("t"),
            Deployments = [],
        };

        var extended = new ExtendedDeploymentResponse(raw);

        Assert.Empty(extended.Processes);
        Assert.Empty(extended.Decisions);
        Assert.Empty(extended.DecisionRequirements);
        Assert.Empty(extended.Forms);
        Assert.Empty(extended.Resources);
    }

    [Fact]
    public void ExtendedDeploymentResponse_HandlesNullDeployments()
    {
        var raw = new DeploymentResult
        {
            DeploymentKey = DeploymentKey.AssumeExists("1"),
            TenantId = TenantId.AssumeExists("t"),
            Deployments = null!,
        };

        var extended = new ExtendedDeploymentResponse(raw);

        Assert.Empty(extended.Processes);
        Assert.Empty(extended.Decisions);
        Assert.Empty(extended.DecisionRequirements);
        Assert.Empty(extended.Forms);
        Assert.Empty(extended.Resources);
    }

    [Fact]
    public void ExtendedDeploymentResponse_ExposesRawResponse()
    {
        var raw = new DeploymentResult
        {
            DeploymentKey = DeploymentKey.AssumeExists("42"),
            TenantId = TenantId.AssumeExists("t"),
            Deployments = [],
        };

        var extended = new ExtendedDeploymentResponse(raw);
        Assert.Same(raw, extended.Raw);
    }

    [Fact]
    public void ExtendedDeploymentResponse_MultipleProcesses()
    {
        var raw = new DeploymentResult
        {
            DeploymentKey = DeploymentKey.AssumeExists("1"),
            TenantId = TenantId.AssumeExists("t"),
            Deployments =
            [
                new DeploymentMetadataResult
                {
                    ProcessDefinition = new DeploymentProcessResult
                    {
                        ProcessDefinitionId = ProcessDefinitionId.AssumeExists("p1"),
                        ProcessDefinitionKey = ProcessDefinitionKey.AssumeExists("10"),
                        ProcessDefinitionVersion = 1,
                        ResourceName = "a.bpmn",
                        TenantId = TenantId.AssumeExists("t"),
                    },
                },
                new DeploymentMetadataResult
                {
                    ProcessDefinition = new DeploymentProcessResult
                    {
                        ProcessDefinitionId = ProcessDefinitionId.AssumeExists("p2"),
                        ProcessDefinitionKey = ProcessDefinitionKey.AssumeExists("11"),
                        ProcessDefinitionVersion = 1,
                        ResourceName = "b.bpmn",
                        TenantId = TenantId.AssumeExists("t"),
                    },
                },
            ],
        };

        var extended = new ExtendedDeploymentResponse(raw);

        Assert.Equal(2, extended.Processes.Count);
        Assert.Empty(extended.Decisions);
    }

    #endregion

    #region DeployResourcesFromFilesAsync tests

    [Fact]
    public async Task DeployResourcesFromFilesAsync_ThrowsOnNullPaths()
    {
        var act = () => _client.DeployResourcesFromFilesAsync(null!);
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task DeployResourcesFromFilesAsync_ThrowsOnEmptyPaths()
    {
        var act = () => _client.DeployResourcesFromFilesAsync([]);
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task DeployResourcesFromFilesAsync_ThrowsOnMissingFile()
    {
        var act = () => _client.DeployResourcesFromFilesAsync(["/nonexistent/file.bpmn"]);
        await Assert.ThrowsAsync<FileNotFoundException>(act);
    }

    [Fact]
    public async Task DeployResourcesFromFilesAsync_DeploysFilesAndReturnsExtended()
    {
        var bpmnPath = CreateTempFile("test.bpmn", "<bpmn />");

        var response = new
        {
            deploymentKey = "100",
            tenantId = "<default>",
            deployments = new[]
            {
                new
                {
                    processDefinition = new
                    {
                        processDefinitionId = "test-process",
                        processDefinitionKey = "200",
                        processDefinitionVersion = 1,
                        resourceName = "test.bpmn",
                        tenantId = "<default>",
                    },
                },
            },
        };

        _handler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        var result = await _client.DeployResourcesFromFilesAsync([bpmnPath]);

        Assert.NotNull(result);
        Assert.Equal("100", result.DeploymentKey.ToString());
        Assert.Single(result.Processes);
        Assert.Equal("test-process", result.Processes[0].ProcessDefinitionId.ToString());

        Assert.Single(_handler.Requests);
        Assert.Contains("/deployments", _handler.Requests[0].RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task DeployResourcesFromFilesAsync_SendsTenantId()
    {
        var bpmnPath = CreateTempFile("test.bpmn", "<bpmn />");

        var response = new
        {
            deploymentKey = "100",
            tenantId = "my-tenant",
            deployments = Array.Empty<object>(),
        };

        string? capturedContent = null;
        _handler.Enqueue(async req =>
        {
            capturedContent = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response), System.Text.Encoding.UTF8, "application/json"),
            };
        });

        var result = await _client.DeployResourcesFromFilesAsync([bpmnPath], tenantId: "my-tenant");

        Assert.NotNull(result);
        Assert.Equal("my-tenant", result.TenantId.ToString());
        Assert.Contains("my-tenant", capturedContent);
    }

    [Fact]
    public async Task DeployResourcesFromFilesAsync_DeploysMultipleFiles()
    {
        var bpmnPath = CreateTempFile("process.bpmn", "<bpmn />");
        var dmnPath = CreateTempFile("decision.dmn", "<dmn />");
        var formPath = CreateTempFile("form.form", "{}");

        var response = new
        {
            deploymentKey = "100",
            tenantId = "<default>",
            deployments = new object[]
            {
                new
                {
                    processDefinition = new
                    {
                        processDefinitionId = "proc",
                        processDefinitionKey = "1",
                        processDefinitionVersion = 1,
                        resourceName = "process.bpmn",
                        tenantId = "<default>",
                    },
                },
                new
                {
                    decisionDefinition = new
                    {
                        decisionDefinitionId = "dec",
                        decisionDefinitionKey = "2",
                    },
                },
                new
                {
                    form = new
                    {
                        formId = "f1",
                        formKey = "3",
                    },
                },
            },
        };

        _handler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(response));

        var result = await _client.DeployResourcesFromFilesAsync([bpmnPath, dmnPath, formPath]);

        Assert.Single(result.Processes);
        Assert.Single(result.Decisions);
        Assert.Single(result.Forms);
    }

    [Fact]
    public async Task DeployResourcesFromFilesAsync_ThrowsOnEmptyFilePath()
    {
        var act = () => _client.DeployResourcesFromFilesAsync(["", "test.bpmn"]);
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    #endregion
}
