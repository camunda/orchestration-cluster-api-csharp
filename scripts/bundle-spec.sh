#!/usr/bin/env bash
# Fetch (optionally) and bundle the upstream OpenAPI spec using camunda-schema-bundler.
#
# Environment variables:
#   SPEC_REF                     Git ref to fetch (default: main). Passed as --ref.
#   CAMUNDA_SDK_SKIP_FETCH_SPEC  If "1", use --spec-dir with already-fetched spec (skip fetch).
#
# Usage:
#   bash scripts/bundle-spec.sh                          # fetch from main + bundle
#   SPEC_REF=stable/8.8 bash scripts/bundle-spec.sh      # fetch from stable/8.8 + bundle
#   CAMUNDA_SDK_SKIP_FETCH_SPEC=1 bash scripts/bundle-spec.sh  # bundle only (no fetch)
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SPEC_DIR="external-spec/upstream/zeebe/gateway-protocol/src/main/proto/v2"
BUNDLED_SPEC="external-spec/bundled/rest-api.bundle.json"
METADATA="external-spec/bundled/spec-metadata.json"

cd "$REPO_ROOT"

if [ "${CAMUNDA_SDK_SKIP_FETCH_SPEC:-0}" = "1" ]; then
    echo "[bundle-spec] Bundling with local spec (skip fetch)"
    npx camunda-schema-bundler \
        --spec-dir "$SPEC_DIR" \
        --deref-path-local \
        --output-spec "$BUNDLED_SPEC" \
        --output-metadata "$METADATA"
else
    REF="${SPEC_REF:-main}"
    echo "[bundle-spec] Fetching (ref: $REF) and bundling spec"
    npx camunda-schema-bundler \
        --ref "$REF" \
        --deref-path-local \
        --output-spec "$BUNDLED_SPEC" \
        --output-metadata "$METADATA"
fi
