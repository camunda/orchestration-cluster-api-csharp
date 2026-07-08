#!/usr/bin/env bash
# Full build pipeline: fetch + bundle spec → generate → build → test
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

# Step 1: Fetch & bundle spec (via camunda-schema-bundler)
echo ""
echo "--- Step 1: Fetch & bundle spec ---"
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

# Step 5: Build examples (type-check compilable code samples)
echo ""
echo "--- Step 5: Build examples ---"
dotnet build docs/examples/Examples.csproj --configuration Release

# Step 6: Lint check (verify formatting matches .editorconfig)
echo ""
echo "--- Step 6: Lint check ---"
dotnet format --verify-no-changes

# Step 7: Check README snippets are in sync (no auto-sync)
echo ""
echo "--- Step 7: Check README snippets are in sync ---"
python3 scripts/sync-readme-snippets.py --check

# Step 8: Check SDK example coverage (operation-map vs. bundled spec)
echo ""
echo "--- Step 8: Check SDK example coverage ---"
node scripts/check-example-coverage.js

# Step 9: Check DocFX overwrite completeness (every public method has an entry)
echo ""
echo "--- Step 9: Check overwrite completeness ---"
node scripts/check-overwrite-completeness.js

# Step 10: Unit tests (acceptance gate — integration tests are separate)
echo ""
echo "--- Step 10: Unit tests ---"
dotnet test test/Camunda.Orchestration.Sdk.Tests --configuration Release --no-build

# Step 11: Build documentation
echo ""
echo "--- Step 11: Build documentation ---"
bash scripts/build-docs.sh

echo ""
echo "=== Build complete ==="
