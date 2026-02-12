using System.Text.Json;
using Camunda.Client.Api;
using Camunda.Client.Runtime;
using FluentAssertions;

namespace Camunda.Client.Tests;

/// <summary>
/// Unit tests for the JobWorker, ActivatedJob, JobWorkerConfig, and exception types.
/// These tests validate configuration, job deserialization, typed variable access,
/// exception semantics, and worker lifecycle — without a live Camunda server.
/// </summary>
public class JobWorkerTests
{
    // ---- Sample DTOs ----

    public record OrderInput(string OrderId, decimal Amount);
    public record OrderOutput(bool Processed, string InvoiceNumber);
    public record TaskHeaders(string Region, int Priority);

    // ---- ActivatedJob typed access ----

    [Fact]
    public void ActivatedJob_GetVariables_DeserializesTypedDto()
    {
        var job = CreateTestJob(
            variables: """{"orderId":"ord-1","amount":42.5}""",
            customHeaders: """{"region":"eu-west","priority":3}""");

        var vars = job.GetVariables<OrderInput>();

        vars.Should().NotBeNull();
        vars!.OrderId.Should().Be("ord-1");
        vars.Amount.Should().Be(42.5m);
    }

    [Fact]
    public void ActivatedJob_GetCustomHeaders_DeserializesTypedDto()
    {
        var job = CreateTestJob(
            customHeaders: """{"region":"us-east","priority":7}""");

        var headers = job.GetCustomHeaders<TaskHeaders>();

        headers.Should().NotBeNull();
        headers!.Region.Should().Be("us-east");
        headers.Priority.Should().Be(7);
    }

    [Fact]
    public void ActivatedJob_GetVariables_AsDictionary()
    {
        var job = CreateTestJob(variables: """{"key1":"val1","key2":42}""");

        var dict = job.GetVariables<Dictionary<string, JsonElement>>();

        dict.Should().ContainKey("key1");
        dict!["key1"].GetString().Should().Be("val1");
        dict["key2"].GetInt32().Should().Be(42);
    }

    [Fact]
    public void ActivatedJob_ExposesAllProperties()
    {
        var job = CreateTestJob();

        job.Type.Should().Be("test-type");
        job.Worker.Should().Be("test-worker");
        job.Retries.Should().Be(3);
        job.Deadline.Should().Be(1700000000000);
        job.Kind.Should().Be(JobKindEnum.BPMNELEMENT);
    }

    // ---- JobWorkerConfig ----

    [Fact]
    public void JobWorkerConfig_Defaults()
    {
        var config = new JobWorkerConfig
        {
            JobType = "my-type",
            JobTimeoutMs = 30000,
        };

        config.MaxConcurrentJobs.Should().Be(10);
        config.PollIntervalMs.Should().Be(500);
        config.PollTimeoutMs.Should().BeNull();
        config.FetchVariables.Should().BeNull();
        config.WorkerName.Should().BeNull();
        config.AutoStart.Should().BeTrue();
    }

    [Fact]
    public void JobWorkerConfig_CustomValues()
    {
        var config = new JobWorkerConfig
        {
            JobType = "payment",
            JobTimeoutMs = 60000,
            MaxConcurrentJobs = 32,
            PollIntervalMs = 1000,
            PollTimeoutMs = 10000,
            FetchVariables = ["orderId", "amount"],
            WorkerName = "payment-worker-1",
            AutoStart = false,
        };

        config.JobType.Should().Be("payment");
        config.JobTimeoutMs.Should().Be(60000);
        config.MaxConcurrentJobs.Should().Be(32);
        config.PollIntervalMs.Should().Be(1000);
        config.PollTimeoutMs.Should().Be(10000);
        config.FetchVariables.Should().BeEquivalentTo(["orderId", "amount"]);
        config.WorkerName.Should().Be("payment-worker-1");
        config.AutoStart.Should().BeFalse();
    }

    // ---- BpmnErrorException ----

    [Fact]
    public void BpmnErrorException_CapuresErrorCode()
    {
        var ex = new BpmnErrorException("INVALID_ORDER", "Order not found");

        ex.ErrorCode.Should().Be("INVALID_ORDER");
        ex.ErrorMessage.Should().Be("Order not found");
        ex.Variables.Should().BeNull();
        ex.Message.Should().Be("Order not found");
    }

    [Fact]
    public void BpmnErrorException_WithVariables()
    {
        var vars = new { reason = "expired" };
        var ex = new BpmnErrorException("EXPIRED", variables: vars);

        ex.ErrorCode.Should().Be("EXPIRED");
        ex.Variables.Should().Be(vars);
    }

    [Fact]
    public void BpmnErrorException_DefaultMessage()
    {
        var ex = new BpmnErrorException("MY_CODE");

        ex.Message.Should().Be("BPMN error: MY_CODE");
    }

    // ---- JobFailureException ----

    [Fact]
    public void JobFailureException_WithRetries()
    {
        var ex = new JobFailureException("Transient error", retries: 2, retryBackOffMs: 5000);

        ex.Message.Should().Be("Transient error");
        ex.Retries.Should().Be(2);
        ex.RetryBackOffMs.Should().Be(5000);
    }

