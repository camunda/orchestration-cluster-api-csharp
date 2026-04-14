function currentBranchName() {
  if (process.env.GITHUB_REF_NAME) return process.env.GITHUB_REF_NAME;
  try {
    // eslint-disable-next-line @typescript-eslint/no-var-requires
    const { execSync } = require('node:child_process');
    return execSync('git rev-parse --abbrev-ref HEAD', { stdio: ['ignore', 'pipe', 'ignore'] })
      .toString()
      .trim();
  } catch {
    return '';
  }
}

function stableMajorFromBranch(branch) {
  // stable/<major> (e.g. stable/9)
  const m = /^stable\/(\d+)$/.exec(branch);
  return m ? m[1] : null;
}

function stableDistTagForMajor(major) {
  // npm dist-tags must NOT be a valid SemVer version or range.
  // "9" alone is a valid SemVer range, so append "-stable".
  return `${major}-stable`;
}

function envCurrentStableMajor() {
  // Expected format: integer (e.g. 9)
  const v = (process.env.CAMUNDA_SDK_CURRENT_STABLE_MAJOR || '').trim();
  return /^\d+$/.test(v) ? v : null;
}

const branch = currentBranchName();
const stableMajor = stableMajorFromBranch(branch);
const currentStableMajor = envCurrentStableMajor();

function maintenanceBranchConfig(branchName, major) {
  return {
    name: branchName,
    range: `${major}.x`,
    channel: stableDistTagForMajor(major),
  };
}

function dedupeBranches(branches) {
  const seen = new Set();
  const out = [];
  for (const b of branches) {
    const name = typeof b === 'string' ? b : b?.name;
    if (!name) continue;
    if (seen.has(name)) continue;
    seen.add(name);
    out.push(b);
  }
  return out;
}

module.exports = {
  // Branch model:
  // - main: alpha prereleases (NuGet prerelease)
  // - stable/<major> (current): stable releases (NuGet)
  // - stable/<major> (older): maintenance stream
  //
  // SDK major version tracks Camunda server minor (server 8.9 → SDK 9.x).
  // The currently promoted stable major is configured via `CAMUNDA_SDK_CURRENT_STABLE_MAJOR`.
  //
  // semantic-release requires ≥1 "release branch" (no `range`, no `prerelease`).
  // Branch type classification:
  //   `range`      → maintenance
  //   `prerelease` → pre-release
  //   neither      → release
  //
  // main is always prerelease(alpha). The current stable line is the release
  // branch (no range, no prerelease). Older stable lines are maintenance.
  // This avoids version-range conflicts when main and the current stable
  // share the same major (e.g. main produces 9.0.0-alpha.N while stable/9
  // produces 9.0.0 stable).
  branches: dedupeBranches([
    // main: always prerelease — produces alpha versions on the 'alpha' channel.
    { name: 'main', prerelease: 'alpha', channel: 'alpha' },

    // Current stable line: release branch (no range). This is the "release"
    // branch that semantic-release requires (≥1 branch with no range/prerelease).
    ...(currentStableMajor
      ? [
          {
            name: `stable/${currentStableMajor}`,
            channel: 'latest',
          },
        ]
      : []),

    // Any other stable/* branch publishes as a maintenance line.
    ...(stableMajor && stableMajor !== currentStableMajor
      ? [maintenanceBranchConfig(branch, stableMajor)]
      : []),
  ]),
  plugins: [
    [
      '@semantic-release/commit-analyzer',
      {
        // Standard semantic versioning:
        // - Patch: fix, perf, revert
        // - Minor: feat
        // - Major: breaking changes
        releaseRules: [
          { type: 'feat', release: 'minor' },
          { type: 'fix', release: 'patch' },
          { type: 'perf', release: 'patch' },
          { type: 'revert', release: 'patch' },
          { breaking: true, release: 'major' },
        ],
      },
    ],
    '@semantic-release/release-notes-generator',
    '@semantic-release/changelog',
    [
      '@semantic-release/exec',
      {
        prepareCmd:
          'bash scripts/prepare-release.sh "${nextRelease.version}"',
        publishCmd:
          'bash scripts/publish-nuget.sh',
      },
    ],
    [
      '@semantic-release/git',
      {
        assets: ['CHANGELOG.md', 'src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj'],
        message:
          'chore(release): ${nextRelease.version} [skip ci]\n\n${nextRelease.notes}',
      },
    ],
    [
      '@semantic-release/github',
      {
        assets: ['release-assets/*.nupkg', 'release-assets/*.snupkg'],
        successComment: false,
      },
    ],
  ],
};
