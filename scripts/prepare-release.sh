#!/bin/bash
# Called by @semantic-release/exec prepareCmd.
# Updates the .csproj version, builds, tests, and packs the NuGet package.
set -euo pipefail

VERSION="$1"
CSPROJ="src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj"

echo "Preparing release v${VERSION}"

# --- Version line guard -------------------------------------------------------
# The SDK major tracks the Camunda server minor (README "Versioning": SDK n.x ->
# Camunda 8.n). semantic-release derives the version from commit history, so a
# stray "breaking change" note in any commit body can silently bump the line
# across a major — this is exactly how stable/9 once published v10.0.0. Refuse to
# release when the computed major does not match the branch's intended line:
#
#   stable/<N>  -> major N                  (the 8.N stable/maintenance line; exact)
#   main        -> CURRENT_STABLE_MAJOR + 1 (alpha for the next server minor)
#
# A genuinely-intended new major must be introduced deliberately (cut/seed the
# matching branch; for main, advance CAMUNDA_SDK_CURRENT_STABLE_MAJOR) — never via
# an accidental marker.
MAJOR="${VERSION%%.*}"
BRANCH="${GITHUB_REF_NAME:-$(git rev-parse --abbrev-ref HEAD 2>/dev/null || true)}"
guard_fail() {
  echo "::error::Refusing to release v${VERSION}: computed major ${MAJOR} does not match the expected major ${1} for branch '${BRANCH}'." >&2
  echo "The SDK major tracks the Camunda server minor (README 'Versioning': SDK n.x -> Camunda 8.n)." >&2
  echo "A stray 'breaking change' commit note must not cross a major. If this major is intended, introduce it deliberately (cut/seed the matching branch; for main advance CAMUNDA_SDK_CURRENT_STABLE_MAJOR)." >&2
  exit 1
}
if [[ "$BRANCH" =~ ^stable/([0-9]+)$ ]]; then
  # A stable line never crosses its major.
  EXPECTED_MAJOR="${BASH_REMATCH[1]}"
  [[ "$MAJOR" == "$EXPECTED_MAJOR" ]] || guard_fail "$EXPECTED_MAJOR"
  echo "Version line guard OK: v${VERSION} matches stable line ${EXPECTED_MAJOR}.x (branch '${BRANCH}')."
elif [[ "$BRANCH" == "main" && "${CAMUNDA_SDK_CURRENT_STABLE_MAJOR:-}" =~ ^[0-9]+$ ]]; then
  # Alpha line should be the next server minor. Block an upward drift past it;
  # tolerate a lower major (line not yet transitioned) with a warning so this
  # guard can land before main is seeded onto the next major.
  EXPECTED_MAJOR=$(( CAMUNDA_SDK_CURRENT_STABLE_MAJOR + 1 ))
  if (( MAJOR > EXPECTED_MAJOR )); then
    guard_fail "$EXPECTED_MAJOR"
  elif (( MAJOR < EXPECTED_MAJOR )); then
    echo "::warning::Releasing v${VERSION} on 'main' but the expected alpha major is ${EXPECTED_MAJOR} (CURRENT_STABLE_MAJOR+1). main has not yet been moved onto the next server line." >&2
  else
    echo "Version line guard OK: v${VERSION} matches the alpha line ${EXPECTED_MAJOR}.x (branch '${BRANCH}')."
  fi
else
  echo "Version line guard: no expected major resolved for branch '${BRANCH}'; skipping."
fi
# -----------------------------------------------------------------------------

# Update <Version> in the .csproj
sed -i "s#<Version>.*</Version>#<Version>${VERSION}</Version>#" "$CSPROJ"
echo "Updated ${CSPROJ} to version ${VERSION}"

# Build
dotnet build --configuration Release

# Smoke tests (unit only – integration tests already passed in the generate job)
dotnet test test/Camunda.Orchestration.Sdk.Tests --configuration Release --no-build --verbosity normal

# Pack
mkdir -p release-assets
dotnet pack "$CSPROJ" \
  --configuration Release \
  --no-build \
  --output release-assets

echo "Packed release-assets/:"
ls -la release-assets/
