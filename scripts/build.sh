#!/usr/bin/env bash
# Full build pipeline: fetch spec → bundle → generate → build → test
# Mirrors the JS SDK's `npm run build` pipeline.
#
# Override the upstream spec ref (branch/tag/SHA):
#   SPEC_REF=my-branch bash scripts/build.sh
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

echo "=== Camunda C# SDK Build ==="
if [ "${SPEC_REF:-main}" != "main" ]; then
    echo "    SPEC_REF=${SPEC_REF}"
fi

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
dotnet run --project src/Camunda.Orchestration.Sdk.Generator

# Step 4: Format generated code
echo ""
echo "--- Step 4: Format generated code ---"
dotnet format src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj --no-restore

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
dotnet test test/Camunda.Orchestration.Sdk.Tests --configuration Release --no-build

 # Step 8: Build documentation
echo ""
echo "--- Step 8: Build documentation ---"
bash scripts/build-docs.sh

echo ""
echo "=== Build complete ==="
