#!/bin/bash
# Called by @semantic-release/exec prepareCmd.
# Updates the .csproj version, builds, tests, and packs the NuGet package.
set -euo pipefail

VERSION="$1"
CSPROJ="src/Camunda.Client/Camunda.Client.csproj"

echo "Preparing release v${VERSION}"

# Update <Version> in the .csproj
sed -i "s#<Version>.*</Version>#<Version>${VERSION}</Version>#" "$CSPROJ"
echo "Updated ${CSPROJ} to version ${VERSION}"

# Build
dotnet build --configuration Release

# Smoke tests (unit only – integration tests already passed in the generate job)
dotnet test test/Camunda.Client.Tests --configuration Release --no-build --verbosity normal

# Pack
mkdir -p release-assets
dotnet pack "$CSPROJ" \
  --configuration Release \
  --no-build \
  --output release-assets

echo "Packed release-assets/:"
ls -la release-assets/
