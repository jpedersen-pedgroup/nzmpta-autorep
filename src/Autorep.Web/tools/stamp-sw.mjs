// Stamps the service worker's CACHE_VERSION with a fingerprint of the built client bundle.
//
// The static-asset cache is cache-first and matched with ignoreSearch, so nothing else
// invalidates it: without this stamp a deploy would leave every installed device serving the
// previous build's JavaScript and CSS indefinitely. Changing the version name is what makes the
// activate handler drop the old cache.
//
// Deterministic — the same bundle produces the same stamp, so a rebuild that changes nothing
// leaves sw.js untouched and the working tree clean.
import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const metaPath = resolve(projectRoot, "obj/esbuild-meta.json");
const swPath = resolve(projectRoot, "wwwroot/sw.js");

const meta = JSON.parse(readFileSync(metaPath, "utf8"));

// Chunk filenames already carry esbuild's content hash; byte counts cover the entry point, whose
// name is fixed. Sorted so filesystem ordering can't change the result.
const fingerprint = Object.entries(meta.outputs)
  .map(([file, out]) => `${file}:${out.bytes}`)
  .sort()
  .join("\n");
const stamp = createHash("sha256").update(fingerprint).digest("hex").slice(0, 12);

const sw = readFileSync(swPath, "utf8");
const pattern = /(const CACHE_VERSION = 'autorep-)[^']*(';)/;

if (!pattern.test(sw)) {
  // Failing loudly matters more than usual here: a silent no-op would ship a stale cache to
  // every device with no other symptom.
  console.error(`[stamp-sw] no CACHE_VERSION line found in ${swPath} — refusing to continue.`);
  process.exit(1);
}

const stamped = sw.replace(pattern, `$1${stamp}$2`);
if (stamped === sw) {
  console.log(`[stamp-sw] CACHE_VERSION already autorep-${stamp}`);
} else {
  writeFileSync(swPath, stamped);
  console.log(`[stamp-sw] CACHE_VERSION -> autorep-${stamp}`);
}
