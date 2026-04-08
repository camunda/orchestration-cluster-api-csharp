#!/usr/bin/env node

/**
 * Check example coverage: compare operationIds in the bundled OpenAPI spec
 * against entries in examples/operation-map.json.
 *
 * Exits with code 1 if any operations are missing examples.
 * Writes missing-examples.json for CI consumption.
 */

const fs = require("fs");
const path = require("path");

const rootDir = path.join(__dirname, "..");
const specPath = path.join(
  rootDir,
  "external-spec",
  "bundled",
  "rest-api.bundle.json"
);
const mapPath = path.join(rootDir, "examples", "operation-map.json");

if (!fs.existsSync(specPath)) {
  console.error(`Spec not found at ${specPath}`);
  console.error(
    "Run 'bash scripts/bundle-spec.sh' first to fetch the spec."
  );
  process.exit(2);
}

const spec = JSON.parse(fs.readFileSync(specPath, "utf8"));

if (!fs.existsSync(mapPath)) {
  console.error(`Operation map not found at ${mapPath}`);
  console.error(
    "Ensure examples/operation-map.json exists and is committed."
  );
  process.exit(2);
}

const operationMap = JSON.parse(fs.readFileSync(mapPath, "utf8"));

const HTTP_METHODS = new Set([
  "get",
  "post",
  "put",
  "patch",
  "delete",
  "head",
  "options",
  "trace",
]);

const specOps = [];
for (const [pathStr, pathItem] of Object.entries(spec.paths || {})) {
  for (const [method, operation] of Object.entries(pathItem)) {
    if (HTTP_METHODS.has(method) && operation.operationId) {
      specOps.push({
        operationId: operation.operationId,
        method: method.toUpperCase(),
        path: pathStr,
        summary: operation.summary || "",
      });
    }
  }
}

const mapKeys = new Set(Object.keys(operationMap));
const covered = specOps.filter((op) => mapKeys.has(op.operationId));
const missing = specOps.filter((op) => !mapKeys.has(op.operationId));

console.log(`Spec operations: ${specOps.length}`);
console.log(`Covered:         ${covered.length}`);
console.log(`Missing:         ${missing.length}`);
console.log(
  `Coverage:        ${Math.round((covered.length / specOps.length) * 100)}%`
);

if (missing.length > 0) {
  missing.sort((a, b) => a.operationId.localeCompare(b.operationId));
  console.log(`\nMissing operations:`);
  for (const op of missing) {
    console.log(`  - ${op.operationId} (${op.method} ${op.path})`);
  }

  console.log(`\nTo fix this:`);
  console.log(`  1. Add an example for each missing operation in examples/`);
  console.log(`     Wrap code in // <RegionName> ... // </RegionName> tags`);
  console.log(`  2. Add an entry to examples/operation-map.json for each operation`);
  console.log(`  3. Or use the Copilot prompt: .github/prompts/add-missing-examples.prompt.md`);

  fs.writeFileSync(
    path.join(rootDir, "missing-examples.json"),
    JSON.stringify(missing, null, 2)
  );
  process.exit(1);
} else {
  console.log("\nFull coverage!");
  const missingPath = path.join(rootDir, "missing-examples.json");
  if (fs.existsSync(missingPath)) {
    fs.unlinkSync(missingPath);
  }
}

// --- Integrity check: every operation-map entry must resolve ---

const REGION_PATTERNS = [
  { start: /^\s*\/\/#region\s+(.+?)\s*$/ },
  { start: /^\s*#region\s+(.+?)\s*$/ },
  { start: /^\s*\/\/\s*<([A-Za-z]\w*)>\s*$/ },
  { start: /^\s*#\s*region\s+(.+?)\s*$/ },
];

function extractRegions(content) {
  const regions = new Set();
  for (const line of content.split(/\r?\n/)) {
    for (const pattern of REGION_PATTERNS) {
      const m = line.match(pattern.start);
      if (m) regions.add(m[1].trim());
    }
  }
  return regions;
}

const examplesDir = path.join(rootDir, "examples");
const integrityErrors = [];
const fileRegionCache = new Map();

for (const [opId, entries] of Object.entries(operationMap)) {
  if (!Array.isArray(entries)) {
    integrityErrors.push(`${opId}: value is not an array`);
    continue;
  }
  for (const entry of entries) {
    if (!entry.file || typeof entry.file !== "string") {
      integrityErrors.push(`${opId}: entry missing "file" field`);
      continue;
    }
    if (!entry.region || typeof entry.region !== "string") {
      integrityErrors.push(`${opId}: entry missing "region" field`);
      continue;
    }
    const filePath = path.resolve(examplesDir, entry.file);
    if (!fs.existsSync(filePath)) {
      integrityErrors.push(`${opId}: file not found: ${entry.file}`);
      continue;
    }
    let regions = fileRegionCache.get(filePath);
    if (!regions) {
      regions = extractRegions(fs.readFileSync(filePath, "utf8"));
      fileRegionCache.set(filePath, regions);
    }
    if (!regions.has(entry.region)) {
      integrityErrors.push(
        `${opId}: region "${entry.region}" not found in ${entry.file}`
      );
    }
  }
}

if (integrityErrors.length > 0) {
  console.error(`\nIntegrity errors (${integrityErrors.length}):`);
  for (const err of integrityErrors) {
    console.error(`  - ${err}`);
  }
  process.exit(1);
} else {
  console.log("Integrity check passed: all files and regions resolve.");
}
