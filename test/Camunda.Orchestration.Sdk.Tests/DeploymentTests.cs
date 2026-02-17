using System.Net;
using System.Text.Json;
using Camunda.Orchestration.Sdk.Api;
using Camunda.Orchestration.Sdk.Runtime;
using FluentAssertions;

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

        extended.DeploymentKey.ToString().Should().Be("123");
        extended.TenantId.ToString().Should().Be("test-tenant");
        extended.Deployments.Should().HaveCount(5);
        extended.Processes.Should().HaveCount(1);
        extended.Processes[0].ProcessDefinitionId.ToString().Should().Be("proc-1");
        extended.Decisions.Should().HaveCount(1);
        extended.Decisions[0].DecisionDefinitionId.ToString().Should().Be("dec-1");
        extended.DecisionRequirements.Should().HaveCount(1);
        extended.DecisionRequirements[0].DecisionRequirementsKey.ToString().Should().Be("3");
        extended.Forms.Should().HaveCount(1);
        extended.Forms[0].FormId.ToString().Should().Be("form-1");
        extended.Resources.Should().HaveCount(1);
        extended.Resources[0].ResourceId.Should().Be("res-1");
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

        extended.Processes.Should().BeEmpty();
        extended.Decisions.Should().BeEmpty();
        extended.DecisionRequirements.Should().BeEmpty();
        extended.Forms.Should().BeEmpty();
        extended.Resources.Should().BeEmpty();
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

        extended.Processes.Should().BeEmpty();
        extended.Decisions.Should().BeEmpty();
        extended.DecisionRequirements.Should().BeEmpty();
        extended.Forms.Should().BeEmpty();
        extended.Resources.Should().BeEmpty();
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
        extended.Raw.Should().BeSameAs(raw);
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

        extended.Processes.Should().HaveCount(2);
        extended.Decisions.Should().BeEmpty();
    }

    #endregion

    #region DeployResourcesFromFilesAsync tests

    [Fact]
    public async Task DeployResourcesFromFilesAsync_ThrowsOnNullPaths()
    {
        var act = () => _client.DeployResourcesFromFilesAsync(null!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeployResourcesFromFilesAsync_ThrowsOnEmptyPaths()
    {
        var act = () => _client.DeployResourcesFromFilesAsync([]);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeployResourcesFromFilesAsync_ThrowsOnMissingFile()
    {
        var act = () => _client.DeployResourcesFromFilesAsync(["/nonexistent/file.bpmn"]);
        await act.Should().ThrowAsync<FileNotFoundException>();
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

        result.Should().NotBeNull();
        result.DeploymentKey.ToString().Should().Be("100");
        result.Processes.Should().HaveCount(1);
        result.Processes[0].ProcessDefinitionId.ToString().Should().Be("test-process");

        _handler.Requests.Should().HaveCount(1);
        _handler.Requests[0].RequestUri!.PathAndQuery.Should().Contain("/deployments");
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

        result.Should().NotBeNull();
        result.TenantId.ToString().Should().Be("my-tenant");
        capturedContent.Should().Contain("my-tenant");
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

        result.Processes.Should().HaveCount(1);
        result.Decisions.Should().HaveCount(1);
        result.Forms.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeployResourcesFromFilesAsync_ThrowsOnEmptyFilePath()
    {
        var act = () => _client.DeployResourcesFromFilesAsync(["", "test.bpmn"]);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion
}
