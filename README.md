# Camunda 8 Orchestration Cluster API — C# SDK

[![NuGet](https://img.shields.io/nuget/v/Camunda.Orchestration.Sdk)](https://www.nuget.org/packages/Camunda.Orchestration.Sdk)
[![License](https://img.shields.io/github/license/camunda/orchestration-cluster-api-csharp)](LICENSE)

C# client SDK for the [Camunda 8 Orchestration Cluster REST API](https://docs.camunda.io/docs/apis-tools/camunda-api-rest/camunda-api-rest-overview/).

Unified configuration, OAuth/Basic auth, automatic retry, backpressure management, strongly-typed domain keys, and opt-in typed variables.

## Installation

```bash
dotnet add package Camunda.Orchestration.Sdk
```

## Quick Start

```csharp
using Camunda.Orchestration.Sdk;
using Camunda.Orchestration.Sdk.Api;

// Reads CAMUNDA_* environment variables automatically
using var client = Camunda.CreateClient();

var topology = await client.GetTopologyAsync();
```

## Configuration

The SDK uses environment variables for configuration, matching the [JS SDK](https://github.com/camunda/orchestration-cluster-api-js) conventions:

| Variable | Description | Default |
|---|---|---|
| `CAMUNDA_REST_ADDRESS` | Cluster REST API address | — |
| `CAMUNDA_AUTH_STRATEGY` | `NONE`, `OAUTH`, or `BASIC` | Auto-detected |
| `CAMUNDA_CLIENT_ID` | OAuth client ID | — |
| `CAMUNDA_CLIENT_SECRET` | OAuth client secret | — |
| `CAMUNDA_OAUTH_URL` | OAuth token endpoint | — |
| `CAMUNDA_TOKEN_AUDIENCE` | OAuth audience | — |
| `CAMUNDA_BASIC_AUTH_USERNAME` | Basic auth username | — |
| `CAMUNDA_BASIC_AUTH_PASSWORD` | Basic auth password | — |
| `CAMUNDA_DEFAULT_TENANT_ID` | Default tenant ID | `<default>` |
| `CAMUNDA_SDK_LOG_LEVEL` | Log level | `error` |
| `CAMUNDA_SDK_VALIDATION` | Validation mode (`req:none,res:none`) | — |
| `ZEEBE_REST_ADDRESS` | Alias for `CAMUNDA_REST_ADDRESS` | — |

### Programmatic Configuration

```csharp
using var client = Camunda.CreateClient(new CamundaOptions
{
    Config = new Dictionary<string, string>
    {
        ["CAMUNDA_REST_ADDRESS"] = "https://my-cluster.camunda.io",
        ["CAMUNDA_CLIENT_ID"] = "my-client-id",
        ["CAMUNDA_CLIENT_SECRET"] = "my-secret",
        ["CAMUNDA_OAUTH_URL"] = "https://login.cloud.camunda.io/oauth/token",
        ["CAMUNDA_TOKEN_AUDIENCE"] = "zeebe.camunda.io",
    },
});
```

### Custom HttpClient

```csharp
var httpClient = new HttpClient { BaseAddress = new Uri("https://my-cluster/v2/") };
using var client = Camunda.CreateClient(new CamundaOptions
{
    HttpClient = httpClient,
});
```

## Authentication

- **OAuth** — Automatic token management with singleflight refresh, caching, and retry
- **Basic** — HTTP Basic Authentication
- **None** — No authentication (local development)

Auth strategy is auto-detected from environment variables when not explicitly set.

## Resilience

### HTTP Retry

Automatic retry with exponential backoff and jitter for transient failures (429, 503, 500, timeouts).

### Backpressure Management

Adaptive concurrency management that responds to 429/503 signals:
- Reduces concurrent request permits on backpressure
- Recovers permits after quiet periods
- Configurable profiles: `BALANCED` (default), `LEGACY` (observe-only)

### Eventual Consistency

Built-in polling for eventually consistent endpoints with configurable wait times and predicates.

## Logging

The SDK uses `Microsoft.Extensions.Logging` — the standard .NET logging abstraction. This means it integrates with any logging framework that supports `ILoggerFactory` (Serilog, NLog, the built-in console logger, etc.).

### Default Behavior

When no logger is injected, the SDK uses a built-in console logger filtered by `CAMUNDA_SDK_LOG_LEVEL`:

| `CAMUNDA_SDK_LOG_LEVEL` | What is logged |
|---|---|
| `error` (default) | Errors only |
| `warn` | Errors + warnings |
| `info` | + OAuth token events, worker start/stop |
| `debug` | + HTTP requests/responses, retry decisions, backpressure changes |
| `trace` | + tenant injection, internal diagnostics |
| `silent` | Nothing (same as `NullLoggerFactory`) |

Output uses a tagged format matching the JS SDK:

```
[camunda-sdk][info][CamundaClient] CamundaClient constructed with auth strategy OAuth
[camunda-sdk][debug][CamundaClient] HTTP POST process-instances/search -> 200
[camunda-sdk][info][JobWorker.worker-process-order-1] JobWorker 'worker-process-order-1' started for type 'process-order'
```

### Injecting Your Own Logger

Pass an `ILoggerFactory` via `CamundaOptions` to integrate with your application's logging:

```csharp
using Microsoft.Extensions.Logging;

// Example: built-in .NET console logger with custom filtering
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .AddConsole()
        .SetMinimumLevel(LogLevel.Debug);
});

using var client = Camunda.CreateClient(new CamundaOptions
{
    LoggerFactory = loggerFactory,
});
```

When an `ILoggerFactory` is provided, `CAMUNDA_SDK_LOG_LEVEL` is ignored — filtering is controlled entirely by the injected factory.

### ASP.NET Core / Dependency Injection

In ASP.NET Core, pass the framework's logger factory:

```csharp
// In Program.cs or Startup
builder.Services.AddSingleton(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    return Camunda.CreateClient(new CamundaOptions
    {
        LoggerFactory = loggerFactory,
    });
});
```

All SDK log entries appear alongside your application logs with proper category names (`Camunda.Orchestration.Sdk.CamundaClient`, `Camunda.Orchestration.Sdk.JobWorker.*`, etc.).

### Serilog Integration

```csharp
using Serilog;
using Serilog.Extensions.Logging;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

using var loggerFactory = new SerilogLoggerFactory();
using var client = Camunda.CreateClient(new CamundaOptions
{
    LoggerFactory = loggerFactory,
});
```

### What Gets Logged

| Component | Level | Events |
|---|---|---|
| `CamundaClient` | Debug | HTTP request method + path, response status codes |
| `CamundaClient` | Warning | HTTP request failures (non-2xx) |
| `CamundaClient` | Trace | Default tenant ID injection |
| `OAuthManager` | Debug | Token request attempts |
| `OAuthManager` | Info | Token acquired (with effective expiry) |
| `BackpressureManager` | Debug | Permit reduction/recovery |
| `HttpRetryExecutor` | Debug | Retry attempts with delay and reason |
| `JobWorker.*` | Info | Worker started, worker stopped |
| `JobWorker.*` | Debug | Job completed |
| `JobWorker.*` | Error | Handler exceptions, poll failures |
| `EventualPoller` | Debug | Consistency polling progress |

## Strongly-Typed Domain Keys

All domain identifiers (process definition keys, job keys, user task keys, etc.) are `readonly record struct` types rather than plain strings. This prevents accidentally mixing different key types at compile time — the same pattern as the JS SDK's branded types.

```csharp
using Camunda.Orchestration.Sdk.Api;

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
```

Key types implement `ICamundaKey` (string-backed) or `ICamundaLongKey` (long-backed) and serialize as plain JSON values. Constraint validation (regex pattern, min/max length) is enforced in `AssumeExists()` and queryable via `IsValid()`.

## Typed Variables with DTOs

Camunda API operations use dynamic `variables` and `customHeaders` payloads. By default these are untyped (`object`), but you can opt in to compile-time type safety using your own DTOs.

### Sending Variables (Input)

Assign any DTO or dictionary to the `Variables` property — `System.Text.Json` serializes the runtime type automatically:

```csharp
using Camunda.Orchestration.Sdk.Api;

// Define your application domain models
public record OrderInput(string OrderId, decimal Amount);

// Assign the DTO directly
await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
{
    ProcessDefinitionId = ProcessDefinitionId.AssumeExists("order-process"),
    Variables = new OrderInput("ord-123", 99.99m),
});

// Dictionaries also work — no DTO required
await client.CompleteJobAsync(jobKey, new CompleteJobRequest
{
    Variables = new Dictionary<string, object> { ["processed"] = true },
});
```

### Receiving Variables (Output)

Use `DeserializeAs<T>()` to extract typed DTOs from API responses:

```csharp
using Camunda.Orchestration.Sdk.Runtime;  // for DeserializeAs<T>()

public record OrderResult(bool Processed, string InvoiceNumber);
public record JobHeaders(string Region, int Priority);

// Deserialize variables from any API response
var result = await client.CreateProcessInstanceAsync(/* ... */);
var output = result.Variables.DeserializeAs<OrderResult>();
// output.Processed, output.InvoiceNumber — fully typed

// Works for custom headers too
var headers = job.CustomHeaders.DeserializeAs<JobHeaders>();
// headers.Region, headers.Priority — fully typed
```

`DeserializeAs<T>()` handles the common runtime shapes:
- `JsonElement` (standard API response) → deserialized via `System.Text.Json`
- Already the target type → returned as-is (zero-copy)
- `null` → returns `default(T)`

Custom `JsonSerializerOptions` can be passed for non-standard naming conventions.

## Job Workers

Job workers subscribe to a specific job type and process jobs as they become available. The worker handles polling, concurrent dispatch, auto-completion, and error handling.

### Basic Worker

```csharp
using Camunda.Orchestration.Sdk;
using Camunda.Orchestration.Sdk.Runtime;
using Camunda.Orchestration.Sdk.Api;

using var client = Camunda.CreateClient();

// Define input/output DTOs
public record OrderInput(string OrderId, decimal Amount);
public record OrderOutput(bool Processed, string InvoiceNumber);

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
await client.RunWorkersAsync(cts.Token);
```

### Handler Contract

The handler return value determines the job outcome:

| Handler behavior | Job outcome |
|---|---|
| Return `object` | Auto-complete with those variables |
| Return `null` | Auto-complete with no variables |
| Throw `BpmnErrorException` | Trigger a BPMN error boundary event |
| Throw `JobFailureException` | Fail with custom retries / back-off |
| Throw any other exception | Auto-fail with `retries - 1` |

```csharp
// BPMN error — caught by error boundary events in the process model
throw new BpmnErrorException("INVALID_ORDER", "Order not found");

// Explicit failure with retry control
throw new JobFailureException("Service unavailable", retries: 2, retryBackOffMs: 5000);
```

### Void Handler (No Output Variables)

For handlers that don't return output variables, use the void overload:

```csharp
client.CreateJobWorker(config, async (job, ct) =>
{
    await SendNotification(job.GetVariables<NotificationInput>()!, ct);
    // Auto-completes with no variables
});
```

### Configuration

| Property | Default | Description |
|---|---|---|
| `JobType` | *(required)* | BPMN task type to subscribe to |
| `JobTimeoutMs` | *(required)* | Job lock duration (ms) |
| `MaxConcurrentJobs` | `10` | Max in-flight jobs per worker |
| `PollIntervalMs` | `500` | Delay between polls when idle |
| `PollTimeoutMs` | `null` | Long-poll timeout (null = broker default) |
| `FetchVariables` | `null` | Variable names to fetch (null = all) |
| `WorkerName` | auto | Worker name for logging |
| `AutoStart` | `true` | Start polling on creation |

### Concurrency

Jobs are dispatched as concurrent `Task`s on the .NET thread pool. `MaxConcurrentJobs` controls how many jobs may be in-flight simultaneously.

- **I/O-bound handlers** (HTTP calls, database queries): higher values like 32–128 improve throughput because `async` handlers release threads during `await` points — many jobs, few OS threads.
- **CPU-bound handlers**: set `MaxConcurrentJobs` to `Environment.ProcessorCount` to match cores.
- **Sequential processing**: set `MaxConcurrentJobs = 1`.

### Lifecycle

```csharp
// Manual start/stop
var worker = client.CreateJobWorker(config with { AutoStart = false }, handler);
worker.Start();

// Graceful stop — waits up to 10s for in-flight jobs to finish
var result = await worker.StopAsync(gracePeriod: TimeSpan.FromSeconds(10));
// result.RemainingJobs, result.TimedOut

// Or stop all workers at once
await client.StopAllWorkersAsync(TimeSpan.FromSeconds(10));

// DisposeAsync stops workers automatically
await using var disposableClient = Camunda.CreateClient();
```

## Contributing

See [MAINTAINER.md](MAINTAINER.md) for build instructions, project structure, and release strategy.

## License

[Apache 2.0](LICENSE)
