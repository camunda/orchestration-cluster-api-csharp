#!/usr/bin/env bash
# Build using already-fetched spec (fast local iteration).
# Equivalent to JS SDK's `npm run build:local`.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

echo "=== Camunda C# SDK Build (local) ==="

# Step 1: Bundle spec (skip fetch)
echo ""
echo "--- Step 1: Bundle spec ---"
CAMUNDA_SDK_SKIP_FETCH_SPEC=1 bash scripts/fetch-spec.sh
bash scripts/bundle-spec.sh

# Step 2: Generate C# client
echo ""
echo "--- Step 2: Generate SDK ---"
dotnet run --project src/Camunda.Orchestration.Sdk.Generator

# Step 3: Format generated code
echo ""
echo "--- Step 3: Format generated code ---"
dotnet format src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj --no-restore

# Step 4: Build
echo ""
echo "--- Step 4: Build ---"
dotnet build --configuration Release

# Step 5: Lint check (verify formatting matches .editorconfig)
echo ""
echo "--- Step 5: Lint check ---"
dotnet format --verify-no-changes

# Step 6: Unit tests (acceptance gate — integration tests are separate)
echo ""
echo "--- Step 6: Unit tests ---"
dotnet test test/Camunda.Orchestration.Sdk.Tests --configuration Release --no-build

echo ""
echo "=== Build complete ==="
