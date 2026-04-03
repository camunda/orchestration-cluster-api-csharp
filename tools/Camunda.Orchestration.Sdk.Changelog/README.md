# C# SDK Breaking Changes Detector

Roslyn-based tool that compares two versions of the generated C# SDK and reports breaking changes, warnings, and additive changes.

## How it works

1. **Parse** — Uses Roslyn to extract the public API surface from generated `.cs` files (classes, enums, structs, methods, polymorphic hierarchies, implicit conversions)
2. **Classify** — Determines whether each type is used in request or response position (from client method signatures + suffix heuristics)
3. **Diff** — Compares old vs new surfaces with role-aware severity rules
4. **Report** — Generates Markdown or JSON output

## Usage

```
dotnet run --project tools/Camunda.Orchestration.Sdk.Changelog -- [options]
```

### Required

| Flag | Description |
|------|-------------|
| `--old <path>` | Path to old generated `.cs` file(s) or directory |
| `--new <path>` | Path to new generated `.cs` file(s) or directory |

### Options

| Flag | Default | Description |
|------|---------|-------------|
| `--old-version <name>` | `"old"` | Label for old version |
| `--new-version <name>` | `"new"` | Label for new version |
| `--format <fmt>` | `markdown` | Output format: `markdown` or `json` |
| `--output <path>` | stdout | Write report to file |

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | No breaking changes |
| 1 | Breaking changes detected |
| 2 | Usage error |

## Example: Compare stable/8.9 vs main

```bash
# 1. Build from stable/8.9 and snapshot the generated code
SPEC_REF=stable/8.9 bash scripts/build.sh
mkdir -p /tmp/baseline-8.9
cp src/Camunda.Orchestration.Sdk/Generated/*.cs /tmp/baseline-8.9/

# 2. Build from main
SPEC_REF=main bash scripts/build.sh

# 3. Compare
dotnet run --project tools/Camunda.Orchestration.Sdk.Changelog -- \
  --old /tmp/baseline-8.9/ \
  --new src/Camunda.Orchestration.Sdk/Generated/ \
  --old-version stable/8.9 --new-version main

# 4. Or output JSON to a file
dotnet run --project tools/Camunda.Orchestration.Sdk.Changelog -- \
  --old /tmp/baseline-8.9/ \
  --new src/Camunda.Orchestration.Sdk/Generated/ \
  --old-version stable/8.9 --new-version main \
  --format json --output report.json
```

## What it detects

### Types

| Change | Severity |
|--------|----------|
| Type removed | Breaking |
| Type added | Additive |
| Base class changed | Breaking |
| Interface removed | Breaking |
| Interface added | Additive |

### Properties

| Change | Severity |
|--------|----------|
| Property removed | Breaking (warning if optional request field) |
| Required property added to request type | Breaking |
| Optional property added | Additive |
| Property type changed | Breaking |
| Response property became optional | Breaking (missing null checks / NPE risk) |
| Request property became required | Breaking |
| JSON property name changed | Breaking (wire-level) |

### Enums

| Change | Severity |
|--------|----------|
| Enum removed | Breaking |
| Enum added | Additive |
| Enum member removed | Breaking |
| Enum member added | Additive |
| Enum member marked `[Obsolete]` | Info |
| Enum member JSON name changed | Breaking |

### Methods (CamundaClient)

| Change | Severity |
|--------|----------|
| Method removed | Breaking |
| Method added | Additive |
| Return type changed | Breaking |
| Parameter signature changed | Breaking |

### Polymorphic types

| Change | Severity |
|--------|----------|
| Discriminator changed | Breaking |
| Derived type removed | Breaking |
| Derived type added | Additive |

### Structs (semantic keys / unions)

| Change | Severity |
|--------|----------|
| Struct removed | Breaking |
| Struct added | Additive |
| Implicit conversion removed | Breaking |
| Implicit conversion added | Additive |

## Role-aware severity

The tool classifies types as **request** or **response** based on:

1. **Structural inference** — client method parameter types → request; return types → response; then transitively via property types
2. **Suffix heuristics** — `*Request`, `*Filter`, `*Query` → request; `*Response`, `*Result` → response

The same change has different severity depending on role. For example, adding a required property to a request type is breaking (callers must now supply it), while adding an optional property to a response type is additive.
