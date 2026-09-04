#!/bin/bash
# Called by the "Publish (semantic-release)" workflow step.
#
# @semantic-release/git commits the version bump (CHANGELOG.md + the .csproj)
# and pushes it straight to the release branch as part of semantic-release's
# "prepare" lifecycle step -- strictly *before* the NuGet push and the GitHub
# release, which only happen afterwards in the "publish" step (see the plugin
# order in release.config.cjs). If another PR merges to the branch while this
# is running (observed twice with a Renovate PR racing the release), that
# push is rejected as non-fast-forward and the whole run fails -- even though
# nothing has actually been published yet. Rather than relying on the next
# run to self-heal, retry in place: reset to the now-current branch tip and
# re-run semantic-release from a clean state. This is safe because:
#   - the only local commit is the version-bump commit semantic-release just
#     made and failed to push, so nothing else depends on it;
#   - semantic-release recomputes the next version from git tags on every
#     run, so a clean retry is idempotent;
#   - the push happens in "prepare", strictly before the NuGet push or GitHub
#     release ("publish"), so a losing race never leaves a partial publish
#     for a retry to duplicate.
set -euo pipefail

BRANCH="${GITHUB_REF_NAME:-$(git rev-parse --abbrev-ref HEAD)}"
MAX_ATTEMPTS=3
LOG="semantic-release-attempt.out"

for attempt in $(seq 1 "$MAX_ATTEMPTS"); do
  echo "semantic-release attempt ${attempt}/${MAX_ATTEMPTS}"

  set +e
  npx semantic-release 2>&1 | tee "$LOG"
  status=$?
  set -e

  if [ "$status" -eq 0 ]; then
    exit 0
  fi

  if [ "$attempt" -eq "$MAX_ATTEMPTS" ]; then
    echo "::error::semantic-release failed after ${attempt} attempt(s); giving up."
    exit "$status"
  fi

  if grep -qE '(non-fast-forward|fetch first|failed to push some refs)' "$LOG"; then
    echo "Push to ${BRANCH} was rejected (a concurrent merge landed while releasing); resetting to origin/${BRANCH} and retrying."
    git fetch origin "${BRANCH}"
    # Safe: the only local commit is the version-bump commit semantic-release
    # just made and failed to push -- nothing else depends on it.
    git reset --hard "origin/${BRANCH}"
    # release-assets/ is gitignored, so `reset --hard` above doesn't clear it.
    # Remove the stale pack output so a retry with a different next version
    # doesn't leave both the old and new .nupkg for publish-nuget.sh to push.
    rm -rf release-assets
    sleep 5
  else
    echo "::error::semantic-release failed for a reason other than the push race; not retrying."
    exit "$status"
  fi
done
