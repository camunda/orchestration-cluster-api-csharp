# Copilot instructions (orchestration-cluster-api-csharp)

This repo generates a C# SDK from a multi-file OpenAPI spec sourced from the Camunda monorepo.

## Key flows (what to run)

- Build (fetches upstream spec): `bash scripts/build.sh`
- Build pinned to a specific spec ref: `SPEC_REF=my-branch bash scripts/build.sh`
- Build using already-fetched spec (fast local iteration): `bash scripts/build-local.sh`
- Only fetch & bundle spec: `bash scripts/bundle-spec.sh`
- Only bundle (skip fetch, use local spec): `CAMUNDA_SDK_SKIP_FETCH_SPEC=1 bash scripts/bundle-spec.sh`
- Only regenerate SDK sources: `dotnet run --project src/Camunda.Orchestration.Sdk.Generator`

Generation pipeline (high level):

1. `scripts/bundle-spec.sh` → uses `camunda-schema-bundler` to fetch & bundle upstream spec → `external-spec/bundled/rest-api.bundle.json`
2. `dotnet run --project src/Camunda.Orchestration.Sdk.Generator` → generate `src/Camunda.Orchestration.Sdk/Generated/*`
3. `dotnet format src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj --no-restore` → auto-fix generated code style
4. `dotnet build` → compile library + tests
5. `dotnet format --verify-no-changes` → lint gate (fails if any violations remain)
6. `dotnet test` → run unit tests

Spec bundling uses the shared `camunda-schema-bundler` npm package which handles:
- Sparse clone of upstream `camunda/camunda` repo
- `SwaggerParser.bundle()` to merge multi-file YAML
- Schema augmentation from all upstream YAML files
- Path-local `$ref` normalization via signature matching
- Path-local `$ref` dereferencing (--deref-path-local, required for Microsoft.OpenApi)
- `SPEC_REF` env var pass-through for CI overrides

## Where things live

- Bundled spec input to the generator: `external-spec/bundled/rest-api.bundle.json`
- Generator output (checked/used by build): `src/Camunda.Orchestration.Sdk/Generated/`
- Generator logic: `src/Camunda.Orchestration.Sdk.Generator/CSharpClientGenerator.cs`
- Runtime infrastructure: `src/Camunda.Orchestration.Sdk/Runtime/`
- Main client class: `src/Camunda.Orchestration.Sdk/CamundaClient.cs`
- Unit tests: `test/Camunda.Orchestration.Sdk.Tests/`
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

Uses [semantic-release](https://github.com/semantic-release/semantic-release) for automated versioning and publishing (same config as JS SDK):
- Commit-driven: only `fix:`, `feat:`, `perf:`, `revert:` commits trigger a release (as patch)
- `server:` → minor bump, `server-major:` → major bump
- `chore:`, `docs:`, `ci:` commits produce no release
- Branch model: `main` = alpha prereleases, `stable/*` = stable releases
- Config: `release.config.cjs`, `commitlint.config.cjs`
- Commit messages linted via commitlint in CI (Conventional Commits required)

## Code Style & Linting

- Style rules: `.editorconfig` (Allman braces, namespace-scoped usings, PascalCase types, _camelCase private fields)
- Enforcement: `Directory.Build.props` sets `EnforceCodeStyleInBuild=true` and `AnalysisLevel=latest-recommended`
- Auto-fix generated code: `dotnet format src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj --no-restore`
- Lint check (CI gate): `dotnet format --verify-no-changes`
- Generated code (`src/Camunda.Orchestration.Sdk/Generated/`) has relaxed style rules (severity `none`)

## Quick debug checklist

1. Confirm the build works: `dotnet build`
2. Run tests: `dotnet test`
3. Lint check: `dotnet format --verify-no-changes`
4. Check generated output: `ls src/Camunda.Orchestration.Sdk/Generated/`
5. Inspect spec: `cat external-spec/bundled/rest-api.bundle.json | head -100`
