# Maintainer Guide

This document covers building, testing, and releasing the C# SDK. For end-user documentation, see [README.md](README.md).

## Building from Source

### Prerequisites

- .NET 8.0 SDK
- Node.js (for spec bundling via `camunda-schema-bundler`)
- Docker (for integration tests)
- Git

### Full build (fetches upstream spec)

```bash
bash scripts/build.sh
```

### Pinned to a specific spec ref

If upstream `main` breaks the spec, pin to a known-good commit, branch, or tag:

```bash
SPEC_REF=abc123 bash scripts/build.sh
SPEC_REF=my-fix-branch bash scripts/build.sh
```

### Local iteration (uses cached spec)

```bash
bash scripts/build-local.sh
```

### Individual steps

```bash
# Fetch & bundle upstream OpenAPI spec (optionally pinned)
bash scripts/bundle-spec.sh
SPEC_REF=my-branch bash scripts/bundle-spec.sh

# Bundle only (skip fetch, use already-fetched local spec)
CAMUNDA_SDK_SKIP_FETCH_SPEC=1 bash scripts/bundle-spec.sh

# Generate C# SDK from bundled spec
dotnet run --project src/Camunda.Orchestration.Sdk.Generator

# Build
dotnet build

# Unit tests (acceptance gate)
dotnet test test/Camunda.Orchestration.Sdk.Tests

# Integration tests (requires Docker)
bash scripts/integration-test.sh
```

## Generation Pipeline

1. `scripts/bundle-spec.sh` → uses `camunda-schema-bundler` to fetch upstream spec & produce `external-spec/bundled/rest-api.bundle.json`
2. `dotnet run --project src/Camunda.Orchestration.Sdk.Generator` → generate `src/Camunda.Orchestration.Sdk/Generated/*`
3. `dotnet format src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj --no-restore` → auto-fix generated code style
4. `dotnet build` → compile library + tests
5. `dotnet format --verify-no-changes` → lint gate (ensures all code is compliant)
6. `dotnet test` → run unit tests

Spec bundling uses the shared `camunda-schema-bundler` npm package which handles:
- Sparse clone of upstream `camunda/camunda` repo
- `SwaggerParser.bundle()` to merge multi-file YAML
- Schema augmentation from all upstream YAML files
- Path-local `$ref` normalization via signature matching
- Path-local `$ref` dereferencing (`--deref-path-local`, required for Microsoft.OpenApi)
- `SPEC_REF` env var pass-through for CI overrides

## Project Structure

```
├── .github/workflows/       # CI/CD (GitHub Actions)
├── external-spec/            # Upstream OpenAPI spec (fetched)
│   ├── upstream/             # Raw multi-file YAML
│   └── bundled/              # Bundled single JSON
├── scripts/                  # Build pipeline scripts
├── src/
│   ├── Camunda.Orchestration.Sdk/       # Main SDK library
│   │   ├── Runtime/          # Auth, retry, backpressure, config
│   │   └── Generated/        # Auto-generated models & client methods
│   └── Camunda.Orchestration.Sdk.Generator/  # Code generator (reads OpenAPI, emits C#)
└── test/
    ├── Camunda.Orchestration.Sdk.Tests/             # Unit tests (build gate)
    └── Camunda.Orchestration.Sdk.IntegrationTests/  # Integration tests (live engine)
```

## Testing

### Unit Tests

Unit tests run as part of the build pipeline and serve as acceptance criteria during code generation. They verify serialization, configuration, domain key constraints, and generated model correctness.

```bash
dotnet test test/Camunda.Orchestration.Sdk.Tests
```

### Integration Tests

Integration tests exercise the generated client against a live Camunda engine running in Docker. They are separate from unit tests and are not part of the build pipeline.

**Run with a single command** (starts Docker, waits for readiness, runs tests, stops Docker):

```bash
bash scripts/integration-test.sh
```

**Run against an already-running engine** (e.g. from a separate Docker Compose session):

```bash
bash scripts/integration-test.sh --no-docker
```

By default, tests connect to `http://localhost:8080/v2`. Override with `CAMUNDA_REST_ADDRESS`:

```bash
CAMUNDA_REST_ADDRESS=http://my-host:8080/v2 bash scripts/integration-test.sh --no-docker
```

**What the integration tests cover:**

- **Topology** — cluster info retrieval
- **Deployment** — BPMN resource deployment
- **Process instances** — create, search, cancel lifecycle
- **Jobs** — activation, completion, process instance state transitions
- **Domain keys** — compile-time type safety, JSON serialization round-trips, `AssumeExists` / `IsValid` validation

## Code Style & Linting

Code style is enforced via `.editorconfig` rules and Roslyn analyzers.

### Configuration files

- **`.editorconfig`** — C# formatting, naming conventions, code style preferences, and analyzer severity overrides. Generated code under `src/Camunda.Orchestration.Sdk/Generated/` has relaxed rules (severity `none`) since it is auto-generated.
- **`Directory.Build.props`** — Solution-wide MSBuild properties: `EnforceCodeStyleInBuild=true` activates Roslyn style analysis during `dotnet build`, and `AnalysisLevel=latest-recommended` uses the latest recommended analyzer rule set.

### Key style rules

