#!/usr/bin/env node
import { execSync } from 'node:child_process';
import semanticRelease from 'semantic-release';

function safeCurrentBranch() {
  if (process.env.GITHUB_REF_NAME) return process.env.GITHUB_REF_NAME;
  try {
    return execSync('git rev-parse --abbrev-ref HEAD', { stdio: ['ignore', 'pipe', 'ignore'] })
      .toString()
      .trim();
  } catch {
    return '';
  }
}

async function main() {
  try {
    const branch = safeCurrentBranch();
    if (branch) console.log(`release_branch=${branch}`);
    const result = await semanticRelease({
      dryRun: true,
      ci: false,
    });
    if (result && result.nextRelease && result.nextRelease.version) {
      if (result.nextRelease.channel) console.log(`release_channel=${result.nextRelease.channel}`);
      console.log(`next_version=${result.nextRelease.version}`);
      process.exit(0);
    } else {
      console.log('next_version=');
      process.exit(0);
    }
  } catch (err) {
    console.error('[next-version] semantic-release dry-run failed');
    console.error(err);
    process.exit(1);
  }
}
main();
