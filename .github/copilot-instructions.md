# Copilot instructions (orchestration-cluster-api-csharp)

This repo generates a C# SDK from a multi-file OpenAPI spec sourced from the Camunda monorepo.

## Key flows (what to run)

- Build (fetches upstream spec): `bash scripts/build.sh`
- Build using already-fetched spec (fast local iteration): `bash scripts/build-local.sh`
- Only fetch spec: `bash scripts/fetch-spec.sh`
- Only bundle spec: `bash scripts/bundle-spec.sh`
- Only regenerate SDK sources: `dotnet run --project src/Camunda.Client.Generator`

Generation pipeline (high level):

1. `scripts/fetch-spec.sh` → sparse clone upstream spec YAML under `external-spec/upstream/...`
2. `scripts/bundle-spec.sh` → produce `external-spec/bundled/rest-api.bundle.json` (via swagger-cli)
3. `dotnet run --project src/Camunda.Client.Generator` → generate `src/Camunda.Client/Generated/*`
4. `dotnet format src/Camunda.Client/Camunda.Client.csproj --no-restore` → auto-fix generated code style
5. `dotnet build` → compile library + tests
6. `dotnet format --verify-no-changes` → lint gate (fails if any violations remain)
7. `dotnet test` → run unit tests

## Where things live

- Bundled spec input to the generator: `external-spec/bundled/rest-api.bundle.json`
- Generator output (checked/used by build): `src/Camunda.Client/Generated/`
- Generator logic: `src/Camunda.Client.Generator/CSharpClientGenerator.cs`
- Runtime infrastructure: `src/Camunda.Client/Runtime/`
- Main client class: `src/Camunda.Client/CamundaClient.cs`
- Unit tests: `test/Camunda.Client.Tests/`
- Build scripts: `scripts/`
- CI/CD: `.github/workflows/ci.yml` and `.github/workflows/release.yml`

## Architecture

The SDK is a partial class design:
- `CamundaClient.cs` — infrastructure: HttpClient, auth, retry, backpressure, JSON serialization
- `Generated/CamundaClient.Generated.cs` — auto-generated API methods (one per OpenAPI operation)
- `Generated/Models.Generated.cs` — auto-generated model classes and enums from OpenAPI schemas

Runtime components:
- `ConfigurationHydrator` — environment variable hydration mirroring the JS SDK
- `AuthHandler` — DelegatingHandler for OAuth/Basic auth
- `OAuthManager` — token management with singleflight refresh and retry
- `HttpRetryExecutor` — exponential backoff with jitter
- `BackpressureManager` — adaptive concurrency management
- `EventualPoller` — eventual consistency polling

## Versioning

Uses MinVer for automatic versioning from git tags:
- Tags: `v<major>.<minor>.<patch>` (e.g. `v8.8.0`)
- Prereleases: automatic `alpha.N` suffix on main
- Same branching model as the JS SDK: `main` = alpha, `stable/*` = releases

## Code Style & Linting

- Style rules: `.editorconfig` (Allman braces, namespace-scoped usings, PascalCase types, _camelCase private fields)
- Enforcement: `Directory.Build.props` sets `EnforceCodeStyleInBuild=true` and `AnalysisLevel=latest-recommended`
- Auto-fix generated code: `dotnet format src/Camunda.Client/Camunda.Client.csproj --no-restore`
- Lint check (CI gate): `dotnet format --verify-no-changes`
- Generated code (`src/Camunda.Client/Generated/`) has relaxed style rules (severity `none`)

## Quick debug checklist

1. Confirm the build works: `dotnet build`
2. Run tests: `dotnet test`
3. Lint check: `dotnet format --verify-no-changes`
4. Check generated output: `ls src/Camunda.Client/Generated/`
5. Inspect spec: `cat external-spec/bundled/rest-api.bundle.json | head -100`
