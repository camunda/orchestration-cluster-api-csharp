using System.Text.Json;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Unit tests for the JobWorker, ActivatedJob, JobWorkerConfig, and exception types.
/// These tests validate configuration, job deserialization, typed variable access,
/// exception semantics, and worker lifecycle — without a live Camunda server.
/// </summary>
public class JobWorkerTests
{
    // ---- Sample DTOs ----

    private static readonly List<string> _fetchVariables = ["orderId", "amount"];

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

        Assert.NotNull(vars);
        Assert.Equal("ord-1", vars!.OrderId);
        Assert.Equal(42.5m, vars.Amount);
    }

    [Fact]
    public void ActivatedJob_GetCustomHeaders_DeserializesTypedDto()
    {
        var job = CreateTestJob(
            customHeaders: """{"region":"us-east","priority":7}""");

        var headers = job.GetCustomHeaders<TaskHeaders>();

        Assert.NotNull(headers);
        Assert.Equal("us-east", headers!.Region);
        Assert.Equal(7, headers.Priority);
    }

    [Fact]
    public void ActivatedJob_GetVariables_AsDictionary()
    {
        var job = CreateTestJob(variables: """{"key1":"val1","key2":42}""");

        var dict = job.GetVariables<Dictionary<string, JsonElement>>();

        Assert.True(dict!.ContainsKey("key1"));
        Assert.Equal("val1", dict["key1"].GetString());
        Assert.Equal(42, dict["key2"].GetInt32());
    }

    [Fact]
    public void ActivatedJob_ExposesAllProperties()
    {
        var job = CreateTestJob();

        Assert.Equal("test-type", job.Type);
        Assert.Equal("test-worker", job.Worker);
        Assert.Equal(3, job.Retries);
        Assert.Equal(1700000000000, job.Deadline);
        Assert.Equal(JobKindEnum.BPMNELEMENT, job.Kind);
    }

    // ---- JobWorkerConfig ----

    [Fact]
    public void JobWorkerConfig_Defaults()
    {
        var config = new JobWorkerConfig
        {
            JobType = "my-type",
        };

        Assert.Null(config.MaxConcurrentJobs);
        Assert.Null(config.JobTimeoutMs);
        Assert.Equal(500, config.PollIntervalMs);
        Assert.Null(config.PollTimeoutMs);
        Assert.Null(config.FetchVariables);
        Assert.Null(config.WorkerName);
        Assert.True(config.AutoStart);
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
            FetchVariables = _fetchVariables,
            WorkerName = "payment-worker-1",
            AutoStart = false,
        };

        Assert.Equal("payment", config.JobType);
        Assert.Equal(60000, config.JobTimeoutMs);
        Assert.Equal(32, config.MaxConcurrentJobs);
        Assert.Equal(1000, config.PollIntervalMs);
        Assert.Equal(10000, config.PollTimeoutMs);
        Assert.Equivalent(_fetchVariables, config.FetchVariables);
        Assert.Equal("payment-worker-1", config.WorkerName);
        Assert.False(config.AutoStart);
    }

    // ---- BpmnErrorException ----

    [Fact]
    public void BpmnErrorException_CapuresErrorCode()
    {
        var ex = new BpmnErrorException("INVALID_ORDER", "Order not found");

        Assert.Equal("INVALID_ORDER", ex.ErrorCode);
        Assert.Equal("Order not found", ex.ErrorMessage);
        Assert.Null(ex.Variables);
        Assert.Equal("Order not found", ex.Message);
    }

    [Fact]
    public void BpmnErrorException_WithVariables()
    {
        var vars = new { reason = "expired" };
        var ex = new BpmnErrorException("EXPIRED", variables: vars);

        Assert.Equal("EXPIRED", ex.ErrorCode);
        Assert.Equal(vars, ex.Variables);
    }

    [Fact]
    public void BpmnErrorException_DefaultMessage()
    {
        var ex = new BpmnErrorException("MY_CODE");

        Assert.Equal("BPMN error: MY_CODE", ex.Message);
    }

    // ---- JobFailureException ----

    [Fact]
    public void JobFailureException_WithRetries()
    {
        var ex = new JobFailureException("Transient error", retries: 2, retryBackOffMs: 5000);

        Assert.Equal("Transient error", ex.Message);
        Assert.Equal(2, ex.Retries);
        Assert.Equal(5000, ex.RetryBackOffMs);
    }

    [Fact]
    public void JobFailureException_NoRetries()
    {
        var ex = new JobFailureException("Fatal error", retries: 0);

        Assert.Equal(0, ex.Retries);
        Assert.Null(ex.RetryBackOffMs);
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

        var worker = client.CreateJobWorker(config, (job, ct) => Task.FromResult<object?>(null));

        Assert.Single(client.GetWorkers());
        Assert.Same(worker, client.GetWorkers()[0]);
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

        var worker = client.CreateJobWorker(config, (job, ct) => Task.FromResult<object?>(null));

        Assert.StartsWith("worker-payment-", worker.Name);
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

        var worker = client.CreateJobWorker(config, (job, ct) => Task.FromResult<object?>(null));

        Assert.Equal("my-custom-worker", worker.Name);
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

        var worker = client.CreateJobWorker(config, (job, ct) => Task.FromResult<object?>(null));

        Assert.False(worker.IsRunning);
        Assert.Equal(0, worker.ActiveJobs);
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

        var w1 = client.CreateJobWorker(config, (job, ct) => Task.FromResult<object?>(null));
        var w2 = client.CreateJobWorker(config, (job, ct) => Task.FromResult<object?>(null));

        // Start them
        w1.Start();
        w2.Start();
        Assert.True(w1.IsRunning);

        // Stop all
        await client.StopAllWorkersAsync(TimeSpan.FromSeconds(1));

        // Allow poll loop to complete
        await Task.Delay(100);
        Assert.False(w1.IsRunning);
        Assert.False(w2.IsRunning);
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

        Assert.NotNull(worker);
        Assert.Single(client.GetWorkers());
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

        var worker = client.CreateJobWorker(config, (job, ct) => Task.FromResult<object?>(null));
        worker.Start();
        Assert.True(worker.IsRunning);

        await worker.DisposeAsync();
        await Task.Delay(100);

        Assert.False(worker.IsRunning);
    }

    // ---- JobCompletionRequest detection ----

    [Fact]
    public void Handler_Returning_JobCompletionRequest_Is_Forwarded_AsIs()
    {
        // Mirrors the pattern in ExecuteJobAsync: when the handler returns a
        // JobCompletionRequest, it should be forwarded directly (not wrapped).
        var corrections = new JobResultCorrections
        {
            Assignee = "new-assignee",
            Priority = 75,
            CandidateGroups = new List<string> { "managers" },
        };
        var completionRequest = new JobCompletionRequest
        {
            Variables = new { approved = true },
            Result = new JobResultUserTask
            {
                Corrections = corrections,
            },
        };
        object? result = completionRequest;

        var actual = result is JobCompletionRequest req
            ? req
            : new JobCompletionRequest { Variables = result };

        Assert.Same(completionRequest, actual);
        Assert.IsType<JobResultUserTask>(actual.Result);

        var userTask = (JobResultUserTask)actual.Result!;
        Assert.Equal("new-assignee", userTask.Corrections!.Assignee);
        Assert.Equal(75, userTask.Corrections.Priority);
        Assert.Equal("managers", Assert.Single(userTask.Corrections.CandidateGroups!));
    }

    [Fact]
    public void Handler_Returning_PlainObject_Is_Wrapped_As_Variables()
    {
        // When the handler returns a plain object (not JobCompletionRequest),
        // it should be wrapped in a new JobCompletionRequest as Variables.
        var output = new OrderOutput(true, "INV-001");
        object? result = output;

        var actual = result is JobCompletionRequest req
            ? req
            : new JobCompletionRequest { Variables = result };

        Assert.Same(output, actual.Variables);
        Assert.Null(actual.Result);
    }

    [Fact]
    public void Handler_Returning_Null_Is_Wrapped_As_Empty_Variables()
    {
        object? result = null;

        var actual = result is JobCompletionRequest req
            ? req
            : new JobCompletionRequest { Variables = result };

        Assert.Null(actual.Variables);
        Assert.Null(actual.Result);
    }

    [Fact]
    public void Handler_Returning_Denial_Is_Forwarded()
    {
        var completionRequest = new JobCompletionRequest
        {
            Result = new JobResultUserTask
            {
                Denied = true,
                DeniedReason = "Missing required fields",
            },
        };
        object? result = completionRequest;

        var actual = result is JobCompletionRequest req
            ? req
            : new JobCompletionRequest { Variables = result };

        Assert.Same(completionRequest, actual);
        var userTask = (JobResultUserTask)actual.Result!;
        Assert.True(userTask.Denied);
        Assert.Equal("Missing required fields", userTask.DeniedReason);
    }

    // ---- Helpers ----

    private static readonly JsonSerializerOptions s_testJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new TolerantEnumConverterFactory(),
            new CamundaKeyJsonConverterFactory(),
            new CamundaLongKeyJsonConverterFactory(),
        },
    };

    private static ActivatedJob CreateTestJob(
        string? variables = null,
        string? customHeaders = null)
    {

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

        var raw = JsonSerializer.Deserialize<ActivatedJobResult>(jobJson, s_testJsonOptions)!;
        return new ActivatedJob(raw);
    }

    private static CamundaClient CreateTestClient()
    {
        return CamundaClient.Create(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "http://localhost:8080/v2",
                ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
            },
        });
    }
}
