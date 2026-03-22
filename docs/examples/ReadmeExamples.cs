// Compilable usage examples from README.md.
// Region tags are used to embed these snippets into the README.
#pragma warning disable CS8321 // Local function is declared but never used
#pragma warning disable CS8602 // Dereference of a possibly null reference
#pragma warning disable CS8604 // Possible null reference argument
#pragma warning disable CA1861 // Constant array arguments

using Camunda.Orchestration.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Camunda.Orchestration.Sdk.Examples;

internal static class ReadmeExamples
{
    // <QuickStart>
    private static async Task QuickStartExample()
    {
        using var client = CamundaClient.Create();

        var topology = await client.GetTopologyAsync();
        Console.WriteLine($"Brokers: {topology.Brokers?.Count ?? 0}");
    }
    // </QuickStart>

    // <ProgrammaticOverrides>
    private static void ProgrammaticOverridesExample()
    {
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
    }
    // </ProgrammaticOverrides>

    // <AppSettingsConfig>
    private static void AppSettingsConfigExample(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        using var client = CamundaClient.Create(new CamundaOptions
        {
            Configuration = builder.Configuration.GetSection("Camunda"),
        });
    }
    // </AppSettingsConfig>

    // <DIZeroConfig>
    private static void DIZeroConfigExample(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddCamundaClient();
    }
    // </DIZeroConfig>

    // <DIAppSettings>
    private static void DIAppSettingsExample(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddCamundaClient(builder.Configuration.GetSection("Camunda"));
    }
    // </DIAppSettings>

    // <DIOptionsCallback>
    private static void DIOptionsCallbackExample(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddCamundaClient(options =>
        {
            options.Configuration = builder.Configuration.GetSection("Camunda");
        });
    }
    // </DIOptionsCallback>

    // <DIControllerInjection>
    public class OrderController(CamundaClient camunda) : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        [Microsoft.AspNetCore.Mvc.HttpPost]
        public async Task<Microsoft.AspNetCore.Mvc.IActionResult> StartProcess()
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

