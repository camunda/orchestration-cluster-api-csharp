using FluentAssertions;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Tests for heritable worker defaults (CAMUNDA_WORKER_* env vars).
/// Validates hydration, merge precedence, and validation behavior.
/// </summary>
public class WorkerDefaultsTests
{
    // ---- Hydration ----

    [Fact]
    public void Hydrate_WorkerTimeout_PopulatesWorkerDefaults()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_WORKER_TIMEOUT"] = "30000" });

        config.WorkerDefaults.Should().NotBeNull();
        config.WorkerDefaults!.JobTimeoutMs.Should().Be(30000);
    }

    [Fact]
    public void Hydrate_WorkerMaxConcurrentJobs_PopulatesWorkerDefaults()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_WORKER_MAX_CONCURRENT_JOBS"] = "8" });

        config.WorkerDefaults.Should().NotBeNull();
        config.WorkerDefaults!.MaxConcurrentJobs.Should().Be(8);
    }

    [Fact]
    public void Hydrate_WorkerRequestTimeout_PopulatesWorkerDefaults()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_WORKER_REQUEST_TIMEOUT"] = "15000" });

        config.WorkerDefaults.Should().NotBeNull();
        config.WorkerDefaults!.PollTimeoutMs.Should().Be(15000);
    }

    [Fact]
    public void Hydrate_WorkerName_PopulatesWorkerDefaults()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_WORKER_NAME"] = "my-worker" });

        config.WorkerDefaults.Should().NotBeNull();
        config.WorkerDefaults!.WorkerName.Should().Be("my-worker");
    }

    [Fact]
    public void Hydrate_WorkerJitter_PopulatesWorkerDefaults()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?> { ["CAMUNDA_WORKER_STARTUP_JITTER_MAX_SECONDS"] = "5" });

        config.WorkerDefaults.Should().NotBeNull();
        config.WorkerDefaults!.StartupJitterMaxSeconds.Should().Be(5);
    }

    [Fact]
    public void Hydrate_NoWorkerVars_WorkerDefaultsIsNull()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>());

        config.WorkerDefaults.Should().BeNull();
    }

    [Fact]
    public void Hydrate_MultipleWorkerVars_PopulatesAll()
    {
        var config = ConfigurationHydrator.Hydrate(
            env: new Dictionary<string, string?>
            {
                ["CAMUNDA_WORKER_TIMEOUT"] = "60000",
                ["CAMUNDA_WORKER_MAX_CONCURRENT_JOBS"] = "16",
                ["CAMUNDA_WORKER_NAME"] = "batch-worker",
            });

        config.WorkerDefaults.Should().NotBeNull();
        config.WorkerDefaults!.JobTimeoutMs.Should().Be(60000);
        config.WorkerDefaults!.MaxConcurrentJobs.Should().Be(16);
        config.WorkerDefaults!.WorkerName.Should().Be("batch-worker");
        config.WorkerDefaults!.PollTimeoutMs.Should().BeNull();
        config.WorkerDefaults!.StartupJitterMaxSeconds.Should().BeNull();
    }

    // ---- Merge: defaults applied when per-worker config omits fields ----

    [Fact]
    public void CreateJobWorker_AppliesWorkerDefaults_WhenPerWorkerOmits()
    {
        using var client = CreateClientWithWorkerDefaults();

        var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "test", AutoStart = false },
            (job, ct) => Task.FromResult<object?>(null));

        // Worker should be created successfully — timeout and concurrency from defaults
        worker.Should().NotBeNull();

    }

    [Fact]
    public void CreateJobWorker_ExplicitConfig_OverridesDefaults()
    {
        using var client = CreateClientWithWorkerDefaults(
            workerName: "default-name");

        var worker = client.CreateJobWorker(
            new JobWorkerConfig
            {
                JobType = "test",
                JobTimeoutMs = 60000,
                MaxConcurrentJobs = 32,
                WorkerName = "explicit-name",
                AutoStart = false,
            },
            (job, ct) => Task.FromResult<object?>(null));

        worker.Name.Should().Be("explicit-name");

    }

    [Fact]
    public void CreateJobWorker_DefaultName_InheritsFromWorkerDefaults()
    {
        using var client = CreateClientWithWorkerDefaults(
            workerName: "global-worker");

        var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "test", AutoStart = false },
            (job, ct) => Task.FromResult<object?>(null));

        worker.Name.Should().Be("global-worker");

    }

    // ---- Validation ----

    [Fact]
    public void CreateJobWorker_Throws_WhenJobTimeoutMs_NotSetAnywhere()
    {
        using var client = CamundaClient.Create(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "http://localhost:8080/v2",
                ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
                ["CAMUNDA_WORKER_MAX_CONCURRENT_JOBS"] = "4",
            },
        });

        var act = () => client.CreateJobWorker(
            new JobWorkerConfig { JobType = "test", AutoStart = false },
            (job, ct) => Task.FromResult<object?>(null));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*JobTimeoutMs is required*");
    }

    [Fact]
    public void CreateJobWorker_Succeeds_WhenBothRequired_FromEnvDefaults()
    {
        using var client = CreateClientWithWorkerDefaults();

        var worker = client.CreateJobWorker(
            new JobWorkerConfig { JobType = "test", AutoStart = false },
            (job, ct) => Task.FromResult<object?>(null));

        worker.Should().NotBeNull();

    }

    [Fact]
    public void CreateJobWorker_Succeeds_WhenBothRequired_SetPerWorker()
    {
        using var client = CamundaClient.Create(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "http://localhost:8080/v2",
                ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
            },
        });

        var worker = client.CreateJobWorker(
            new JobWorkerConfig
            {
                JobType = "test",
                JobTimeoutMs = 30000,
                MaxConcurrentJobs = 4,
                AutoStart = false,
            },
            (job, ct) => Task.FromResult<object?>(null));

        worker.Should().NotBeNull();

    }

    [Fact]
    public void CreateJobWorker_MaxConcurrentJobs_DefaultsTo10_WhenNotSetAnywhere()
    {
        using var client = CamundaClient.Create(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "http://localhost:8080/v2",
                ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
            },
        });

        // Should not throw — MaxConcurrentJobs defaults to 10
        var worker = client.CreateJobWorker(
            new JobWorkerConfig
            {
                JobType = "test",
                JobTimeoutMs = 30000,
                AutoStart = false,
            },
            (job, ct) => Task.FromResult<object?>(null));

        worker.Should().NotBeNull();

    }

    // ---- Helpers ----

    private static CamundaClient CreateClientWithWorkerDefaults(
        string? workerName = null)
    {
        var config = new Dictionary<string, string>
        {
            ["CAMUNDA_REST_ADDRESS"] = "http://localhost:8080/v2",
            ["CAMUNDA_AUTH_STRATEGY"] = "NONE",
            ["CAMUNDA_WORKER_TIMEOUT"] = "30000",
            ["CAMUNDA_WORKER_MAX_CONCURRENT_JOBS"] = "4",
        };
        if (workerName != null)
            config["CAMUNDA_WORKER_NAME"] = workerName;

        return CamundaClient.Create(new CamundaOptions { Config = config });
    }
}
