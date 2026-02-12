#!/usr/bin/env bash
# Fetch the upstream OpenAPI spec from camunda/camunda and bundle it.
# Mirrors the JS SDK's fetch-spec.ts + bundle-openapi.ts pipeline.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UPSTREAM_REPO="https://github.com/camunda/camunda.git"
UPSTREAM_BRANCH="main"
UPSTREAM_SPEC_DIR="zeebe/gateway-protocol/src/main/proto/v2"
UPSTREAM_SPEC_ENTRY="rest-api.yaml"

EXTERNAL_DIR="$REPO_ROOT/external-spec"
WORK_DIR="$EXTERNAL_DIR/tmp-clone"
UPSTREAM_LOCAL_DIR="$EXTERNAL_DIR/upstream/$UPSTREAM_SPEC_DIR"
BUNDLED_DIR="$EXTERNAL_DIR/bundled"
BUNDLED_FILE="$BUNDLED_DIR/rest-api.bundle.json"

# Allow skipping fetch
if [ "${CAMUNDA_SDK_SKIP_FETCH_SPEC:-0}" = "1" ] && [ -f "$UPSTREAM_LOCAL_DIR/$UPSTREAM_SPEC_ENTRY" ]; then
    echo "[fetch-spec] Skip fetch (CAMUNDA_SDK_SKIP_FETCH_SPEC=1) – using existing spec"
else
    echo "[fetch-spec] Fetching upstream spec from $UPSTREAM_REPO..."
    mkdir -p "$EXTERNAL_DIR"

    # Clean previous working copy
    rm -rf "$WORK_DIR"

    git clone --depth 1 --filter=blob:none --sparse "$UPSTREAM_REPO" "$WORK_DIR"
    git -C "$WORK_DIR" sparse-checkout init --no-cone
    git -C "$WORK_DIR" sparse-checkout set "/$UPSTREAM_SPEC_DIR"
    git -C "$WORK_DIR" checkout "$UPSTREAM_BRANCH"

    SOURCE_DIR="$WORK_DIR/$UPSTREAM_SPEC_DIR"
    if [ ! -f "$SOURCE_DIR/$UPSTREAM_SPEC_ENTRY" ]; then
        echo "[fetch-spec] ERROR: Upstream spec entry not found at $SOURCE_DIR/$UPSTREAM_SPEC_ENTRY"
        exit 1
    fi

    # Replace local upstream dir
    rm -rf "$UPSTREAM_LOCAL_DIR"
    mkdir -p "$UPSTREAM_LOCAL_DIR"
    cp -r "$SOURCE_DIR/"* "$UPSTREAM_LOCAL_DIR/"

    # Cleanup
    rm -rf "$WORK_DIR"
    echo "[fetch-spec] Upstream spec fetched -> $UPSTREAM_LOCAL_DIR"
fi
