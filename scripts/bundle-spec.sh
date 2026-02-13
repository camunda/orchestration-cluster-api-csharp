#!/usr/bin/env bash
# Bundle multi-file OpenAPI YAML spec into a single JSON file.
# Uses a Node.js script that mirrors the JS SDK's bundling logic:
# SwaggerParser.bundle() + schema augmentation + ref normalization.
# Requires Node.js to be installed.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

if ! command -v node &> /dev/null; then
    echo "[bundle-spec] node not found; install Node.js first"
    exit 1
fi

node "$REPO_ROOT/scripts/bundle-spec.mjs"
