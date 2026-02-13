#!/bin/bash
# Called by @semantic-release/exec publishCmd.
# Pushes the NuGet package to nuget.org.
set -euo pipefail

if [ -z "${NUGET_API_KEY:-}" ]; then
  echo "::error::NUGET_API_KEY is not set"
  exit 1
fi

echo "Publishing to NuGet..."
dotnet nuget push release-assets/*.nupkg \
  --api-key "$NUGET_API_KEY" \
  --source https://api.nuget.org/v3/index.json \
  --skip-duplicate

echo "NuGet publish complete"
