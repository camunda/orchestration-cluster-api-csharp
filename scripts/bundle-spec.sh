#!/usr/bin/env bash
# Bundle multi-file OpenAPI YAML spec into a single JSON file using swagger-cli.
# Requires Node.js (for swagger-cli) to be installed.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UPSTREAM_SPEC_DIR="zeebe/gateway-protocol/src/main/proto/v2"
ENTRY="$REPO_ROOT/external-spec/upstream/$UPSTREAM_SPEC_DIR/rest-api.yaml"
BUNDLED_DIR="$REPO_ROOT/external-spec/bundled"
BUNDLED_FILE="$BUNDLED_DIR/rest-api.bundle.json"

if [ ! -f "$ENTRY" ]; then
    echo "[bundle-spec] ERROR: Spec entry not found at $ENTRY"
    echo "[bundle-spec] Run scripts/fetch-spec.sh first"
    exit 1
fi

mkdir -p "$BUNDLED_DIR"

# Use swagger-cli (npx) to bundle. Install swagger-cli if needed.
if ! command -v npx &> /dev/null; then
    echo "[bundle-spec] npx not found; install Node.js first"
    exit 1
fi

echo "[bundle-spec] Bundling $ENTRY -> $BUNDLED_FILE"
npx @apidevtools/swagger-cli@4 bundle "$ENTRY" --outfile "$BUNDLED_FILE" --type json

echo "[bundle-spec] Bundle complete"