    // <CustomHttpClient>
    private static void CustomHttpClientExample()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("https://my-cluster/v2/") };
        using var client = CamundaClient.Create(new CamundaOptions
        {
            HttpClient = httpClient,
        });
    }
    // </CustomHttpClient>

    // <BackpressureState>
    private static void BackpressureStateExample()
    {
        using var client = CamundaClient.Create();

        var state = client.GetBackpressureState();
        // state.Severity: "healthy", "soft", or "severe"
        // state.Consecutive: consecutive backpressure signals observed
        // state.PermitsMax: current concurrency cap (null when LEGACY / not engaged)
    }
    // </BackpressureState>

    // <InjectLogger>
    private static void InjectLoggerExample()
    {
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
    }
    // </InjectLogger>

    // <DILogging>
    private static void DILoggingExample(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        builder.Services.AddCamundaClient(builder.Configuration.GetSection("Camunda"));
    }
    // </DILogging>

    // <DomainKeys>
    private static async Task DomainKeysExample()
    {
        using var client = CamundaClient.Create();

        var defKey = ProcessDefinitionKey.AssumeExists("2251799813686749");

        var taskKey = UserTaskKey.AssumeExists("123456");
        // await client.GetProcessDefinitionAsync(taskKey); // ← compile error

        ProcessDefinitionKey.IsValid("2251799813686749"); // true

        var result = await client.GetProcessDefinitionAsync(defKey);
        // result.ProcessDefinitionKey is ProcessDefinitionKey, not string
    }
    // </DomainKeys>

    // <DeployResources>
    private static async Task DeployResourcesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.DeployResourcesFromFilesAsync(["process.bpmn", "decision.dmn"]);

        Console.WriteLine($"Deployment key: {result.DeploymentKey}");
        foreach (var process in result.Processes)
        {
            Console.WriteLine($"  Process: {process.ProcessDefinitionId} (key: {process.ProcessDefinitionKey})");
        }
    }
    // </DeployResources>

    // <CreateProcessInstance>
    private static async Task CreateProcessInstanceExample()
    {
        using var client = CamundaClient.Create();

        var deployment = await client.DeployResourcesFromFilesAsync(["process.bpmn"]);
        var processKey = deployment.Processes[0].ProcessDefinitionKey;

        var result = await client.CreateProcessInstanceAsync(
            new ProcessInstanceCreationInstructionByKey
            {
                ProcessDefinitionKey = processKey,
            });

        Console.WriteLine($"Process instance key: {result.ProcessInstanceKey}");
    }
    // </CreateProcessInstance>

    // <CreateProcessFromStorage>
    private static async Task CreateProcessFromStorageExample()
    {
        using var client = CamundaClient.Create();

        var storedKey = "2251799813685249";
        var result = await client.CreateProcessInstanceAsync(
            new ProcessInstanceCreationInstructionByKey
            {
                ProcessDefinitionKey = ProcessDefinitionKey.AssumeExists(storedKey),
            });

        Console.WriteLine($"Process instance key: {result.ProcessInstanceKey}");
    }
    // </CreateProcessFromStorage>

    // <CreateProcessById>
    private static async Task CreateProcessByIdExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateProcessInstanceAsync(
            new ProcessInstanceCreationInstructionById
            {
                ProcessDefinitionId = ProcessDefinitionId.AssumeExists("order-process"),
            });
    }
    // </CreateProcessById>

    // <SendingVariables>
    public record OrderInput(string OrderId, decimal Amount);

    private static async Task SendingVariablesExample()
    {
        using var client = CamundaClient.Create();
        var jobKey = JobKey.AssumeExists("1");

        await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
        {
            ProcessDefinitionId = ProcessDefinitionId.AssumeExists("order-process"),
            Variables = new OrderInput("ord-123", 99.99m),
        });

        await client.CompleteJobAsync(jobKey, new JobCompletionRequest
        {
            Variables = new Dictionary<string, object> { ["processed"] = true },
        });
    }
    // </SendingVariables>

    // <ReceivingVariables>
    public record OrderResult(bool Processed, string InvoiceNumber);
    public record JobHeaders(string Region, int Priority);

    private static async Task ReceivingVariablesExample()
    {
        using var client = CamundaClient.Create();

        var result = await client.CreateProcessInstanceAsync(
            new ProcessInstanceCreationInstructionById
            {
                ProcessDefinitionId = ProcessDefinitionId.AssumeExists("test"),
            });
        var output = result.Variables.DeserializeAs<OrderResult>();
    }
    // </ReceivingVariables>

    // <BasicWorker>
    public record OrderOutput(bool Processed, string InvoiceNumber);

    private static async Task BasicWorkerExample()
    {
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

                return new OrderOutput(true, invoice);
            });

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        await client.RunWorkersAsync(ct: cts.Token);
    }

    private static Task<string> ProcessOrder(OrderInput input, CancellationToken ct) =>
        Task.FromResult("INV-001");
    // </BasicWorker>

    // <ErrorHandling>
    private static void ErrorHandlingExample()
    {
        // BPMN error — caught by error boundary events in the process model
        throw new BpmnErrorException("INVALID_ORDER", "Order not found");
    }

    private static void FailureExample()
    {
        // Explicit failure with retry control
        throw new JobFailureException("Service unavailable", retries: 2, retryBackOffMs: 5000);
    }
    // </ErrorHandling>

    // <VoidHandler>
    public record NotificationInput(string Message);

    private static void VoidHandlerExample()
    {
        using var client = CamundaClient.Create();
        var config = new JobWorkerConfig { JobType = "send-notification", JobTimeoutMs = 30_000 };

        client.CreateJobWorker(config, async (job, ct) =>
        {
            await SendNotification(job.GetVariables<NotificationInput>()!, ct);
        });
    }

    private static Task SendNotification(NotificationInput input, CancellationToken ct) =>
        Task.CompletedTask;
    // </VoidHandler>
}
