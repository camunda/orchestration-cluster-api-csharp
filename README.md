# Camunda 8 Orchestration Cluster API — C# SDK (Technical Preview)

<!-- WARNING: The content and specific structure of this file drives Docusaurus generation in camunda-docs. Also, code examples are injected during build. Please refer to MAINTAINER.md before editing. -->
<!-- docs:cut:start -->
[![NuGet](https://img.shields.io/nuget/v/Camunda.Orchestration.Sdk)](https://www.nuget.org/packages/Camunda.Orchestration.Sdk)
[![Documentation](https://img.shields.io/badge/docs-API%20Reference-blue)](https://camunda.github.io/orchestration-cluster-api-csharp)
[![License](https://img.shields.io/github/license/camunda/orchestration-cluster-api-csharp)](LICENSE)
<!-- docs:cut:end -->

Technical preview of the C# client SDK for the [Camunda 8 Orchestration Cluster REST API](https://docs.camunda.io/docs/apis-tools/camunda-api-rest/camunda-api-rest-overview/).

Unified configuration, OAuth/Basic auth, automatic retry, backpressure management, strongly-typed domain keys, and opt-in typed variables.

<!-- docs:cut:start -->
Full API Documentation available [here](https://camunda.github.io/orchestration-cluster-api-csharp).
<!-- docs:cut:end -->

## Support status

This is a technical preview of the C# client that will become fully supported in Camunda 8.10.0. 

Prior to Camunda 8.10.0, this client will undergo changes as we stabilise the code and incorporate feedback from early adopters. We endeavor to keep disruption to a minimum, but there may be breaking changes.

## Installation

```bash
dotnet add package Camunda.Orchestration.Sdk
```

### Versioning

This SDK has a different release cadence from the Camunda server. Features and fixes land in the SDK during a server release. 

The major version of the SDK signals a 1:1 type coherence with the server API for a Camunda minor release. 

SDK version `n.y.z` -> server version `8.n`, so the type surface of SDK version 9.y.z matches the API surface of Camunda 8.9.

Using a later SDK version, for example: SDK version 10.y.z with Camunda 8.9, means that the SDK contains additive surfaces that are not guaranteed at runtime, and the compiler cannot warn of unsupported operations. 

Using an earlier SDK version, for example: SDK version 9.y.z with Camunda 8.10, results in slightly degraded compiler reasoning: exhaustiveness checks cannot be guaranteed by the compiler for any extended surfaces (principally, enums with added members). 

In the vast majority of use-cases, this will not be an issue; but you should be aware that using the matching SDK major version for the server minor version provides the strongest compiler guarantees about runtime reliability. 

**Recommended approach**:

- Check the [CHANGELOG](https://github.com/camunda/orchestration-cluster-api-csharp/releases).
- As a sanity check during server version upgrade, rebuild applications with the matching SDK major version to identify any affected runtime surfaces.

## Quick Start (Zero-Config — Recommended)

Keep configuration out of application code. Let the factory read `CAMUNDA_*` variables from the environment (12-factor style). This makes rotation, secret management, and environment promotion safer and simpler.

<!-- snippet:UsingDirective+QuickStart -->
```csharp
using Camunda.Orchestration.Sdk;

// Zero-config construction: reads CAMUNDA_* from environment variables.
// If no configuration is present, defaults to Camunda 8 Run on localhost.
using var client = CamundaClient.Create();

var topology = await client.GetTopologyAsync();
Console.WriteLine($"Brokers: {topology.Brokers?.Count ?? 0}");
```

Typical environment (example):

```bash
CAMUNDA_REST_ADDRESS=https://cluster.example   # SDK appends /v2 automatically
CAMUNDA_AUTH_STRATEGY=OAUTH
CAMUNDA_CLIENT_ID=***
CAMUNDA_CLIENT_SECRET=***
CAMUNDA_OAUTH_URL=https://login.cloud.camunda.io/oauth/token
CAMUNDA_DEFAULT_TENANT_ID=<default>            # optional: override default tenant
```

> **Why zero-config?**
>
> - **Separation of concerns**: business code depends on an interface, not on secrets/constants wiring.
> - **12-Factor alignment**: config lives in the environment → simpler promotion (dev → staging → prod).
> - **Secret rotation**: rotate credentials without a code change or redeploy.
> - **Immutable start**: single hydration pass prevents drift / mid-request mutations.
> - **Test ergonomics**: swap env vars per test without touching source; create multiple clients for multi-tenant tests.
> - **Security review**: fewer code paths handling secrets; scanners & vault tooling work at the boundary.
> - **Deploy portability**: same artifact runs everywhere; only the environment differs.
> - **Cross-SDK consistency**: identical variable names across JavaScript, C#, and Python SDKs.

### Programmatic Overrides (Advanced)

Use only when you must supply or mutate configuration dynamically (e.g. multi-tenant routing, tests, ephemeral preview environments). Keys mirror their `CAMUNDA_*` env names:

<!-- snippet:UsingDirective+ProgrammaticOverrides -->
```csharp
using Camunda.Orchestration.Sdk;

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
```

### Configuration via `appsettings.json`

The SDK can read configuration from any `IConfiguration` source (appsettings.json, user secrets, Azure Key Vault, etc.) using idiomatic .NET PascalCase section keys:

```json
{
  "Camunda": {
    "RestAddress": "https://cluster.example.com",
    "Auth": {
      "Strategy": "OAUTH",
      "ClientId": "my-client-id",
      "ClientSecret": "my-secret"
    },
    "OAuth": {
      "Url": "https://login.cloud.camunda.io/oauth/token"
    },
    "Backpressure": {
      "Profile": "CONSERVATIVE"
    }
  }
}
```

Pass the section to the client:

<!-- snippet:UsingDirective+AppSettingsConfig -->
```csharp
using Camunda.Orchestration.Sdk;

var builder = WebApplication.CreateBuilder(args);

using var client = CamundaClient.Create(new CamundaOptions
{
    Configuration = builder.Configuration.GetSection("Camunda"),
});
```

Precedence (highest wins): `Config` dictionary > `IConfiguration` section > environment variables > defaults.

This means you can set secrets via environment variables (or a vault) and non-sensitive settings via `appsettings.json` — they layer naturally:

```json
// appsettings.json — non-sensitive, checked into source control
{
  "Camunda": {
    "RestAddress": "https://cluster.example.com",
    "Backpressure": { "Profile": "CONSERVATIVE" }
  }
}
```

```bash
# Secrets injected via environment (vault, CI, container orchestrator)
CAMUNDA_CLIENT_ID=***
CAMUNDA_CLIENT_SECRET=***
CAMUNDA_OAUTH_URL=https://login.cloud.camunda.io/oauth/token
```

<details>
<summary>appsettings.json key reference</summary>

| appsettings.json key | Maps to env var |
|---|---|
| `RestAddress` | `CAMUNDA_REST_ADDRESS` |
| `TokenAudience` | `CAMUNDA_TOKEN_AUDIENCE` |
| `DefaultTenantId` | `CAMUNDA_DEFAULT_TENANT_ID` |
| `LogLevel` | `CAMUNDA_SDK_LOG_LEVEL` |
| `Validation` | `CAMUNDA_SDK_VALIDATION` |
| `Auth:Strategy` | `CAMUNDA_AUTH_STRATEGY` |
| `Auth:ClientId` | `CAMUNDA_CLIENT_ID` |
| `Auth:ClientSecret` | `CAMUNDA_CLIENT_SECRET` |
| `Auth:BasicUsername` | `CAMUNDA_BASIC_AUTH_USERNAME` |
| `Auth:BasicPassword` | `CAMUNDA_BASIC_AUTH_PASSWORD` |
| `OAuth:Url` | `CAMUNDA_OAUTH_URL` |
| `OAuth:ClientId` | `CAMUNDA_CLIENT_ID` |
| `OAuth:ClientSecret` | `CAMUNDA_CLIENT_SECRET` |
| `OAuth:GrantType` | `CAMUNDA_OAUTH_GRANT_TYPE` |
| `OAuth:Scope` | `CAMUNDA_OAUTH_SCOPE` |
| `OAuth:TimeoutMs` | `CAMUNDA_OAUTH_TIMEOUT_MS` |
| `OAuth:RetryMax` | `CAMUNDA_OAUTH_RETRY_MAX` |
| `OAuth:RetryBaseDelayMs` | `CAMUNDA_OAUTH_RETRY_BASE_DELAY_MS` |
| `HttpRetry:MaxAttempts` | `CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS` |
| `HttpRetry:BaseDelayMs` | `CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS` |
| `HttpRetry:MaxDelayMs` | `CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS` |
| `Backpressure:Profile` | `CAMUNDA_SDK_BACKPRESSURE_PROFILE` |
| `Backpressure:InitialMax` | `CAMUNDA_SDK_BACKPRESSURE_INITIAL_MAX` |
| `Backpressure:SoftFactor` | `CAMUNDA_SDK_BACKPRESSURE_SOFT_FACTOR` |
| `Backpressure:SevereFactor` | `CAMUNDA_SDK_BACKPRESSURE_SEVERE_FACTOR` |
| `Backpressure:RecoveryIntervalMs` | `CAMUNDA_SDK_BACKPRESSURE_RECOVERY_INTERVAL_MS` |
| `Backpressure:RecoveryStep` | `CAMUNDA_SDK_BACKPRESSURE_RECOVERY_STEP` |
| `Backpressure:DecayQuietMs` | `CAMUNDA_SDK_BACKPRESSURE_DECAY_QUIET_MS` |
| `Backpressure:Floor` | `CAMUNDA_SDK_BACKPRESSURE_FLOOR` |
| `Backpressure:SevereThreshold` | `CAMUNDA_SDK_BACKPRESSURE_SEVERE_THRESHOLD` |
| `Eventual:PollDefaultMs` | `CAMUNDA_SDK_EVENTUAL_POLL_DEFAULT_MS` |

</details>

### Dependency Injection (`AddCamundaClient`)

For ASP.NET Core and other DI-based applications, use the `AddCamundaClient()` extension method on `IServiceCollection`. The client is registered as a singleton and automatically picks up `ILoggerFactory` from the container.

**Zero-config** (environment variables only):

<!-- snippet:UsingDirective+DIZeroConfig -->
```csharp
using Camunda.Orchestration.Sdk;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCamundaClient();
```

**With `appsettings.json`**:

<!-- snippet:UsingDirective+DIAppSettings -->
```csharp
using Camunda.Orchestration.Sdk;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCamundaClient(builder.Configuration.GetSection("Camunda"));
```

**With options callback** (full control):

<!-- snippet:UsingDirective+DIOptionsCallback -->
```csharp
using Camunda.Orchestration.Sdk;

builder.Services.AddCamundaClient(options =>
{
    options.Configuration = builder.Configuration.GetSection("Camunda");
    // or: options.Config = new Dictionary<string, string> { ... };
});
```

Inject the client anywhere via constructor injection:

<!-- snippet:DIControllerInjection -->
```csharp
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
```

### Custom HttpClient

<!-- snippet:UsingDirective+CustomHttpClient -->
```csharp
using Camunda.Orchestration.Sdk;

var httpClient = new HttpClient { BaseAddress = new Uri("https://my-cluster/v2/") };
using var client = CamundaClient.Create(new CamundaOptions
{
    HttpClient = httpClient,
});
```

## Configuration Reference

The SDK uses environment variables for configuration, matching the [JS SDK](https://github.com/camunda/orchestration-cluster-api-js) conventions:

| Variable | Description | Default |
|---|---|---|
| `CAMUNDA_REST_ADDRESS` | Cluster REST API address | — |
| `CAMUNDA_AUTH_STRATEGY` | `NONE`, `OAUTH`, or `BASIC` | Auto-detected |
| `CAMUNDA_CLIENT_ID` | OAuth client ID | — |
| `CAMUNDA_CLIENT_SECRET` | OAuth client secret | — |
| `CAMUNDA_OAUTH_URL` | OAuth token endpoint | — |
| `CAMUNDA_TOKEN_AUDIENCE` | OAuth audience | — |
| `CAMUNDA_OAUTH_GRANT_TYPE` | OAuth grant type | `client_credentials` |
| `CAMUNDA_OAUTH_SCOPE` | OAuth scope | — |
| `CAMUNDA_OAUTH_TIMEOUT_MS` | OAuth token request timeout (ms) | `5000` |
| `CAMUNDA_OAUTH_RETRY_MAX` | Max OAuth token fetch retries | `5` |
| `CAMUNDA_OAUTH_RETRY_BASE_DELAY_MS` | OAuth retry base delay (ms) | `1000` |
| `CAMUNDA_BASIC_AUTH_USERNAME` | Basic auth username | — |
| `CAMUNDA_BASIC_AUTH_PASSWORD` | Basic auth password | — |
| `CAMUNDA_DEFAULT_TENANT_ID` | Default tenant ID | `<default>` |
| `CAMUNDA_SDK_LOG_LEVEL` | Log level (`error`, `warn`, `info`, `debug`, `trace`, `silent`) | `error` |
| `CAMUNDA_SDK_VALIDATION` | Validation mode (see below) | `req:none,res:none` |
| `CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS` | Total HTTP retry attempts (initial + retries) | `3` |
| `CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS` | HTTP retry base backoff (ms) | `100` |
| `CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS` | HTTP retry max backoff cap (ms) | `2000` |
| `CAMUNDA_SDK_EVENTUAL_POLL_DEFAULT_MS` | Default eventual consistency poll interval (ms) | `500` |
| `ZEEBE_REST_ADDRESS` | Alias for `CAMUNDA_REST_ADDRESS` | — |

For backpressure configuration variables, see [Global Backpressure](#global-backpressure-adaptive-concurrency).

## Authentication

- **OAuth** — Automatic token management with singleflight refresh, caching, and retry
- **Basic** — HTTP Basic Authentication
- **None** — No authentication (local development)

Auth strategy is auto-detected from environment variables when not explicitly set.

## Resilience

### HTTP Retry

Automatic retry with exponential backoff and jitter for transient failures (429, 503, 500, timeouts).

| Variable | Default | Description |
|---|---|---|
| `CAMUNDA_SDK_HTTP_RETRY_MAX_ATTEMPTS` | `3` | Total attempts (initial + retries) |
| `CAMUNDA_SDK_HTTP_RETRY_BASE_DELAY_MS` | `100` | Base backoff delay (ms) |
| `CAMUNDA_SDK_HTTP_RETRY_MAX_DELAY_MS` | `2000` | Maximum backoff cap (ms) |

### Global Backpressure (Adaptive Concurrency)

The client includes an adaptive backpressure manager that throttles the number of in-flight operations when the cluster signals resource exhaustion. It complements (not replaces) per-request HTTP retry.

#### Signals Considered

An HTTP response is treated as a backpressure signal when it matches one of:

- `429` (Too Many Requests) — always
- `503` with `title === "RESOURCE_EXHAUSTED"`
- `500` whose RFC 9457 / 7807 `detail` text contains `RESOURCE_EXHAUSTED`

All other 5xx variants are treated as non-retryable (fail fast) and do **not** influence the adaptive gate.

#### How It Works

1. Normal state starts with the concurrency cap from `CAMUNDA_SDK_BACKPRESSURE_INITIAL_MAX` (default 16).
2. On backpressure signals the manager reduces available permits using the soft factor (70% by default).
3. Repeated consecutive signals escalate severity to `severe`, applying a stronger reduction factor (50%).
4. Successful (non-backpressure) completions trigger passive recovery checks that gradually restore permits over time if the system stays quiet.
5. Quiet periods (no signals for a configurable decay interval) downgrade severity and reset the consecutive counter.

The policy is intentionally conservative: it only engages after genuine pressure signals and recovers gradually to avoid oscillation.

#### Configuration

| Variable | Default | Description |
|---|---|---|
| `CAMUNDA_SDK_BACKPRESSURE_PROFILE` | `BALANCED` | Preset profile (see below) |
| `CAMUNDA_SDK_BACKPRESSURE_INITIAL_MAX` | `16` | Bootstrap concurrency cap |
| `CAMUNDA_SDK_BACKPRESSURE_SOFT_FACTOR` | `70` | Percentage multiplier on soft backpressure (70 → 0.70×) |
| `CAMUNDA_SDK_BACKPRESSURE_SEVERE_FACTOR` | `50` | Percentage multiplier on severe backpressure |
| `CAMUNDA_SDK_BACKPRESSURE_RECOVERY_INTERVAL_MS` | `1000` | Interval between passive recovery checks (ms) |
| `CAMUNDA_SDK_BACKPRESSURE_RECOVERY_STEP` | `1` | Permits regained per recovery interval |
| `CAMUNDA_SDK_BACKPRESSURE_DECAY_QUIET_MS` | `2000` | Quiet period to downgrade severity (ms) |
| `CAMUNDA_SDK_BACKPRESSURE_FLOOR` | `1` | Minimum concurrency floor while degraded |
| `CAMUNDA_SDK_BACKPRESSURE_SEVERE_THRESHOLD` | `3` | Consecutive signals required to enter severe state |

#### Profiles

Profiles supply coordinated defaults. Any explicitly set env var overrides the profile value.

| Profile | initialMax | softFactor% | severeFactor% | recoveryMs | recoveryStep | quietDecayMs | floor | severeThreshold | Use case |
|---|---|---|---|---|---|---|---|---|---|
| `BALANCED` | 16 | 70 | 50 | 1000 | 1 | 2000 | 1 | 3 | General workloads |
| `CONSERVATIVE` | 12 | 60 | 40 | 1200 | 1 | 2500 | 1 | 2 | Tighter capacity constraints |
| `AGGRESSIVE` | 24 | 80 | 60 | 800 | 2 | 1500 | 2 | 4 | High throughput scenarios |
| `LEGACY` | — | — | — | — | — | — | — | — | Observe-only (no gating) |

Select via environment:

```bash
CAMUNDA_SDK_BACKPRESSURE_PROFILE=AGGRESSIVE
```

Override individual knobs on top of a profile:

```bash
CAMUNDA_SDK_BACKPRESSURE_PROFILE=AGGRESSIVE
CAMUNDA_SDK_BACKPRESSURE_INITIAL_MAX=32
```

The `LEGACY` profile disables adaptive gating entirely — signals are still tracked for observability but no concurrency limits are applied. Use this to opt out of backpressure management while retaining per-request retry.

#### Inspecting State Programmatically

<!-- snippet:BackpressureState -->
```csharp
var state = client.GetBackpressureState();
// state.Severity: "healthy", "soft", or "severe"
// state.Consecutive: consecutive backpressure signals observed
// state.PermitsMax: current concurrency cap (null when LEGACY / not engaged)
```

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

<!-- snippet:UsingDirective+InjectLogger -->
```csharp
using Camunda.Orchestration.Sdk;

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
```

When an `ILoggerFactory` is provided, `CAMUNDA_SDK_LOG_LEVEL` is ignored — filtering is controlled entirely by the injected factory.

### ASP.NET Core / Dependency Injection

When using `AddCamundaClient()`, the SDK automatically resolves `ILoggerFactory` from the DI container — no manual wiring needed:

<!-- snippet:UsingDirective+DILogging -->
```csharp
using Camunda.Orchestration.Sdk;

var builder = WebApplication.CreateBuilder(args);

// Logging configuration
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// SDK automatically uses the host's ILoggerFactory
builder.Services.AddCamundaClient(builder.Configuration.GetSection("Camunda"));
```

All SDK log entries appear alongside your application logs with proper category names (`Camunda.Orchestration.Sdk.CamundaClient`, `Camunda.Orchestration.Sdk.JobWorker.*`, etc.).

### Serilog Integration

```csharp
using Camunda.Orchestration.Sdk;
using Serilog;
using Serilog.Extensions.Logging;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

using var loggerFactory = new SerilogLoggerFactory();
using var client = CamundaClient.Create(new CamundaOptions
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

<!-- snippet:UsingDirective+DomainKeys -->
```csharp
using Camunda.Orchestration.Sdk;

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

## Deploying Resources

Deploy BPMN, DMN, or Form files from disk:

<!-- snippet:UsingDirective+DeployResources -->
```csharp
using Camunda.Orchestration.Sdk;

using var client = CamundaClient.Create();

var result = await client.DeployResourcesFromFilesAsync(["process.bpmn", "decision.dmn"]);

Console.WriteLine($"Deployment key: {result.DeploymentKey}");
foreach (var process in result.Processes)
{
    Console.WriteLine($"  Process: {process.ProcessDefinitionId} (key: {process.ProcessDefinitionKey})");
}
```

## Creating a Process Instance

The recommended pattern is to obtain keys from a prior API response (e.g. a deployment) and pass them directly — no manual conversion needed:

<!-- snippet:UsingDirective+ReadmeCreateProcessInstance -->
```csharp
using Camunda.Orchestration.Sdk;

using var client = CamundaClient.Create();

var deployment = await client.DeployResourcesFromFilesAsync(["process.bpmn"]);
var processKey = deployment.Processes[0].ProcessDefinitionKey;

var result = await client.CreateProcessInstanceAsync(
    new ProcessInstanceCreationInstructionByKey
    {
        ProcessDefinitionKey = processKey,
    });

Console.WriteLine($"Process instance key: {result.ProcessInstanceKey}");
```

If you need to restore a key from external storage (database, message queue, config file), wrap the raw value with the domain key constructor:

<!-- snippet:UsingDirective+CreateProcessFromStorage -->
```csharp
using Camunda.Orchestration.Sdk;

using var client = CamundaClient.Create();

var storedKey = "2251799813685249"; // from a DB row or config
var result = await client.CreateProcessInstanceAsync(
    new ProcessInstanceCreationInstructionByKey
    {
        ProcessDefinitionKey = ProcessDefinitionKey.AssumeExists(storedKey),
    });

Console.WriteLine($"Process instance key: {result.ProcessInstanceKey}");
```

You can also start a process instance by BPMN process ID (which uses the latest deployed version):

<!-- snippet:CreateProcessById -->
```csharp
var result = await client.CreateProcessInstanceAsync(
    new ProcessInstanceCreationInstructionById
    {
        ProcessDefinitionId = ProcessDefinitionId.AssumeExists("order-process"),
    });
```

## Typed Variables with DTOs

Camunda API operations use dynamic `variables` and `customHeaders` payloads. By default these are untyped (`object`), but you can opt in to compile-time type safety using your own DTOs.

### Sending Variables (Input)

Assign any DTO or dictionary to the `Variables` property — `System.Text.Json` serializes the runtime type automatically:

<!-- snippet:UsingDirective+SendingVariables+SendingVariablesBody -->
```csharp
using Camunda.Orchestration.Sdk;

// Define your application domain models
public record OrderInput(string OrderId, decimal Amount);

// Assign the DTO directly
await client.CreateProcessInstanceAsync(new ProcessInstanceCreationInstructionById
{
    ProcessDefinitionId = ProcessDefinitionId.AssumeExists("order-process"),
    Variables = new OrderInput("ord-123", 99.99m),
});

// Dictionaries also work — no DTO required
await client.CompleteJobAsync(jobKey, new JobCompletionRequest
{
    Variables = new Dictionary<string, object> { ["processed"] = true },
});
```

### Receiving Variables (Output)

Use `DeserializeAs<T>()` to extract typed DTOs from API responses:

<!-- snippet:UsingDirective+ReceivingVariables+ReceivingVariablesBody -->
```csharp
using Camunda.Orchestration.Sdk;

public record OrderResult(bool Processed, string InvoiceNumber);

// Deserialize variables from any API response
var result = await client.CreateProcessInstanceAsync(
    new ProcessInstanceCreationInstructionById
    {
        ProcessDefinitionId = ProcessDefinitionId.AssumeExists("test"),
    });
var output = result.Variables.DeserializeAs<OrderResult>();
// output.Processed, output.InvoiceNumber — fully typed
```

`DeserializeAs<T>()` handles the common runtime shapes:
- `JsonElement` (standard API response) → deserialized via `System.Text.Json`
- Already the target type → returned as-is (zero-copy)
- `null` → returns `default(T)`

Custom `JsonSerializerOptions` can be passed for non-standard naming conventions.

## Job Workers

Job workers subscribe to a specific job type and process jobs as they become available. The worker handles polling, concurrent dispatch, auto-completion, and error handling.

### Basic Worker

<!-- snippet:UsingDirective+BasicWorker+BasicWorkerBody -->
```csharp
using Camunda.Orchestration.Sdk;

// Define input/output DTOs
public record OrderOutput(bool Processed, string InvoiceNumber);

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

<!-- snippet:ErrorHandling+ErrorHandlingFailure -->
```csharp
// BPMN error — caught by error boundary events in the process model
throw new BpmnErrorException("INVALID_ORDER", "Order not found");

// Explicit failure with retry control
throw new JobFailureException("Service unavailable", retries: 2, retryBackOffMs: 5000);
```

### Void Handler (No Output Variables)

For handlers that don't return output variables, use the void overload:

<!-- snippet:VoidHandler+VoidHandlerBody -->
```csharp
public record NotificationInput(string Message);

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
| `StartupJitterMaxSeconds` | `0` | Max random delay (seconds) before first poll. Spreads out activation requests when multiple instances restart simultaneously. `0` = no delay. |

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
await using var disposableClient = CamundaClient.Create();
```

## Contributing

See [MAINTAINER.md](MAINTAINER.md) for build instructions, project structure, and release strategy.

## License

[Apache 2.0](LICENSE)
