#!/usr/bin/env bash
# Fails if the SDK breaks compatibility with the last published release of its major.
#
# Packs Camunda.Orchestration.Sdk with .NET package validation (ApiCompat) against the
# newest stable NuGet release of the same major. Any change that would stop existing
# customer code compiling or binding (removed or changed members, nullability changes,
# renumbered enum values, ...) fails the check. Additions pass.
#
# An intentional break is recorded in
# src/Camunda.Orchestration.Sdk/CompatibilitySuppressions.xml. Changing that file in a PR
# requires the `breaking-change-approved` label (enforced by the api-compat CI job).
#
# Usage:
#   bash scripts/check-api-compat.sh                         # check
#   bash scripts/check-api-compat.sh --generate-suppressions # write the suppression file
#
# Environment:
#   API_COMPAT_MAJOR     major to compare against (default: from stable/<N> branch, else
#                        from the csproj <Version>)
#   API_COMPAT_BASELINE  exact baseline version (skips the NuGet lookup)
set -euo pipefail

cd "$(dirname "$0")/.."

PROJECT=src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj
PACKAGE_ID=camunda.orchestration.sdk
SUPPRESSION_FILE=src/Camunda.Orchestration.Sdk/CompatibilitySuppressions.xml

generate=false
if [ "${1:-}" = "--generate-suppressions" ]; then
  generate=true
fi

major="${API_COMPAT_MAJOR:-}"
if [ -z "$major" ]; then
  branch="${GITHUB_BASE_REF:-${GITHUB_REF_NAME:-$(git rev-parse --abbrev-ref HEAD 2>/dev/null || true)}}"
  if [[ "$branch" =~ ^stable/([0-9]+)$ ]]; then
    major="${BASH_REMATCH[1]}"
  else
    major=$(sed -nE 's|.*<Version>([0-9]+)\..*</Version>.*|\1|p' "$PROJECT" | head -1)
  fi
fi
if [ -z "$major" ]; then
  echo "::error::Could not determine the SDK major version to compare against." >&2
  exit 1
fi

baseline="${API_COMPAT_BASELINE:-}"
if [ -z "$baseline" ]; then
  versions=$(curl -fsSL --retry 3 "https://api.nuget.org/v3-flatcontainer/${PACKAGE_ID}/index.json")
  baseline=$(echo "$versions" | python3 -c '
import json, sys
major = sys.argv[1]
vs = [v for v in json.load(sys.stdin)["versions"] if "-" not in v and v.split(".")[0] == major]
vs.sort(key=lambda v: tuple(int(p) for p in v.split(".")))
print(vs[-1] if vs else "")
' "$major")
fi

if [ -z "$baseline" ]; then
  echo "No stable ${major}.x release on NuGet yet; nothing to compare against. Skipping API compatibility check."
  exit 0
fi

echo "Checking API compatibility against Camunda.Orchestration.Sdk ${baseline} (major ${major})."

out_dir=$(mktemp -d)
trap 'rm -rf "$out_dir"' EXIT

args=(
  pack "$PROJECT"
  --configuration Release
  --output "$out_dir"
  -p:EnablePackageValidation=true
  -p:PackageValidationBaselineVersion="$baseline"
  # Any version above the baseline; the package is thrown away.
  -p:Version="${major}.999.0-apicompat"
)
if [ "$generate" = true ]; then
  args+=(-p:ApiCompatGenerateSuppressionFile=true)
fi

if dotnet "${args[@]}"; then
  if [ "$generate" = true ]; then
    echo "Wrote ${SUPPRESSION_FILE}. Commit it, and add the 'breaking-change-approved' label to the PR."
  else
    echo "No breaking API changes against ${baseline}."
  fi
  exit 0
fi

cat >&2 <<EOF
::error::Breaking API change against the published Camunda.Orchestration.Sdk ${baseline}.
Existing customer code may stop compiling. Restore the published API if you can.
If the break is intentional (for example, a bug fix that needs it):
  1. Run: bash scripts/check-api-compat.sh --generate-suppressions
  2. Commit ${SUPPRESSION_FILE}
  3. Add the 'breaking-change-approved' label to the PR, and describe the break in the PR.
EOF
exit 1
