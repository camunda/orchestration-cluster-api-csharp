#!/usr/bin/env bash
# Full build pipeline: fetch spec → bundle → generate → build → test
# Mirrors the JS SDK's `npm run build` pipeline.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

echo "=== Camunda C# SDK Build ==="

# Step 1: Fetch upstream spec
echo ""
echo "--- Step 1: Fetch spec ---"
bash scripts/fetch-spec.sh

# Step 2: Bundle spec
echo ""
echo "--- Step 2: Bundle spec ---"
bash scripts/bundle-spec.sh

# Step 3: Generate C# client
echo ""
echo "--- Step 3: Generate SDK ---"
dotnet run --project src/Camunda.Client.Generator

# Step 4: Format generated code
echo ""
echo "--- Step 4: Format generated code ---"
dotnet format src/Camunda.Client/Camunda.Client.csproj --no-restore

# Step 5: Build
echo ""
echo "--- Step 5: Build ---"
dotnet build --configuration Release

# Step 6: Lint check (verify formatting matches .editorconfig)
echo ""
echo "--- Step 6: Lint check ---"
dotnet format --verify-no-changes

# Step 7: Unit tests (acceptance gate — integration tests are separate)
echo ""
echo "--- Step 7: Unit tests ---"
dotnet test test/Camunda.Client.Tests --configuration Release --no-build

echo ""
echo "=== Build complete ==="
