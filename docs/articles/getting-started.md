# Getting Started

## Installation

```bash
dotnet add package Camunda.Client
```

## Basic Usage

```csharp
using Camunda.Client;

// Reads CAMUNDA_* environment variables automatically
using var client = Camunda.CreateClient();
```

## Configuration

Set environment variables to configure the client:

```bash
export CAMUNDA_REST_ADDRESS=https://your-cluster.camunda.io
export CAMUNDA_CLIENT_ID=your-client-id
export CAMUNDA_CLIENT_SECRET=your-secret
export CAMUNDA_OAUTH_URL=https://login.cloud.camunda.io/oauth/token
export CAMUNDA_TOKEN_AUDIENCE=zeebe.camunda.io
```

Or configure programmatically:

```csharp
using var client = Camunda.CreateClient(new CamundaOptions
{
    Config = new Dictionary<string, string>
    {
        ["CAMUNDA_REST_ADDRESS"] = "https://your-cluster.camunda.io",
        ["CAMUNDA_CLIENT_ID"] = "your-client-id",
        ["CAMUNDA_CLIENT_SECRET"] = "your-secret",
        ["CAMUNDA_OAUTH_URL"] = "https://login.cloud.camunda.io/oauth/token",
        ["CAMUNDA_TOKEN_AUDIENCE"] = "zeebe.camunda.io",
    },
});
```

## Authentication

The SDK supports three auth strategies:

- **OAuth** — Automatic token management with singleflight refresh
- **Basic** — HTTP Basic Authentication
- **None** — No authentication (local development)

Auth strategy is auto-detected when `CAMUNDA_CLIENT_ID`, `CAMUNDA_CLIENT_SECRET`, and `CAMUNDA_OAUTH_URL` are all set.

## Error Handling

All API errors throw typed exceptions:

```csharp
try
{
    // var result = await client.GetProcessInstanceAsync(key);
}
catch (HttpSdkException ex) when (ex.Status == 404)
{
    Console.WriteLine($"Not found: {ex.Detail}");
}
catch (EventualConsistencyTimeoutException ex)
{
    Console.WriteLine($"Timed out after {ex.WaitedMs}ms");
}
```