- Allman-style braces, 4-space indentation
- File-scoped namespaces (`namespace Foo;`)
- `using` directives outside namespace (sorted, no blank-line separation)
- PascalCase for types/methods/properties, `_camelCase` for private fields, `I` prefix for interfaces
- `var` preferred when type is apparent (suggestion severity)

### Running the linter

```bash
# Auto-fix all violations
dotnet format

# Check without modifying (CI gate)
dotnet format --verify-no-changes

# Auto-fix only the generated project
dotnet format src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj --no-restore
```

The build scripts (`build.sh`, `build-local.sh`) automatically run `dotnet format` on generated code and verify the full solution with `--verify-no-changes`. The CI workflow includes matching steps.

### Suppressed rules

- **CA1848** (Use LoggerMessage delegates) — globally suppressed; the SDK does minimal logging and the refactoring burden is not justified.
- **CA1707** (Identifiers should not contain underscores) — suppressed for test projects; `Should_Do_Something_When_Condition` is standard xUnit naming convention.

## Pinning the Upstream Spec (SPEC_REF)

If the upstream `camunda/camunda` spec on `main` breaks the build, you can pin to a known-good git ref (branch, tag, or commit SHA).

### Locally

```bash
SPEC_REF=abc123def bash scripts/build.sh
```

### In CI (persistent override)

Set these GitHub repository variables:

| Variable | Value | Purpose |
|---|---|---|
| `SPEC_REF_OVERRIDE` | The git ref (e.g. `abc123def`) | Overrides the default `main` for all CI runs |
| `SPEC_REF_OVERRIDE_ACK` | `true` | Acknowledgement that the override is intentional |
| `SPEC_REF_OVERRIDE_EXPIRES` | `YYYY-MM-DD` | Expiry date — CI fails if today is past this date |

The expiry guard prevents stale overrides from silently pinning the spec forever.

### In CI (one-off manual run)

Both `ci.yml` and `release.yml` support `workflow_dispatch` with a `spec_ref` input. This takes priority over `SPEC_REF_OVERRIDE` for that single run.

### Priority order

1. `workflow_dispatch` input `spec_ref` (highest — manual trigger only)
2. `vars.SPEC_REF_OVERRIDE` repo variable (persistent)
3. `main` (default)

## Release Strategy

Releases are fully automated via [semantic-release](https://github.com/semantic-release/semantic-release), matching the JS SDK's configuration.

### Branch model

- **`main`** → alpha prereleases (`8.9.0-alpha.1`, `8.9.0-alpha.2`, ...)
- **`stable/<major>.<minor>`** (current) → stable releases (`8.8.0`, `8.8.1`, ...)
- **`stable/<major>.<minor>`** (older) → maintenance releases

The currently promoted stable minor is configured via the `CAMUNDA_SDK_CURRENT_STABLE_MINOR` repo variable (e.g. `8.8`).

### Version bumping (mutated semver)

This repo uses a "mutated semver" policy — same as the JS SDK:

| Commit type | Release bump | Use case |
|---|---|---|
| `fix:`, `feat:`, `perf:`, `revert:` | **patch** | Normal changes |
| `server:` | **minor** | Camunda server minor line bump (8.8 → 8.9) |
| `server-major:` | **major** | Camunda server major line bump (8.x → 9.x) |
| `chore:`, `docs:`, `ci:`, `style:` | no release | No NuGet publish |
| `BREAKING CHANGE` | **patch** | Breaking changes are patch (use `server:` / `server-major:` for line bumps) |

### Commit message format

Commit messages must follow [Conventional Commits](https://www.conventionalcommits.org/). Enforced by commitlint in CI on pull requests.

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

Examples:
```
fix: handle null response in process instance search
feat: add signal broadcast support
server: upgrade to Camunda 8.9 spec
```

### How it works

1. `@semantic-release/commit-analyzer` inspects commits since the last tag
2. If release-worthy commits exist → determines version bump
3. `@semantic-release/exec` runs `scripts/prepare-release.sh` (updates .csproj version, builds, tests, packs)
4. `@semantic-release/exec` runs `scripts/publish-nuget.sh` (pushes to NuGet)
5. `@semantic-release/git` commits the version bump to the .csproj
6. `@semantic-release/github` creates a GitHub Release with `.nupkg` assets

### Configuration files

- `release.config.cjs` — semantic-release config (branches, plugins, release rules)
- `commitlint.config.cjs` — commit message linting rules
- `scripts/next-version.mjs` — dry-run script to preview next version
- `scripts/prepare-release.sh` — builds and packs the NuGet package
- `scripts/publish-nuget.sh` — pushes to nuget.org

## Generator Architecture

The generator (`src/Camunda.Orchestration.Sdk.Generator/CSharpClientGenerator.cs`) reads the bundled OpenAPI spec and emits two files:

- `Models.Generated.cs` — model classes, enums, domain key types
- `CamundaClient.Generated.cs` — async API methods (one per OpenAPI operation)

Key generator behaviors:

- **oneOf schemas** → `abstract class` parent with `[JsonDerivedType]` attributes; variant classes inherit from the parent
- **Domain keys** — `readonly record struct` types with constraint validation, generated for string/long properties matching OpenAPI `x-]]` patterns
- **allOf with single $ref** — resolved to the referenced type (common pattern: `allOf: [{$ref: "..."}]` + description overlay)
- **Inline oneOf request bodies** — matched to component schemas by comparing required field sets
