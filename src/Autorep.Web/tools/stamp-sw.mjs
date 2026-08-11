// Stamps the service worker's CACHE_VERSION with a fingerprint of everything the versioned cache
// can pin: the built client bundle AND the static assets sw.js precaches.
//
// The static-asset cache is cache-first and matched with ignoreSearch, so nothing else
// invalidates it: without this stamp a deploy would leave every installed device serving the
// previous build's JavaScript and CSS indefinitely. Changing the version name is what makes the
// activate handler drop the old cache.
//
// The bundle alone is not enough. Chunk filenames carry esbuild's content hash, so the JS side is
// covered by names and byte counts — but site.css, pwa-register.js and the logos keep the same URL
// forever, and `?v=` can't rescue them because the fetch handler matches with ignoreSearch. Hashing
// their contents is what makes a CSS-only change reach devices. APP_SHELL is read out of sw.js
// rather than duplicated here so the two lists cannot drift.
//
// Deterministic — the same bundle and the same assets produce the same stamp, so a rebuild that
// changes nothing leaves sw.js untouched and the working tree clean. Text assets are hashed with
// line endings normalised, so a CRLF checkout on Windows and an LF one on CI agree: without that,
// every CI deploy would rename the cache and make each tester re-download the whole shell.
import { createHash } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, extname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const metaPath = resolve(projectRoot, "obj/esbuild-meta.json");
const swPath = resolve(projectRoot, "wwwroot/sw.js");
const webRoot = resolve(projectRoot, "wwwroot");

// Failing loudly matters more than usual in this script: a silent no-op would ship a stale cache to
// every device with no other symptom.
function fail(message) {
  console.error(`[stamp-sw] ${message}`);
  process.exit(1);
}

const sw = readFileSync(swPath, "utf8");
const pattern = /(const CACHE_VERSION = 'autorep-)[^']*(';)/;
if (!pattern.test(sw)) fail(`no CACHE_VERSION line found in ${swPath} — refusing to continue.`);

// --- the bundle ---------------------------------------------------------------------------------
// Chunk filenames already carry esbuild's content hash; byte counts cover the entry point, whose
// name is fixed.
const meta = JSON.parse(readFileSync(metaPath, "utf8"));
const bundleParts = Object.entries(meta.outputs).map(([file, out]) => `${file}:${out.bytes}`);

// --- the precached shell ------------------------------------------------------------------------
const TEXT_EXTENSIONS = new Set([".css", ".js", ".mjs", ".svg", ".webmanifest", ".json", ".html", ".txt"]);

/** The asset URLs listed in sw.js's APP_SHELL array. */
function appShellUrls(source) {
  const block = /const APP_SHELL = \[([\s\S]*?)\];/.exec(source);
  if (!block) fail("no APP_SHELL array found in sw.js — refusing to continue.");
  // Drop // comments before reading the string literals: an apostrophe in a comment would
  // otherwise register as the start of an asset path and swallow everything up to the next quote.
  const body = block[1].replace(/\/\/[^\n]*/g, "");
  const urls = [...body.matchAll(/'([^']+)'/g)].map((m) => m[1]);
  // An empty list would hash to a constant and quietly reinstate the stale-CSS bug this guards.
  if (!urls.length) fail("APP_SHELL parsed as empty — the array's shape must have changed.");
  return urls;
}

const shellParts = appShellUrls(sw).map((url) => {
  const file = resolve(webRoot, url.replace(/^\//, ""));
  let bytes;
  try {
    bytes = readFileSync(file);
  } catch {
    fail(
      `APP_SHELL lists '${url}' but ${file} could not be read. Every precached entry must be a ` +
        `file under wwwroot — a served route can't be hashed, so it can't be cache-busted either.`,
    );
  }
  // Normalise CRLF for text so the stamp doesn't depend on how git checked the file out.
  const content = TEXT_EXTENSIONS.has(extname(file).toLowerCase())
    ? Buffer.from(bytes.toString("utf8").replace(/\r\n/g, "\n"), "utf8")
    : bytes;
  return `${url}:${createHash("sha256").update(content).digest("hex")}`;
});

// Sorted within each section so filesystem ordering can't change the result.
const fingerprint = ["bundle", ...bundleParts.sort(), "shell", ...shellParts.sort()].join("\n");
const stamp = createHash("sha256").update(fingerprint).digest("hex").slice(0, 12);

const stamped = sw.replace(pattern, `$1${stamp}$2`);
if (stamped === sw) {
  console.log(`[stamp-sw] CACHE_VERSION already autorep-${stamp}`);
} else {
  writeFileSync(swPath, stamped);
  console.log(`[stamp-sw] CACHE_VERSION -> autorep-${stamp}`);
}
