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

function stableMinorFromBranch(branch) {
  // stable/<major>.<minor> (e.g. stable/8.8)
  const m = /^stable\/(\d+\.\d+)$/.exec(branch);
  return m ? m[1] : null;
}

function envCurrentStableMinor() {
  // Expected format: <major>.<minor> (e.g. 8.8)
  const v = (process.env.CAMUNDA_SDK_CURRENT_STABLE_MINOR || '').trim();
  return /^\d+\.\d+$/.test(v) ? v : null;
}

const branch = currentBranchName();
const stableMinor = stableMinorFromBranch(branch);
const currentStableMinor = envCurrentStableMinor();

function maintenanceBranchConfig(branchName, minor) {
  return {
    name: branchName,
    range: `${minor}.x`,
    channel: `${minor}-stable`,
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
  // Branch model (mirrors JS SDK):
  // - main: alpha prereleases (NuGet prerelease)
  // - stable/<major>.<minor> (current): stable releases (NuGet)
  // - stable/<major>.<minor> (other): maintenance stream
  //
  // The currently promoted stable minor is configured via `CAMUNDA_SDK_CURRENT_STABLE_MINOR`.
  branches: dedupeBranches([
    {
      name: 'main',
      prerelease: 'alpha',
      channel: 'alpha',
    },

    ...(currentStableMinor
      ? [
          {
            name: `stable/${currentStableMinor}`,
            channel: 'latest',
          },
        ]
      : []),

    ...(stableMinor && stableMinor !== currentStableMinor
      ? [maintenanceBranchConfig(branch, stableMinor)]
      : []),
  ]),
  plugins: [
    [
      '@semantic-release/commit-analyzer',
      {
        // Mutated semver policy (same as JS SDK):
        // - Patch: normal changes (including features)
        // - Minor: reserved for Camunda server minor line bumps (e.g. 8.8 -> 8.9)
        // - Major: reserved for Camunda server major line bumps (e.g. 8.x -> 9.x)
        releaseRules: [
          { type: 'feat', release: 'patch' },
          { type: 'fix', release: 'patch' },
          { type: 'perf', release: 'patch' },
          { type: 'revert', release: 'patch' },
          { breaking: true, release: 'patch' },
          { type: 'server', release: 'minor' },
          { type: 'server-major', release: 'major' },
        ],
      },
    ],
    '@semantic-release/release-notes-generator',
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
        assets: ['src/Camunda.Orchestration.Sdk/Camunda.Orchestration.Sdk.csproj'],
        message:
          'chore(release): ${nextRelease.version} [skip ci]\n\n${nextRelease.notes}',
      },
    ],
    [
      '@semantic-release/github',
      {
        assets: ['release-assets/*.nupkg', 'release-assets/*.snupkg'],
        successComment:
          'Released in `${nextRelease.gitTag}` (NuGet: `Camunda.Orchestration.Sdk@${nextRelease.version}`).',
      },
    ],
  ],
};
