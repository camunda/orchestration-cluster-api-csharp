---
mode: agent
description: Generate missing SDK code examples for new API operations
tools:
  - run_in_terminal
  - file_search
  - read_file
  - replace_string_in_file
  - create_file
  - grep_search
---

# Add Missing SDK Examples

You are generating C# code examples for the Camunda Orchestration Cluster API C# SDK.

## Step 1: Identify missing operations

Run the coverage check:
```
node scripts/check-example-coverage.js
```

This compares every `operationId` in `external-spec/bundled/rest-api.bundle.json` against `examples/operation-map.json` and prints the missing operations.

If the spec hasn't been fetched yet, run `bash scripts/bundle-spec.sh` first.

## Step 2: Study the spec for each missing operation

For each missing operationId, read the relevant entry in the bundled spec to understand:
- HTTP method and path
- Path parameters (and their types)
- Request body schema (if any)
- Response schema
- Which tag/domain it belongs to (e.g. "Process instance", "Job", "User task")

Also check `src/Camunda.Orchestration.Sdk/Generated/Models.Generated.cs` for the exact model type names and `src/Camunda.Orchestration.Sdk/Generated/CamundaClient.Generated.cs` for the exact method signatures.

## Step 3: Find the right example file

Examples are organized by domain in `examples/`. Find the existing file that matches the operation's tag. If no suitable file exists, create a new one following the naming convention (`PascalCase.cs`) with:
```csharp
// Compilable usage examples for DomainName operations.
using Camunda.Orchestration.Sdk;

public static class DomainNameExamples
{
    // examples go here
}
```

## Step 4: Write the example

Follow these exact patterns:

### Region tags (dual system required)
```csharp
#region RegionName
// <RegionName>
public static async Task RegionNameExample()
{
    using var client = CamundaClient.Create();

    // method call here
}
// </RegionName>
#endregion RegionName
```
Use PascalCase region names derived from the operationId (e.g., `createProcessInstance` → `CreateProcessInstance`).

Both the C# `#region` directive AND the `// <Tag>` XML-style comments are required.

### Method call patterns
```csharp
// Simple GET with typed key param:
public static async Task GetSomethingExample(SomethingKey somethingKey)
{
    using var client = CamundaClient.Create();
    var result = await client.GetSomethingAsync(somethingKey);
    Console.WriteLine($"Name: {result.Name}");
}

// POST with request body:
public static async Task CreateSomethingExample()
{
    using var client = CamundaClient.Create();
    await client.CreateSomethingAsync(new SomethingCreationRequest
    {
        Name = "example",
        Description = "example description",
    });
}

// Search:
public static async Task SearchSomethingsExample()
{
    using var client = CamundaClient.Create();
    var result = await client.SearchSomethingsAsync(new SomethingSearchQuery
    {
        Filter = new SomethingSearchQueryFilter
        {
            Name = "example",
        },
    });
}

// DELETE with typed key param:
public static async Task DeleteSomethingExample(SomethingKey somethingKey)
{
    using var client = CamundaClient.Create();
    await client.DeleteSomethingAsync(somethingKey);
}
```

Do NOT use `.AssumeExists()` — always accept typed keys as function parameters instead.

### Conventions
- All async methods end in `Async`
- Use `CamundaClient.Create()` for client init (wrapped in `using var`)
- Accept typed keys (e.g. `ProcessInstanceKey`, `JobKey`) as **function parameters** rather than using `.AssumeExists()`
- Use object initializer syntax for request bodies (`new Type { Props }`)

## Step 5: Update operation-map.json

Add an entry for each new example:
```json
"operationId": [
  { "file": "Domain.cs", "region": "RegionName", "label": "Short description" }
]
```

The `operationId` key must match the spec's operationId exactly (camelCase).

## Step 6: Verify

Run `dotnet build docs/examples/` to confirm the examples compile, then re-run `node scripts/check-example-coverage.js` to verify coverage.
