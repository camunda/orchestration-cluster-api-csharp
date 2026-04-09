#!/usr/bin/env node

/**
 * CI Guard: Verify that every public CamundaClient API method has a DocFX
 * overwrite entry in docs/overwrite/CamundaClient.md, and that every overwrite
 * entry references a valid example file + region.
 *
 * Addresses: https://github.com/camunda/orchestration-cluster-api-csharp/issues/78
 *
 * Exits with code 1 if any public API method is missing an overwrite entry
 * (except for methods explicitly excluded by this script because they are not
 * API operations), or if any overwrite entry references a non-existent example
 * file or region.
 */

const fs = require("fs");
const path = require("path");

const rootDir = path.join(__dirname, "..");
const srcDir = path.join(rootDir, "src", "Camunda.Orchestration.Sdk");
const overwriteFile = path.join(
  rootDir,
  "docs",
  "overwrite",
  "CamundaClient.md"
);
const examplesDir = path.join(rootDir, "examples");

// ---------------------------------------------------------------------------
// Lifecycle / infrastructure methods that are public but don't need overwrite
// entries because they aren't API operations (users call Create(), not ctor).
// ---------------------------------------------------------------------------
const EXCLUDED_METHODS = new Set([
  "Dispose",
  "DisposeAsync",
]);

// ── Step 1: Extract public method names from CamundaClient source files ─────

/**
 * Scan a C# file for public instance method declarations and return an array
 * of method names.  Matches patterns like:
 *   public async Task<T> MethodNameAsync(...)
 *   public ReturnType MethodName(...)
 *   public ReturnType MethodName(...) =>
 */
