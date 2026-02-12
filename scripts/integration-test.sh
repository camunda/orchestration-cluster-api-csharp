#!/usr/bin/env bash
# Run integration tests against a Camunda engine running in Docker.
# Usage:
#   bash scripts/integration-test.sh          # start docker, run tests, stop docker
#   bash scripts/integration-test.sh --no-docker  # run tests against already-running engine
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

CAMUNDA_VERSION="${CAMUNDA_VERSION:-latest}"
NO_DOCKER=false

for arg in "$@"; do
  case "$arg" in
    --no-docker) NO_DOCKER=true ;;
  esac
done

cleanup() {
  if [[ "$NO_DOCKER" == false ]]; then
    echo ""
    echo "--- Stopping Camunda engine ---"
    CAMUNDA_VERSION="$CAMUNDA_VERSION" docker compose -f docker/docker-compose.yaml down -v 2>/dev/null || true
  fi
}

echo "=== Camunda C# SDK Integration Tests ==="

if [[ "$NO_DOCKER" == false ]]; then
  echo ""
  echo "--- Starting Camunda engine (${CAMUNDA_VERSION}) ---"
  CAMUNDA_VERSION="$CAMUNDA_VERSION" docker compose -f docker/docker-compose.yaml up -d

  trap cleanup EXIT

  echo "Waiting for engine to be healthy..."
  timeout=120
  elapsed=0
  while true; do
    if docker inspect --format='{{.State.Health.Status}}' camunda-engine 2>/dev/null | grep -q healthy; then
      echo "Engine is healthy."
      break
    fi
    if (( elapsed >= timeout )); then
      echo "ERROR: Engine did not become healthy within ${timeout}s"
      docker compose -f docker/docker-compose.yaml logs camunda 2>/dev/null | tail -30
      exit 1
    fi
    sleep 2
    elapsed=$((elapsed + 2))
  done
fi

echo ""
echo "--- Building ---"
dotnet build --configuration Release

echo ""
echo "--- Running integration tests ---"
dotnet test test/Camunda.Client.IntegrationTests \
  --configuration Release \
  --no-build \
  --logger "console;verbosity=normal"

echo ""
echo "=== Integration tests complete ==="