    [Fact]
    public void JobFailureException_NoRetries()
    {
        var ex = new JobFailureException("Fatal error", retries: 0);

        ex.Retries.Should().Be(0);
        ex.RetryBackOffMs.Should().BeNull();
    }

    // ---- Worker lifecycle (without server) ----

    [Fact]
    public void CreateJobWorker_RegistersWorker()
    {
        using var client = CreateTestClient();
        var config = new JobWorkerConfig
        {
            JobType = "test",
            JobTimeoutMs = 30000,
            AutoStart = false,
        };

        var worker = client.CreateJobWorker(config, async (job, ct) => null);

        client.GetWorkers().Should().ContainSingle();
        client.GetWorkers()[0].Should().BeSameAs(worker);
    }

    [Fact]
    public void CreateJobWorker_AutoGeneratesName()
    {
        using var client = CreateTestClient();
        var config = new JobWorkerConfig
        {
            JobType = "payment",
            JobTimeoutMs = 30000,
            AutoStart = false,
        };

        var worker = client.CreateJobWorker(config, async (job, ct) => null);

        worker.Name.Should().StartWith("worker-payment-");
    }

    [Fact]
    public void CreateJobWorker_UsesCustomName()
    {
        using var client = CreateTestClient();
        var config = new JobWorkerConfig
        {
            JobType = "payment",
            JobTimeoutMs = 30000,
            WorkerName = "my-custom-worker",
            AutoStart = false,
        };

        var worker = client.CreateJobWorker(config, async (job, ct) => null);

        worker.Name.Should().Be("my-custom-worker");
    }

    [Fact]
    public void CreateJobWorker_NoAutoStart_IsNotRunning()
    {
        using var client = CreateTestClient();
        var config = new JobWorkerConfig
        {
            JobType = "test",
            JobTimeoutMs = 30000,
            AutoStart = false,
        };

        var worker = client.CreateJobWorker(config, async (job, ct) => null);

        worker.IsRunning.Should().BeFalse();
        worker.ActiveJobs.Should().Be(0);
    }

    [Fact]
    public async Task StopAllWorkers_StopsAllRegistered()
    {
        using var client = CreateTestClient();
        var config = new JobWorkerConfig
        {
            JobType = "test",
            JobTimeoutMs = 30000,
            AutoStart = false,
        };

        var w1 = client.CreateJobWorker(config, async (job, ct) => null);
        var w2 = client.CreateJobWorker(config, async (job, ct) => null);

        // Start them
        w1.Start();
        w2.Start();
        w1.IsRunning.Should().BeTrue();

        // Stop all
        await client.StopAllWorkersAsync(TimeSpan.FromSeconds(1));

        // Allow poll loop to complete
        await Task.Delay(100);
        w1.IsRunning.Should().BeFalse();
        w2.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void CreateJobWorker_VoidHandler_Overload()
    {
        using var client = CreateTestClient();
        var config = new JobWorkerConfig
        {
            JobType = "test",
            JobTimeoutMs = 30000,
            AutoStart = false,
        };

        // Void handler overload — no return value needed
        var worker = client.CreateJobWorker(config, async (job, ct) =>
        {
            // side-effect only, no return
            await Task.CompletedTask;
        });

        worker.Should().NotBeNull();
        client.GetWorkers().Should().ContainSingle();
    }

    [Fact]
    public async Task Worker_Dispose_StopsGracefully()
    {
        using var client = CreateTestClient();
        var config = new JobWorkerConfig
        {
            JobType = "test",
            JobTimeoutMs = 30000,
            AutoStart = false,
        };

        var worker = client.CreateJobWorker(config, async (job, ct) => null);
        worker.Start();
        worker.IsRunning.Should().BeTrue();

        await worker.DisposeAsync();
        await Task.Delay(100);

        worker.IsRunning.Should().BeFalse();
    }

    // ---- Helpers ----

    private static ActivatedJob CreateTestJob(
        string? variables = null,
        string? customHeaders = null)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new Runtime.TolerantEnumConverterFactory(),
                new Runtime.CamundaKeyJsonConverterFactory(),
                new Runtime.CamundaLongKeyJsonConverterFactory(),
            },
        };

        var jobJson = $$"""
        {
            "type": "test-type",
            "processDefinitionId": "test-process",
            "processDefinitionVersion": 1,
            "elementId": "task-1",
            "customHeaders": {{customHeaders ?? "{}"}},
            "worker": "test-worker",
            "retries": 3,
            "deadline": 1700000000000,
            "variables": {{variables ?? "{}"}},
            "tenantId": "<default>",
            "jobKey": "123456",
            "processInstanceKey": "789012",
            "processDefinitionKey": "345678",
            "elementInstanceKey": "901234",
            "kind": "BPMN_ELEMENT",
            "listenerEventType": "UNSPECIFIED"
        }
        """;

        var raw = JsonSerializer.Deserialize<ActivatedJobResult>(jobJson, jsonOptions)!;
        return new ActivatedJob(raw);
    }

    private static CamundaClient CreateTestClient()
    {
        return Camunda.CreateClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "http://localhost:26500/v2",
                ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
            },
        });
    }
}
