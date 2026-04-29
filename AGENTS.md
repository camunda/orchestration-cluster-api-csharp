# AGENTS.md

> **Note:** This file delegates to a central AGENTS.md. Read and apply it before proceeding.

**URL:**
https://raw.githubusercontent.com/camunda/.github/refs/heads/main/AGENTS.md

Treat the central file's contents as if they were written directly in this file.
Instructions below extend those guidelines and take precedence if there is any conflict.

## Repo-specific instructions

### Role & boundary

This repo generates the C# SDK (`Camunda.Orchestration.Sdk`) from a multi-file OpenAPI spec sourced from the Camunda monorepo. The published SDK is consumed by .NET application code.

Upstream dependencies — when they misbehave, fix them at the source rather than working around them here:

- [`camunda-schema-bundler`](https://github.com/camunda/camunda-schema-bundler) — fetches and bundles the upstream OpenAPI spec.
- [`camunda/camunda`](https://github.com/camunda/camunda) — source of the OpenAPI spec.

**Path map:**

| Path                                                              | Ownership and intent                                                                  |
| ----------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `src/Camunda.Orchestration.Sdk/`                                  | SDK package root — main edit surface for hand-written runtime + partial-class infra. |
| `src/Camunda.Orchestration.Sdk/Runtime/`                          | Hand-written runtime (HTTP, retry, backpressure, auth, polling, hydration).           |
| `src/Camunda.Orchestration.Sdk/CamundaClient.cs`                  | Hand-written infrastructure half of the partial `CamundaClient` class.                |
| `src/Camunda.Orchestration.Sdk/Generated/`                        | **Generated.** Produced by the generator project. Never hand-edit.                    |
| `src/Camunda.Orchestration.Sdk.Generator/`                        | The generator (C# console app) — primary edit surface for fixing generator output.    |
| `src/Camunda.Orchestration.Sdk.Generator/CSharpClientGenerator.cs`| Core generator logic.                                                                 |
| `external-spec/bundled/rest-api.bundle.json`                      | Bundled OpenAPI spec — generator input.                                               |
| `external-spec/upstream/`                                         | Sparse clone of the upstream repo. Transient; never commit `.tmp*` paths.             |
| `test/Camunda.Orchestration.Sdk.Tests/`                           | Unit tests.                                                                           |
| `docs/examples/`                                                  | Compilable examples that source `README.md` snippets.                                 |
| `scripts/`                                                        | Build, bundle, and sync helpers.                                                      |
| `.github/workflows/`                                              | CI (`ci.yml`) and release (`release.yml`).                                            |

## Generator pipeline

### Key flows (what to run)

- Build (fetches upstream spec): `bash scripts/build.sh`
- Build pinned to a specific spec ref: `SPEC_REF=my-branch bash scripts/build.sh`
- Build using already-fetched spec (fast local iteration): `bash scripts/build-local.sh`
- Only fetch & bundle spec: `bash scripts/bundle-spec.sh`
- Only bundle (skip fetch, use local spec): `CAMUNDA_SDK_SKIP_FETCH_SPEC=1 bash scripts/bundle-spec.sh`
- Only regenerate SDK sources: `dotnet run --project src/Camunda.Orchestration.Sdk.Generator`

### Pipeline (high level)

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
- Path-local `$ref` dereferencing (`--deref-path-local`, required for `Microsoft.OpenApi`)
- `SPEC_REF` env var pass-through for CI overrides

### Where things live

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
- `CamundaClient.cs` — infrastructure: `HttpClient`, auth, retry, backpressure, JSON serialization
- `Generated/CamundaClient.Generated.cs` — auto-generated API methods (one per OpenAPI operation)
- `Generated/Models.Generated.cs` — auto-generated model classes and enums from OpenAPI schemas

Runtime components:
- `ConfigurationHydrator` — environment variable hydration mirroring the JS SDK
- `AuthHandler` — `DelegatingHandler` for OAuth/Basic auth
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

## Code Style & Linting

- Style rules: `.editorconfig` (Allman braces, namespace-scoped usings, PascalCase types, `_camelCase` private fields)
- Enforcement: `Directory.Build.props` sets `EnforceCodeStyleInBuild=true` and `AnalysisLevel=latest-recommended`
- Auto-fix generated code: `dotnet format src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj --no-restore`
- Lint check (CI gate): `dotnet format --verify-no-changes`
- Generated code (`src/Camunda.Orchestration.Sdk/Generated/`) has relaxed style rules (severity `none`)

## Commit message guidelines

We use Conventional Commits (enforced by commitlint in CI).

Format:

```
<type>(optional scope): <subject>

<body>

BREAKING CHANGE: <explanation>
```

Allowed type values (common set):

```
feat
fix
chore
docs
style
refactor
test
ci
build
perf
```

Rules:

- **Subject length: 5–100 characters.** Enforced by `subject-max-length` in `commitlint.config.cjs`; CI fails the lint job on longer subjects. Em-dashes (`—`) and other multi-byte characters count by character count, not byte count. Keep subjects concise — push detail into the body.
- Use imperative mood ("add support", not "added support").
- Lowercase subject (except proper nouns). No PascalCase subjects.
- Keep subject concise; body can include details, rationale, links.
- Prefix breaking changes with `BREAKING CHANGE:` either in body or footer.

### Review-comment fix-ups

Commits that address PR review comments must use the `chore` type (e.g. `chore:` or `chore(<scope>):`), **not** the `fix` type.
`fix` commits trigger a patch release and a CHANGELOG entry — review iterations are not user-facing bug fixes.

```
# Correct
chore: address review comments — use logger.json for dry-run

# Wrong — will pollute the CHANGELOG
fix: address review comments — use logger.json for dry-run
```

### Separate generator changes from regenerated output

When a change modifies the generator (`src/Camunda.Orchestration.Sdk.Generator/`, bundler integration, build scripts) **and** that change causes `src/Camunda.Orchestration.Sdk/Generated/*` to differ, **split the work into two commits**:

1. **First commit** — generator change only: generator source / scripts / runtime templates / test edits. No `Generated/*` changes.
2. **Second commit** — regenerated output: `src/Camunda.Orchestration.Sdk/Generated/*` produced by running the pipeline against the first commit.

Why:

- **Cherry-picks stay clean.** Backports to `stable/*` only need the generator commit; the target branch's release CI regenerates `Generated/*` itself. Mixing the two means the cherry-pick drags generated diff through, which conflicts with whatever generated state the target branch has.
- **Reviewers can read the change.** Generator commits are small and meaningful; regenerated commits are large and mechanical. Mixing them makes the review effectively unreadable.
- **`git blame` stays useful** for both surfaces.

Naming convention for the second commit:

```
chore(gen): regenerate Generated/ for <short summary of generator change>
```

If `bash scripts/build.sh` (or `bash scripts/build-local.sh`) modifies `src/Camunda.Orchestration.Sdk/Generated/*` after the generator commit, `git add` that directory and commit it separately — do **not** amend it back into the generator commit.

The pre-push checklist below still applies: always run the full build before pushing, and commit any regenerated drift before the push.

## Pre-push checklist

Before pushing any commits, **always** run a full build. This:

1. Regenerates `src/Camunda.Orchestration.Sdk/Generated/` from the bundled spec
2. Compiles the library and tests
3. Verifies code style (`dotnet format --verify-no-changes`)
4. Runs the unit tests

If the build modifies any files (e.g. `Generated/*` drift, README snippet drift), commit those changes before pushing — and respect the **separate-commits rule** above when the modified files are under `Generated/`.

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

### There are no flaky tests

We do not acknowledge the existence of "flaky tests". A test that passes sometimes and fails other times is reporting one of two things:

1. **A test defect** — the test contains a race, an unbounded timeout, an order-of-operation assumption, an unsynchronised readiness signal, or a dependency on wall-clock timing. Fix the test so its outcome is deterministic for the behaviour it claims to assert.
2. **A product defect** — the production code has a race, a missed signal, an unhandled error path, or a resource it leaks under load. Fix the product.

Either way, an intermittent failure is a real defect that must be diagnosed and fixed before the change merges. Do not retry the CI job, mark the test `[Skip]`, or describe the failure as "flaky" or "unrelated" in the PR description. "Re-run and hope" is a coping strategy, not engineering.

When triaging an intermittent CI failure:

- Reproduce locally if possible (loops, resource pressure, timeout reduction). If you cannot reproduce, reason from first principles about what *could* differ between local and CI (load, filesystem semantics, signal delivery latency, parallel test interaction).
- Identify the specific race or assumption. Common shapes: polling for an output line that is printed *before* the relevant handler is registered; timeouts that double as correctness assertions; tests that share a temp directory across runs; tests that depend on event ordering across two processes.
- Pick category 1 vs category 2 explicitly in the fix commit message, and explain which signal the test was previously relying on and which deterministic signal it now relies on.
- If timeouts must be generous to absorb runner load, the timeout is a safety net — not a correctness signal. State this in a comment so future maintainers don't tighten it back into a race.

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
