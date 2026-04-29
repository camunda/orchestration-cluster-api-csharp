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
- Standard semantic versioning: `fix:`/`perf:`/`revert:` → patch, `feat:` → minor, `BREAKING CHANGE` → major
- `chore:`, `docs:`, `ci:` commits produce no release
- Branch model: `main` = alpha prereleases, `stable/<major>` (current) = stable releases, `stable/<major>` (older) = maintenance
- SDK major tracks Camunda server minor (server 8.9 → SDK 9.x). Current stable major set via `CAMUNDA_SDK_CURRENT_STABLE_MAJOR` repo variable.
- Config: `release.config.cjs`, `commitlint.config.cjs`
- Commit messages linted via commitlint in CI (Conventional Commits required)
- **Subject length limit: 100 characters max** (`subject-max-length` in `commitlint.config.cjs`). CI fails the lint job on longer subjects. Em-dashes count as one character; multi-byte characters count by their character count, not byte count. Keep subjects concise — push detail into the body.

## Code Style & Linting

- Style rules: `.editorconfig` (Allman braces, namespace-scoped usings, PascalCase types, _camelCase private fields)
- Enforcement: `Directory.Build.props` sets `EnforceCodeStyleInBuild=true` and `AnalysisLevel=latest-recommended`
- Auto-fix generated code: `dotnet format src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj --no-restore`
- Lint check (CI gate): `dotnet format --verify-no-changes`
- Generated code (`src/Camunda.Orchestration.Sdk/Generated/`) has relaxed style rules (severity `none`)

## Bug fix process (red/green refactor)

Every bug fix **must** follow the red/green refactor discipline:

1. **Red** — Write a failing test **first**, before changing any production code. The test must fail for the reason you expect (the bug). Commit this separately or demonstrate the failure clearly in the PR.
2. **Green** — Apply the minimal production fix that makes the test pass.
3. **Refactor** (optional) — Clean up while keeping all tests green.

### Test scope: target the defect class, not just the instance

The regression test must be broad enough to detect the **class of defect**, not only the specific instance you are fixing. For example, if the bug is "derived type X re-declares the discriminator property", the test should verify that **no** derived type re-declares a discriminator property — not just type X.

A test that only covers the exact instance provides weaker protection: the same category of bug can recur in a different type without being caught.

### Why

- The failing test **proves** the test can detect this category of defect.
- The green step **proves** the fix resolves it.
- A class-scoped test acts as a durable regression guard against future reintroduction of the same pattern.

## Quick debug checklist

1. Confirm the build works: `dotnet build`
2. Run tests: `dotnet test`
3. Lint check: `dotnet format --verify-no-changes`
4. Check generated output: `ls src/Camunda.Orchestration.Sdk/Generated/`
5. Inspect spec: `cat external-spec/bundled/rest-api.bundle.json | head -100`

## README Code Examples

### API spec examples: prefer ergonomic helpers

The `examples/operation-map.json` file maps OpenAPI `operationId`s to example regions that are displayed in the Camunda docs API reference (via `docusaurus-plugin-openapi-docs`).

When an ergonomic helper method exists for a generated operation, the operation-map entry **must** point to the helper — not to the raw generated method. Users should see the best developer experience by default.

Example: `createDeployment` maps to `DeployResourcesFromFiles` (file-path helper) instead of the raw `CreateDeployment` (requires manual `MultipartFormDataContent`). This preference is consistent across all three SDK repos (C#, TypeScript, Python).

Code blocks in `README.md` are **injected from compilable example files** — do not edit them inline.

- **Source of truth**: `docs/examples/ReadmeExamples.cs` (and other `.cs` files in `docs/examples/`)
- **Sync script**: `scripts/sync-readme-snippets.py`
- **CI gate**: `python3 scripts/sync-readme-snippets.py --check` (fails if README is out of sync)
- **Examples project**: `docs/examples/` is a standalone project compiled by `dotnet build docs/examples/`

### How it works

1. Wrap code in a `.cs` file under `docs/examples/` with `// <RegionName>` / `// </RegionName>` tags.
2. In `README.md`, place `<!-- snippet-source: docs/examples/File.cs | regions: RegionName -->` immediately before the fenced code block.
3. Run `python3 scripts/sync-readme-snippets.py` to update README.
4. Composite regions: `<!-- snippet-source: docs/examples/File.cs | regions: A+B+C -->` concatenates regions separated by blank lines.

### Adding or updating a README example

1. Add/edit the region-tagged code in a `.cs` file under `docs/examples/`.
2. Add/verify the `<!-- snippet-source: docs/examples/File.cs | regions: RegionName -->` marker in `README.md`.
3. Run `dotnet build docs/examples/` to confirm it compiles.
4. Run `python3 scripts/sync-readme-snippets.py` to sync.

**Never edit a snippet-marked code block directly in README.md** — it will be overwritten on the next sync.
