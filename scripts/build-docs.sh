#!/usr/bin/env bash
# Generate API documentation using DocFX.
# Analogous to the JS SDK's `npm run docs:api` (typedoc).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOCS_DIR="$REPO_ROOT/docs"

# Restore local .NET tools (docfx)
dotnet tool restore

# Ensure the library builds with XML doc comments
echo "[docs] Building library with XML documentation..."
dotnet build "$REPO_ROOT/src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj" --configuration Release

# Compile examples (regression guard — fails if API contracts break)
echo "[docs] Compiling API examples..."
dotnet build "$REPO_ROOT/docs/examples/Examples.csproj" --configuration Release

# Install docfx as a local tool if not already installed
if ! dotnet tool list --local 2>/dev/null | grep -q docfx; then
    echo "[docs] Installing DocFX as a local .NET tool..."
    dotnet tool install --local docfx
fi

# Clean previous output
rm -rf "$DOCS_DIR/_site" "$DOCS_DIR/api"

echo "[docs] Running DocFX metadata extraction..."
cd "$REPO_ROOT"
dotnet tool run docfx metadata docs/docfx.json

# Create the API index page (metadata step won't create this)
cat > "$DOCS_DIR/api/index.md" << 'EOF'
# API Reference

Browse the auto-generated API documentation for the Camunda C# SDK.

## Namespaces

- **Camunda.Orchestration.Sdk** — Main SDK namespace containing `CamundaClient` and the `Camunda` factory
- **Camunda.Orchestration.Sdk.Runtime** — Configuration, authentication, retry, backpressure, and error types
- **Camunda.Orchestration.Sdk.Api** — Auto-generated model classes and enums from the OpenAPI spec
EOF

echo "[docs] Building documentation site..."
dotnet tool run docfx build docs/docfx.json

echo "[docs] Pruning TOC (keep method sub-items only for CamundaClient)..."
python3 "$REPO_ROOT/scripts/prune-toc.py" "$DOCS_DIR/_site/api/toc.json"

echo "[docs] Documentation generated -> $DOCS_DIR/_site"