function extractPublicMethods(filePath) {
  const content = fs.readFileSync(filePath, "utf8");
  const methods = new Set();

  // Matches "public [modifiers] ReturnType MethodName(" at the start of a line.
  // Excludes constructors, properties, static methods, and class declarations.
  const re =
    /^\s+public\s+(?:(?:async|override|virtual|new)\s+)*(?:[\w<>\[\]?,\s]+?)\s+(\w+)\s*[(<]/gm;
  let m;
  while ((m = re.exec(content)) !== null) {
    const name = m[1];
    // Skip constructors (name == class name)
    if (name === "CamundaClient") continue;
    // Skip static methods (factory methods like Create)
    if (m[0].includes(" static ")) continue;
    methods.add(name);
  }
  return methods;
}

// Collect from generated file
const generatedFile = path.join(
  srcDir,
  "Generated",
  "CamundaClient.Generated.cs"
);


const allPublicMethods = new Set();

if (fs.existsSync(generatedFile)) {
  for (const name of extractPublicMethods(generatedFile)) {
    allPublicMethods.add(name);
  }
}

// Collect from hand-written partial class files (CamundaClient.cs + CamundaClient.*.cs)
const mainClientFile = path.join(srcDir, "CamundaClient.cs");
if (fs.existsSync(mainClientFile)) {
  for (const name of extractPublicMethods(mainClientFile)) {
    allPublicMethods.add(name);
  }
}

const partialFiles = fs
  .readdirSync(srcDir)
  .filter((f) => f.startsWith("CamundaClient.") && f.endsWith(".cs"));

for (const file of partialFiles) {
  for (const name of extractPublicMethods(path.join(srcDir, file))) {
    allPublicMethods.add(name);
  }
}

// Remove excluded lifecycle methods
for (const excl of EXCLUDED_METHODS) {
  allPublicMethods.delete(excl);
}

// ── Step 2: Parse overwrite file ────────────────────────────────────────────

if (!fs.existsSync(overwriteFile)) {
  console.error(`Overwrite file not found: ${overwriteFile}`);
  process.exit(2);
}

const overwriteContent = fs.readFileSync(overwriteFile, "utf8");

// Parse overwrite file into per-uid blocks: track each uid and its code refs
// Format: --- / uid: ... / example: / - *content / --- / [!code-csharp[](path#Region)]
const uidEntries = []; // { uid, methodName, refs: [{ file, region }] }
const uidMethodNames = new Set();

const sections = overwriteContent.split(/^---\s*$/m);
let currentUid = null;
let currentMethodName = null;
for (const section of sections) {
  const trimmed = section.trim();
  if (!trimmed) continue;
  const uidMatch = trimmed.match(
    /^uid:\s*Camunda\.Orchestration\.Sdk\.CamundaClient\.(\w+)/m
  );
  if (uidMatch) {
    currentMethodName = uidMatch[1];
    currentUid = trimmed.match(/^uid:\s*(.+)$/m)?.[1]?.trim();
    uidMethodNames.add(currentMethodName);
    continue;
  }
  // This section contains code references for the previous uid
  if (currentUid) {
    const refs = [];
    const refRe =
      /\[!code-csharp\[\]\((?:\.\.\/)*examples\/([^#)]+)#(\w+)\)\]/g;
    let rm;
    while ((rm = refRe.exec(trimmed)) !== null) {
      refs.push({ file: rm[1], region: rm[2] });
    }
    uidEntries.push({
      uid: currentUid,
      methodName: currentMethodName,
      refs,
    });
    currentUid = null;
    currentMethodName = null;
  }
}

// Flatten all example refs for file/region validation
const exampleRefs = uidEntries.flatMap((e) => e.refs);

// ── Step 3: Check overwrite completeness ────────────────────────────────────

const missing = [];

for (const method of [...allPublicMethods].sort()) {
  if (uidMethodNames.has(method)) continue;
  missing.push(method);
}

// ── Step 4: Validate example references ─────────────────────────────────────

const REGION_PATTERNS = [
  /^\s*#region\s+(.+?)\s*$/,
  /^\s*\/\/\s*<([A-Za-z]\w*)>\s*$/,
];

function extractRegions(filePath) {
  const content = fs.readFileSync(filePath, "utf8");
  const regions = new Set();
  for (const line of content.split(/\r?\n/)) {
    for (const pattern of REGION_PATTERNS) {
      const m = line.match(pattern);
      if (m) regions.add(m[1].trim());
    }
  }
  return regions;
}

const fileRegionCache = new Map();
const refErrors = [];

for (const ref of exampleRefs) {
  const filePath = path.join(examplesDir, ref.file);
  if (!fs.existsSync(filePath)) {
    refErrors.push(`File not found: examples/${ref.file} (region: ${ref.region})`);
    continue;
  }
  let regions = fileRegionCache.get(filePath);
  if (!regions) {
    regions = extractRegions(filePath);
    fileRegionCache.set(filePath, regions);
  }
  if (!regions.has(ref.region)) {
    refErrors.push(
      `Region "${ref.region}" not found in examples/${ref.file}`
    );
  }
}

// ── Report ──────────────────────────────────────────────────────────────────

// Check for uid entries that have no code references
const uidsWithoutRefs = uidEntries
  .filter((e) => e.refs.length === 0)
  .map((e) => e.uid);

console.log(`Public CamundaClient methods: ${allPublicMethods.size}`);
console.log(`Overwrite uid entries:         ${uidEntries.length}`);
console.log(`Example references:            ${exampleRefs.length}`);

let exitCode = 0;

if (missing.length > 0) {
  console.error(
    `\n✗ ${missing.length} public method(s) missing overwrite entries:`
  );
  for (const m of missing) {
    console.error(`  - ${m}`);
  }
  console.error(
    `\nTo fix: add uid + example reference blocks to docs/overwrite/CamundaClient.md`
  );
  exitCode = 1;
}

if (uidsWithoutRefs.length > 0) {
  console.error(
    `\n✗ ${uidsWithoutRefs.length} overwrite uid(s) have no [!code-csharp] reference:`
  );
  for (const uid of uidsWithoutRefs) {
    console.error(`  - ${uid}`);
  }
  exitCode = 1;
}

if (refErrors.length > 0) {
  console.error(
    `\n✗ ${refErrors.length} broken example reference(s) in overwrite file:`
  );
  for (const err of refErrors) {
    console.error(`  - ${err}`);
  }
  exitCode = 1;
}

if (exitCode === 0) {
  console.log("\n✓ All public methods have overwrite entries.");
  console.log("✓ All overwrite entries have code references.");
  console.log("✓ All example references resolve.");
}

process.exit(exitCode);
