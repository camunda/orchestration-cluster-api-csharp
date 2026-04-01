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
  console.log(`  1. Add an example for each missing operation in docs/examples/`);
  console.log(`     Wrap code in // <OperationId> ... // </OperationId> tags`);
  console.log(`  2. Add an entry to examples/operation-map.json for each operation`);
  console.log(`  3. Or use the Copilot prompt: .github/prompts/add-missing-examples.prompt.md`);

  fs.writeFileSync(
    path.join(rootDir, "missing-examples.json"),
    JSON.stringify(missing, null, 2)
  );
  process.exit(1);
} else {
  console.log("\nFull coverage!");
}
