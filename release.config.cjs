const base = require('@camunda8/sdk-infra/configs/release.config.base.cjs');

module.exports = {
  ...base,
  plugins: [
    ...base.plugins,
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
