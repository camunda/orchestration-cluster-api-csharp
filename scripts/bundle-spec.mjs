#!/usr/bin/env node
/**
 * Bundle multi-file OpenAPI YAML spec into a single JSON file.
 *
 * Mirrors the JS SDK's bundle-openapi.ts logic:
 * 1. SwaggerParser.bundle() merges multi-file spec into one document
 * 2. Augment with missing schemas from all upstream YAML files
 * 3. Normalize path-local $refs back to #/components/schemas/... via signature matching
 * 4. Rewrite x-semantic-type annotations
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import SwaggerParser from '@apidevtools/swagger-parser';
import { parse as parseYaml } from 'yaml';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, '..');
const UPSTREAM_SPEC_DIR = 'zeebe/gateway-protocol/src/main/proto/v2';
const ENTRY = path.join(REPO_ROOT, 'external-spec', 'upstream', UPSTREAM_SPEC_DIR, 'rest-api.yaml');
const BUNDLED_DIR = path.join(REPO_ROOT, 'external-spec', 'bundled');
const BUNDLED_FILE = path.join(BUNDLED_DIR, 'rest-api.bundle.json');

// ── Helpers ──────────────────────────────────────────────────────────────────

function listFilesRecursive(dir) {
  const out = [];
  const stack = [dir];
  while (stack.length) {
    const cur = stack.pop();
    for (const e of fs.readdirSync(cur, { withFileTypes: true })) {
      const p = path.join(cur, e.name);
      if (e.isDirectory()) stack.push(p);
      else if (e.isFile()) out.push(p);
    }
  }
  out.sort();
  return out;
}

function normalizeInternalRef(ref) {
  if (!ref.startsWith('#') || !ref.includes('%')) return ref;
  try {
    return '#' + decodeURIComponent(ref.slice(1));
  } catch {
    return ref;
  }
}

function rewriteInternalRefs(root) {
  const stack = [root];
  while (stack.length) {
    const cur = stack.pop();
    if (!cur || typeof cur !== 'object') continue;
    if (Array.isArray(cur)) { for (const item of cur) stack.push(item); continue; }
    if (typeof cur['$ref'] === 'string') cur['$ref'] = normalizeInternalRef(cur['$ref']);
    for (const v of Object.values(cur)) stack.push(v);
  }
}

function rewriteExternalRefsToLocal(root) {
  const stack = [root];
  while (stack.length) {
    const cur = stack.pop();
    if (!cur || typeof cur !== 'object') continue;
    if (Array.isArray(cur)) { for (const item of cur) stack.push(item); continue; }
    if (typeof cur['$ref'] === 'string') {
      const ref = cur['$ref'];
      if (ref.includes('#/components/schemas/') && (ref.includes('.yaml') || ref.includes('.yml'))) {
        const name = ref.split('#/components/schemas/').pop();
        if (name) cur['$ref'] = `#/components/schemas/${name}`;
      }
    }
    for (const k of Object.keys(cur)) {
      if (k !== '$ref') stack.push(cur[k]);
    }
  }
}

function jsonPointerDecode(segment) {
  return decodeURIComponent(segment.replace(/~1/g, '/').replace(/~0/g, '~'));
}

function resolveInternalRef(root, ref) {
  if (!ref.startsWith('#/')) return undefined;
  let cur = root;
  for (const seg of ref.slice(2).split('/')) {
    if (!cur || typeof cur !== 'object') return undefined;
    cur = cur[jsonPointerDecode(seg)];
  }
  return cur;
}

function sortKeys(obj) {
  if (Array.isArray(obj)) return obj.map(sortKeys);
  if (typeof obj === 'object' && obj !== null) {
    return Object.keys(obj).sort().reduce((acc, key) => { acc[key] = sortKeys(obj[key]); return acc; }, {});
  }
  return obj;
}

function canonicalStringify(obj) {
  return JSON.stringify(sortKeys(obj));
}

// ── Main ─────────────────────────────────────────────────────────────────────

async function main() {
  if (!fs.existsSync(ENTRY)) {
    console.error(`[bundle-spec] ERROR: Spec entry not found at ${ENTRY}`);
    console.error('[bundle-spec] Run scripts/fetch-spec.sh first');
    process.exit(1);
  }

  fs.mkdirSync(BUNDLED_DIR, { recursive: true });

  console.log(`[bundle-spec] Bundling ${ENTRY} -> ${BUNDLED_FILE}`);

  // 1. Bundle
  const bundled = await SwaggerParser.bundle(ENTRY);

  // 2. Augment with missing schemas from upstream YAML files
  console.log('[bundle-spec] Augmenting bundle with missing schemas...');
  const upstreamDir = path.join(REPO_ROOT, 'external-spec', 'upstream', UPSTREAM_SPEC_DIR);
  const allFiles = listFilesRecursive(upstreamDir);
  let augmentedCount = 0;

  if (!bundled.components) bundled.components = {};
  if (!bundled.components.schemas) bundled.components.schemas = {};

  for (const file of allFiles) {
    if (!file.endsWith('.yaml') && !file.endsWith('.yml')) continue;
    try {
      const content = fs.readFileSync(file, 'utf8');
      const doc = parseYaml(content);
      if (doc?.components?.schemas) {
        for (const [name, schema] of Object.entries(doc.components.schemas)) {
          if (!bundled.components.schemas[name]) {
            const s = JSON.parse(JSON.stringify(schema));
            rewriteExternalRefsToLocal(s);
            bundled.components.schemas[name] = s;
            augmentedCount++;
          }
        }
      }
    } catch (e) {
      console.warn(`[bundle-spec] Failed to parse/merge ${file}`, e);
    }
  }
  console.log(`[bundle-spec] Added ${augmentedCount} missing schemas.`);

  // 3. Normalize path-local $refs
  const componentSchemas = bundled.components.schemas;
  const componentValues = new Set(Object.values(componentSchemas));
  const schemaSignatureMap = new Map();
  for (const [name, schema] of Object.entries(componentSchemas)) {
    schemaSignatureMap.set(canonicalStringify(schema), name);
  }

  function safeNormalize(root, seen = new Set()) {
    if (!root || typeof root !== 'object') return;
    if (seen.has(root)) return;
    seen.add(root);

    // Rewrite path-local $like refs
    {
      const rawRef = root['$ref'];
      const normalizedRef = typeof rawRef === 'string' ? normalizeInternalRef(rawRef) : undefined;
      if (
        normalizedRef &&
        normalizedRef.startsWith('#/paths/') &&
        /\/properties\/\$like$/.test(normalizedRef) &&
        componentSchemas['LikeFilter']
      ) {
        root['$ref'] = '#/components/schemas/LikeFilter';
        return;
      }
    }

    if (componentValues.has(root)) {
      for (const v of Object.values(root)) safeNormalize(v, seen);
      return;
    }

    if (Array.isArray(root)) {
      root.forEach(x => safeNormalize(x, seen));
      return;
    }

    // Post-order: normalize children first
    for (const v of Object.values(root)) safeNormalize(v, seen);

    // Rewrite path-local refs
    if (root['$ref'] && typeof root['$ref'] === 'string' && root['$ref'].startsWith('#/paths/')) {
      const resolved = resolveInternalRef(bundled, root['$ref']);
      if (resolved) {
        safeNormalize(resolved, seen);

        if (resolved['$ref'] && typeof resolved['$ref'] === 'string' && resolved['$ref'].startsWith('#/components/schemas/')) {
          root['$ref'] = resolved['$ref'];
          return;
        }

        // Manual overrides for known tricky paths
        const manualOverrides = {
          '#/paths/~1process-instances~1search/post/requestBody/content/application~1json/schema/properties/filter/allOf/0':
            'ProcessInstanceFilter',
          '#/paths/~1process-definitions~1%7BprocessDefinitionKey%7D~1statistics~1element-instances/post/requestBody/content/application~1json/schema/properties/filter/allOf/0/allOf/0':
            'BaseProcessInstanceFilterFields',
        };

        if (manualOverrides[root['$ref']]) {
          root['$ref'] = `#/components/schemas/${manualOverrides[root['$ref']]}`;
          return;
        }

        const sig = canonicalStringify(resolved);
        const matchingName = schemaSignatureMap.get(sig);
        if (matchingName) {
          root['$ref'] = `#/components/schemas/${matchingName}`;
          return;
        }
      }
    }

    // Rewrite inline matching objects
    if (!root['$ref']) {
      const sig = canonicalStringify(root);
      const matchingName = schemaSignatureMap.get(sig);
      if (matchingName) {
        for (const k of Object.keys(root)) delete root[k];
        root['$ref'] = `#/components/schemas/${matchingName}`;
      }
    }

    // Rewrite x-semantic-type
    if (root['x-semantic-type'] && !root['$ref'] && componentSchemas[root['x-semantic-type']]) {
      const target = root['x-semantic-type'];
      for (const k of Object.keys(root)) delete root[k];
      root['$ref'] = `#/components/schemas/${target}`;
    }
  }

  // Snapshot the bundled doc before normalization so we can resolve path-local
  // refs even after safeNormalize replaces intermediate nodes with component $refs.
  const preNormSnapshot = JSON.parse(JSON.stringify(bundled));

  safeNormalize(bundled);
  rewriteInternalRefs(bundled);

  // 4. Dereference remaining path-local $refs by inlining.
  //    Microsoft.OpenApi (used by the C# generator) cannot resolve #/paths/... $refs.
  //    Unlike @hey-api/openapi-ts (JS SDK), it treats them as errors.
  //    We resolve them by deep-cloning the target into the ref site.
  //    Use the pre-normalization snapshot for resolution because safeNormalize may
  //    have replaced intermediate path nodes with component $refs.
  //    Run in a loop because inlining can introduce new path-local refs from the target.
  let totalDereferenced = 0;
  for (let pass = 1; pass <= 20; pass++) {
    let dereferenced = 0;

    const stack = [bundled];
    const seen = new Set();
    while (stack.length) {
      const node = stack.pop();
      if (!node || typeof node !== 'object' || seen.has(node)) continue;
      seen.add(node);

      if (Array.isArray(node)) {
        for (let i = 0; i < node.length; i++) {
          const item = node[i];
          if (item && typeof item === 'object' && typeof item['$ref'] === 'string' && item['$ref'].startsWith('#/paths/')) {
            const resolved = resolveInternalRef(preNormSnapshot, item['$ref']);
            if (resolved) {
              node[i] = JSON.parse(JSON.stringify(resolved));
              dereferenced++;
            }
          }
          stack.push(node[i]);
        }
      } else {
        for (const [key, val] of Object.entries(node)) {
          if (!val || typeof val !== 'object') continue;
          if (typeof val['$ref'] === 'string' && val['$ref'].startsWith('#/paths/')) {
            const resolved = resolveInternalRef(preNormSnapshot, val['$ref']);
            if (resolved) {
              node[key] = JSON.parse(JSON.stringify(resolved));
              dereferenced++;
            }
          }
          stack.push(node[key]);
        }
      }
    }

    totalDereferenced += dereferenced;
    if (dereferenced === 0) break;
  }
  if (totalDereferenced > 0) {
    console.log(`[bundle-spec] Dereferenced ${totalDereferenced} path-local $refs.`);
  }

  // 5. Write output
  fs.writeFileSync(BUNDLED_FILE, JSON.stringify(bundled, null, 2) + '\n', 'utf8');

  const pathCount = bundled?.paths ? Object.keys(bundled.paths).length : 0;
  const schemaCount = bundled?.components?.schemas ? Object.keys(bundled.components.schemas).length : 0;
  console.log(`[bundle-spec] Bundle complete (paths=${pathCount}, schemas=${schemaCount})`);
}

try {
  await main();
} catch (err) {
  console.error('[bundle-spec] Failed to bundle OpenAPI spec');
  console.error(err);
  process.exitCode = 1;
}
