// Compilable usage examples from README.md.
// Region tags wrap ONLY the lines that appear in the README.
// The sync-readme-snippets.py script extracts these regions and injects
// them into README.md code blocks marked with <!-- snippet:RegionName -->.
#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CS8602 // Dereference of a possibly null reference
#pragma warning disable CS8604 // Possible null reference argument
#pragma warning disable CA1861 // Constant array arguments
#pragma warning disable CA1852 // Type can be sealed — example types shown in README

// <UsingDirective>
using Camunda.Orchestration.Sdk;
// </UsingDirective>
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk.Examples;

internal static class ReadmeExamples
{
    private static async Task QuickStartExample()
    {
        // <QuickStart>
        // Zero-config construction: reads CAMUNDA_* from environment variables.
        // If no configuration is present, defaults to Camunda 8 Run on localhost.
        using var client = CamundaClient.Create();

        var topology = await client.GetTopologyAsync();
        Console.WriteLine($"Brokers: {topology.Brokers?.Count ?? 0}");
        // </QuickStart>
    }

    private static void ProgrammaticOverridesExample()
    {
        // <ProgrammaticOverrides>
        using var client = CamundaClient.Create(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://my-cluster.camunda.io",
                ["CAMUNDA_AUTH_STRATEGY"] = "OAUTH",
                ["CAMUNDA_CLIENT_ID"] = "my-client-id",
                ["CAMUNDA_CLIENT_SECRET"] = "my-secret",
                ["CAMUNDA_OAUTH_URL"] = "https://login.cloud.camunda.io/oauth/token",
                ["CAMUNDA_TOKEN_AUDIENCE"] = "zeebe.camunda.io",
            },
        });
        // </ProgrammaticOverrides>
    }

    private static void AppSettingsConfigExample(string[] args)
    {
        // <AppSettingsConfig>
        var builder = WebApplication.CreateBuilder(args);

        using var client = CamundaClient.Create(new CamundaOptions
        {
            Configuration = builder.Configuration.GetSection("Camunda"),
        });
        // </AppSettingsConfig>
    }

    private static void DIZeroConfigExample(string[] args)
    {
        // <DIZeroConfig>
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddCamundaClient();
        // </DIZeroConfig>
    }

    private static void DIAppSettingsExample(string[] args)
    {
        // <DIAppSettings>
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddCamundaClient(builder.Configuration.GetSection("Camunda"));
        // </DIAppSettings>
    }

    private static void DIOptionsCallbackExample(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        // <DIOptionsCallback>
        builder.Services.AddCamundaClient(options =>
        {
            options.Configuration = builder.Configuration.GetSection("Camunda");
            // or: options.Config = new Dictionary<string, string> { ... };
        });
        // </DIOptionsCallback>
    }

    // <DIControllerInjection>
    public class OrderController(CamundaClient camunda) : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> StartProcess()
        {
            var result = await camunda.CreateProcessInstanceAsync(
                new ProcessInstanceCreationInstructionById
                {
                    ProcessDefinitionId = ProcessDefinitionId.AssumeExists("order-process"),
                });
            return Ok(result);
        }
    }
    // </DIControllerInjection>

    private static void CustomHttpClientExample()
    {
        // <CustomHttpClient>
        var httpClient = new HttpClient { BaseAddress = new Uri("https://my-cluster/v2/") };
        using var client = CamundaClient.Create(new CamundaOptions
        {
            HttpClient = httpClient,
        });
        // </CustomHttpClient>
    }

    private static void BackpressureStateExample()
    {
        using var client = CamundaClient.Create();

        // <BackpressureState>
        var state = client.GetBackpressureState();
        // state.Severity: "healthy", "soft", or "severe"
        // state.Consecutive: consecutive backpressure signals observed
        // state.PermitsMax: current concurrency cap (null when LEGACY / not engaged)
        // </BackpressureState>
    }

    private static void InjectLoggerExample()
    {
        // <InjectLogger>
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Debug);
        });

        using var client = CamundaClient.Create(new CamundaOptions
        {
            LoggerFactory = loggerFactory,
        });
        // </InjectLogger>
    }

    private static void DILoggingExample(string[] args)
    {
        // <DILogging>
        var builder = WebApplication.CreateBuilder(args);

        // Logging configuration
        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        // SDK automatically uses the host's ILoggerFactory
        builder.Services.AddCamundaClient(builder.Configuration.GetSection("Camunda"));
        // </DILogging>
    }

    private static async Task DomainKeysExample()
    {
        using var client = CamundaClient.Create();

        // <DomainKeys>
        // Lift a raw value into the correct nominal type
        var defKey = ProcessDefinitionKey.AssumeExists("2251799813686749");

        // Type safety — compiler prevents mixing key types
        var taskKey = UserTaskKey.AssumeExists("123456");
        // await client.GetProcessDefinitionAsync(taskKey); // ← compile error

        // Validation — constraints (pattern, length) checked at construction
        ProcessDefinitionKey.IsValid("2251799813686749"); // true

        // Values returned from API calls are already typed
        var result = await client.GetProcessDefinitionAsync(defKey);
        // result.ProcessDefinitionKey is ProcessDefinitionKey, not string

        // Transparent JSON serialization — no special handling needed
        // </DomainKeys>
    }

    private static async Task DeployResourcesExample()
    {
        // <DeployResources>
        using var client = CamundaClient.Create();

        var result = await client.DeployResourcesFromFilesAsync(["process.bpmn", "decision.dmn"]);

        Console.WriteLine($"Deployment key: {result.DeploymentKey}");
        foreach (var process in result.Processes)
        {
            Console.WriteLine($"  Process: {process.ProcessDefinitionId} (key: {process.ProcessDefinitionKey})");
        }
        // </DeployResources>
    }

    private static async Task CreateProcessInstanceExample()
    {
        // <ReadmeCreateProcessInstance>
        using var client = CamundaClient.Create();

        var deployment = await client.DeployResourcesFromFilesAsync(["process.bpmn"]);
        var processKey = deployment.Processes[0].ProcessDefinitionKey;

        var result = await client.CreateProcessInstanceAsync(
            new ProcessInstanceCreationInstructionByKey
            {
                ProcessDefinitionKey = processKey,
            });

        Console.WriteLine($"Process instance key: {result.ProcessInstanceKey}");
        // </ReadmeCreateProcessInstance>
    }

    private static async Task CreateProcessFromStorageExample()
    {
        // <CreateProcessFromStorage>
        using var client = CamundaClient.Create();

        var storedKey = "2251799813685249"; // from a DB row or config
        var result = await client.CreateProcessInstanceAsync(
            new ProcessInstanceCreationInstructionByKey
            {
                ProcessDefinitionKey = ProcessDefinitionKey.AssumeExists(storedKey),
            });

        Console.WriteLine($"Process instance key: {result.ProcessInstanceKey}");
        // </CreateProcessFromStorage>
    }

    private static async Task CreateProcessByIdExample()
    {
        using var client = CamundaClient.Create();

        // <CreateProcessById>
        var result = await client.CreateProcessInstanceAsync(
            new ProcessInstanceCreationInstructionById
            {
                ProcessDefinitionId = ProcessDefinitionId.AssumeExists("my-process-id"),
            });
        // </CreateProcessById>
    }

    // <SendingVariables>
    // Define your application domain models
    public record OrderInput(string OrderId, decimal Amount);
    // </SendingVariables>

    private static async Task SendingVariablesExample(ProcessDefinitionId processDefinitionId, JobKey jobKey)
    {
        using var client = CamundaClient.Create();

        // <SendingVariablesBody>
        // Assign the DTO directly
        await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = processDefinitionId,
            Variables = new OrderInput("ord-123", 99.99m),
        });

        // Dictionaries also work — no DTO required
        await client.CompleteJobAsync(jobKey, new JobCompletionRequest
        {
            Variables = new Dictionary<string, object> { ["processed"] = true },
        });
        // </SendingVariablesBody>
    }

    // <ReceivingVariables>
    public record OrderResult(bool Processed, string InvoiceNumber);
    // </ReceivingVariables>

    private static async Task ReceivingVariablesExample(ProcessDefinitionId processDefinitionId)
    {
        using var client = CamundaClient.Create();

        // <ReceivingVariablesBody>
        // Deserialize variables from any API response
        var result = await client.CreateProcessInstanceAsync(
            new ProcessInstanceCreationInstructionById
            {
                ProcessDefinitionId = processDefinitionId,
            });
        var output = result.Variables.DeserializeAs<OrderResult>();
        // output.Processed, output.InvoiceNumber — fully typed
        // </ReceivingVariablesBody>
    }

    // <BasicWorker>
    // Define input/output DTOs
    public record OrderOutput(bool Processed, string InvoiceNumber);
    // </BasicWorker>

    private static async Task BasicWorkerExample()
    {
        // <BasicWorkerBody>
        using var client = CamundaClient.Create();

        client.CreateJobWorker(
            new JobWorkerConfig
            {
                JobType = "process-order",
                JobTimeoutMs = 30_000,
            },
            async (job, ct) =>
            {
                var input = job.GetVariables<OrderInput>();
                var invoice = await ProcessOrder(input!, ct);

                // Return value auto-completes the job with these output variables
                return new OrderOutput(true, invoice);
            });

        // Block until Ctrl+C
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        await client.RunWorkersAsync(ct: cts.Token);
        // </BasicWorkerBody>
    }

    private static Task<string> ProcessOrder(OrderInput input, CancellationToken ct) =>
        Task.FromResult("INV-001");

    private static void ErrorHandlingExample(bool isBpmnError)
    {
        // <ErrorHandling>
        // BPMN error — caught by error boundary events in the process model
        throw new BpmnErrorException("INVALID_ORDER", "Order not found");
        // </ErrorHandling>
    }

    private static void FailureExample()
    {
        // <ErrorHandlingFailure>
        // Explicit failure with retry control
        throw new JobFailureException("Service unavailable", retries: 2, retryBackOffMs: 5000);
        // </ErrorHandlingFailure>
    }

    // <VoidHandler>
    public record NotificationInput(string Message);
    // </VoidHandler>

    private static void VoidHandlerExample()
    {
        using var client = CamundaClient.Create();
        var config = new JobWorkerConfig { JobType = "send-notification", JobTimeoutMs = 30_000 };

        // <VoidHandlerBody>
        client.CreateJobWorker(config, async (job, ct) =>
        {
            await SendNotification(job.GetVariables<NotificationInput>()!, ct);
            // Auto-completes with no variables
        });
        // </VoidHandlerBody>
    }

    private static Task SendNotification(NotificationInput input, CancellationToken ct) =>
        Task.CompletedTask;

    private static void JobCorrectionsExample()
    {
        using var client = CamundaClient.Create();
        var config = new JobWorkerConfig { JobType = "validate-task", JobTimeoutMs = 30_000 };

        // <JobCorrections>
        client.CreateJobWorker(config, async (job, ct) =>
        {
            // Apply corrections to the user task
            return new JobCompletionRequest
            {
                Variables = new { reviewed = true },
                Result = new JobResultUserTask
                {
                    Corrections = new JobResultCorrections
                    {
                        Assignee = "new-assignee",
                        Priority = 75,
                        CandidateGroups = new List<string> { "managers" },
                    },
                },
            };
        });
        // </JobCorrections>
    }

    private static void JobCorrectionsDeniedExample()
    {
        using var client = CamundaClient.Create();
        var config = new JobWorkerConfig { JobType = "review-task", JobTimeoutMs = 30_000 };

        // <JobCorrectionsDenied>
        client.CreateJobWorker(config, async (job, ct) =>
        {
            return new JobCompletionRequest
            {
                Result = new JobResultUserTask
                {
                    Denied = true,
                    DeniedReason = "Missing required fields",
                },
            };
        });
        // </JobCorrectionsDenied>
    }

    private static void WorkerDefaultsEnvExample()
    {
        using var client = CamundaClient.Create();

        // <WorkerDefaultsEnv>
        // Workers inherit timeout, concurrency, and name from environment
        client.CreateJobWorker(
            new JobWorkerConfig { JobType = "validate-order" },
            async (job, ct) => null);

        client.CreateJobWorker(
            new JobWorkerConfig { JobType = "ship-order" },
            async (job, ct) => null);

        // Per-worker override: this worker uses 32 concurrent jobs instead of the global 8
        client.CreateJobWorker(
            new JobWorkerConfig { JobType = "bulk-import", MaxConcurrentJobs = 32 },
            async (job, ct) => null);
        // </WorkerDefaultsEnv>
    }

    private static void WorkerDefaultsClientExample()
    {
        // <WorkerDefaultsClient>
        var client = CamundaClient.Create(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_WORKER_TIMEOUT"] = "30000",
                ["CAMUNDA_WORKER_MAX_CONCURRENT_JOBS"] = "8",
            },
        });
        // </WorkerDefaultsClient>
    }
}
