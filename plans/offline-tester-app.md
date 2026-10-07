# Offline Tester App — implementation plan (M2)

> Scope: make the **tester** side of AutoRep work with no signal — cold launch, see your tests, resume or start a test, capture a full machine test, print it, sync when back online.
> Status legend (assessment table only): ✅ done · 🟡 partial · ⬜ not started · ❓ needs a decision
> Assessed against the worktree at `claude/tester-farm-review-approval-e1ebf5`, 22 Jul 2026. Every claim below cites a file:line that was read; where something is inferred rather than verified it says so.

---

## 1. Where we actually are

The **data layer is done. The shell layer is not.** That gap is why offline feels half-built: everything a tester captures already lives on the device and syncs correctly, but the tester cannot *get to* any of it without the server.

| Layer | State | Evidence |
|---|---|---|
| IndexedDB capture store (tests, config, faults, readings, attachments, amendments) | ✅ | `Client/db/testStore.ts:144-204` |
| Per-tester DB namespacing on shared devices | ✅ | `testStore.ts:161-164` |
| Reference sync — standards, equipment, fault catalog, privacy | ✅ | `Client/standards/*.ts`, wired at `Client/main.ts:40` |
| Company farm book cached to device | ✅ | `Client/sync/farmsSync.ts:28-38` |
| Offline farm **detail** lookup for a chosen farm | ✅ | `farms.ts:32` → `farmsSync.ts:51-53` → `WizardApp.tsx:226` |
| Delta test pull with lagged watermark | ✅ | `syncClient.ts:62-73`, `Api/SyncController.cs:66-96` |
| Wizard step navigation, pass/fail, fault rollup, PDF doc build | ✅ | `WizardApp.tsx`, `Client/report/testSummaryPdf.ts` |
| **Any page navigation offline** | ✅ (Phase 2, Oct 2026) | `sw.js` `shellNavigation`: tester routes are server-first, then the cached identity-free `wwwroot/app-shell.html` (offline, or no answer within 8 s); everything else keeps the offline card |
| **Cold PWA launch offline** | ✅ (Phase 2, Oct 2026) | `start_url` unchanged (`/`); the shell answers `/` and makes the role decision from the device's identity record (`Client/db/identity.ts`, `shell/shell.tsx`) |
| **Starting a test offline** | ✅ (Phase 3, Oct 2026) | `/App/Tests/New` is drawn on the device (`Client/ui/NewTestApp.tsx`) from the cached farm book and opens the wizard directly; adding a farm is `POST /api/farms`, online-only and says so |
| **Printing offline on a device that never printed online** | ⬜ | PDF chunks only cached on first fetch (`sw.js:133-148`); `chunks/pdfmake-EIRMY33F.js` is 2.85 MB and is never fetched until a report is generated |
| Precache list actually serving | ⬜ | `sw.js:13,14,19` precache bare URLs; pages request `?v=` (`_Layout.cshtml:33,120`, `_BrandHead.cshtml:37`) and `sw.js:135` matches with no `ignoreSearch` — those three entries are **dead** |

Two things in the brief need correcting before anyone plans against them:

- **`getCachedFarms()` is NOT uncalled.** `farmsSync.ts:52` → `farms.ts:32` → `WizardApp.tsx:226`. The offline farm-*detail* path works end to end today. What is missing is an offline farm **picker**.
- **"The wizard works offline once mounted" is only true for a resumed test.** On first open of a *new* test the wizard awaits `fetchFarm()` before its first render (`WizardApp.tsx:225-231`), and `farms.ts:26` uses bare `fetch` with no `AbortController` — contrast `connectivity.ts:11-20`, which does. Hard-offline rejects fast; weak signal / captive portal hangs on "Loading test…" for the browser default. And PDF generation inside the wizard depends on uncached chunks.

There are also three **live data-loss defects** that only bite once testers really work offline for a shift. They are not caused by this work but they invalidate it, so they go first (Phase 0).

---

## 2. Non-goals

Explicitly out of scope for this chunk. If any of these come back, they are a variation or a later phase, not a scope creep inside these phases.

- **Admin portal offline.** `/Admin/*` stays network-first with the offline card. The SW navigation allowlist must be path-scoped so admin pages are never served from a shell. Note `Client/main.ts:36-41` already excludes the admin read-only wizard from the farm-book sync — keep that.
- **Offline login on a device/account that has never been online.** `/Account/*` is excluded from the SW today (`sw.js:118-120`) and stays excluded. A brand-new device must reach the server once.
- **Farm PII in a device-shared cache.** The Cache API is per-origin, IndexedDB is per-tester (`testStore.ts:161-164`). No rendered Razor page containing a farm book, a farmer's contact details, or a tester's name/initials goes into the Cache API. Ever. This constraint is what forces the identity-free shell.
- **Offline sign-out.** It is a POST to an `[Authorize]` page. Offline it is disabled, not faked.
- **Offline address autocomplete.** `Api/AddressController.cs` is `[Authorize(Policy = "AdminArea")]` and only used by `Pages/Admin/Farms/Edit.cshtml`. The tester add-farm modal has plain text inputs. Nothing to make offline — recorded so nobody budgets for it.
- **Background Sync / periodic sync.** Not supported on iPad Safari, which is the target device. Any use is enhancement-only and must not be load-bearing.
- **Offline creation of a *brand-new* farm** — conditionally out of scope. See Phase 5 and Decision (f); this is still an open NZMPTA question (`plans/build-checklist.md:175`).

---

## 3. Decisions at a glance

Full reasoning in §5. The phases below assume these answers.

| # | Decision | Recommendation |
|---|---|---|
| a | Offline auth/session identity | Identity record in **IndexedDB, written online, read at boot**; explicit rule that **offline = one tester per device**; harden the purge; return 401 (not 302) on `/api/*` |
| b | App shell vs client-side router | **Cached identity-free shell per tester route**, no router |
| c | `asp-append-version` / stale bundle | **Drop `asp-append-version` on shell assets**, build-stamp `CACHE_VERSION`, `ignoreSearch:true`, emit a chunk manifest |
| d | What is safe to cache | Shell + static assets + logos only. **No rendered Razor page HTML, ever.** |
| e | Storage quota / eviction | Request persistence, surface `estimate()`, paginate the first pull, drop the local attachment copy after upload |
| f | Offline farm creation | **Defer** pending NZMPTA answer; ship "pick from the cached book" first |

---

## 4. Phases

Each phase is independently shippable and useful on its own. Phases 0 and 1 are prerequisites for Phase 2 being *safe*, but they both deliver value even if Phase 2 never lands.

### Phase 0 — Stop the bleeding (sync + purge correctness) ✅ DONE 22 Jul 2026

These were live bugs. They lost tester work, and every "you have N unsynced tests" indicator added later would have been untrustworthy until they were fixed.

- [x] **Return 401/403 from `/api/*` instead of a 302 to login.** Add `CookieAuthenticationEvents.OnRedirectToLogin`/`OnRedirectToAccessDenied` in `ConfigureApplicationCookie` (`Program.cs:66-74`) that returns a status code when `context.Request.Path` starts with `/api`. Today an expired cookie makes `fetch` follow the redirect (POST→GET), land on a 200 HTML login page, so `res.ok` is **true** at `syncClient.ts:58` and the test is flipped to `syncState:"uploaded", everUploaded:true` at `:59`. That test can then never be re-pushed (`syncClient.ts:161`) nor deleted (`TestListApp.tsx:31-33`) and displays as synced. The server never got it.
- [x] **Belt and braces on the client:** `redirect:"manual"` (or assert `content-type: application/json`) on both `pushTest` (`syncClient.ts:39`) and `pullTests` (`syncClient.ts:71`); surface a distinct "your session expired — sign in again to sync" state rather than the generic toast at `TestListApp.tsx:53-57`.
- [x] **Don't abort the whole sync on one bad push.** `syncClient.ts:158-166` is a bare `for` loop where `pushTest` throws; one rejected payload blocks every later push *and* the pull, permanently. Collect per-test failures, continue the loop, always run `pullTests()`, and return `{pushed, failed, pulled}` so the UI can say which test is stuck.
- [x] **Harden `purgeStaleLocalData()`** (`testStore.ts:177-188`): no-op when `currentTesterId()` is null. Today `current` falls back to `""` at `:179`, `last === current` fails at `:181`, and `:183` deletes `autorep_<lastTesterId>` — every unsynced test on the device. This is the single landmine that makes any shell work destructive, and it is a two-line fix that should land regardless.
- [x] **Don't silently destroy unsynced work on a genuine tester handover.** Done, but with a toast rather than a blocking screen and without renaming the function: it still purges in the normal case, so the name still fits, and a blocking screen belongs with the Phase 2 shell chrome rather than bolted onto startup. `purgeStaleLocalData` now returns `PurgeResult { retained? }` and `main.ts` warns. **Deliberate trade recorded:** the outgoing tester's cached farm PII stays on disk until their work syncs — irreversible data loss outranks cache hygiene, and the data sits under a DB name the incoming session never opens.
- [x] **Give `fetchFarm` a timeout and a broader fallback.** `farms.ts:26` → add the 5 s `AbortController` pattern from `connectivity.ts:11-20`; fall back to `getCachedFarm` on 401/403/5xx as well as on throw, but **keep 404 authoritative** (the comment at `farms.ts:27-29` is deliberate — a 404 means out-of-scope, don't resurrect it).
- [x] Tests: `ApiChallengeTests` (401 on three `/api` routes incl. POST; pages still redirect) over `WebAppFactory`, since `AuthedWebAppFactory`'s test handler bypasses the cookie pipeline and would prove nothing. New `Client/sync/syncClient.test.ts` (continue-on-failure, the followed-redirect regression, 401) and `Client/db/testStore.purge.test.ts` (own file — purging deletes databases the other specs share).
- [x] **Fixed in passing:** `WebAppFactory` hard-coded one InMemory database name, so a second fixture instance re-seeded the same store and made `Reference_data_is_seeded_on_startup` depend on how many classes used the factory. Now per-instance, matching `AuthedWebAppFactory`. `Address_proxy_rejects_anonymous` tightened from `BeOneOf(302, 401)` to exactly 401.

**Files:** `Program.cs`, `Client/sync/syncClient.ts`, `Client/db/testStore.ts`, `Client/farms.ts`, `Client/ui/TestListApp.tsx`, `tests/Autorep.Web.Tests/AuthIntegrationTests.cs`, `Client/db/testStore.test.ts` (+ new `Client/sync/syncClient.test.ts`).
**Estimate: 2–3 days.**
**Ships:** a tester who returns from a long day with an expired cookie gets an honest failure instead of silently losing a day's tests; one bad test no longer wedges all syncing; handing the iPad over no longer destroys unsynced work.

---

### Phase 1 — Make the cache honest (build coupling + offline printing)

The current precache list mostly does not work, and the biggest offline asset (the PDF generator) is never precached. Fixing this is a prerequisite for a cached shell surviving a deploy, and it independently delivers offline printing, which is an explicit NZMPTA requirement (`plans/build-checklist.md:88`).

> **ORDERING CORRECTION (22 Jul 2026, verified in-browser).** The next three bullets are **one
> atomic change**, not three independent ones. `ignoreSearch` is *not* safe on its own: the static
> branch is cache-first, so once `/js/dist/autorep.js` is cached, `ignoreSearch` would match it for
> every future `?v=` and the device would serve that build **forever**. Today `asp-append-version`
> is the only thing invalidating it — a new deploy mints a new URL, misses the cache, and refetches.
> So `ignoreSearch` may only land together with build-stamped `CACHE_VERSION` (which invalidates the
> whole cache per deploy) and the removal of `asp-append-version`. Doing them in any other order
> either breaks deploys or achieves nothing.
>
> Confirmed live: after one load, `autorep-v7` holds `/css/site.css` **and**
> `/css/site.css?v=…` — the precached copy is dead weight and the runtime copy is what actually
> serves. Same for `pwa-register.js`. So the three "dead" entries are not merely unused, they are
> duplicated on disk.

- [x] **Atomically:** `caches.match(event.request, { ignoreSearch: true })` at the static branch **+**
      build-stamp `CACHE_VERSION` **+** drop `asp-append-version` from the shell assets the SW owns —
      `_Layout.cshtml:33` (FA css), `_Layout.cshtml:121` (`pwa-register.js`), `_BrandHead.cshtml:37`
      (`site.css`), `Pages/App/Tests/Index.cshtml:22`, `Pages/App/Tests/Wizard.cshtml:13` and
      `Pages/Account/FinishSync.cshtml:37-38` (`autorep.js`, **six** call sites, not five — FinishSync
      was added by the licence sync-only work). `CACHE_VERSION` is a hand-typed literal at `sw.js:8`
      and neither `build` nor `build:prod` touches it, so a deploy currently ships stale assets
      silently unless someone remembers to bump it. Add a node step after esbuild that reads the
      metafile and rewrites a token in `sw.js`; wire it into both scripts so CI and local stay in step.
- [x] **Offline printing — DONE 22 Jul 2026.** No chunk manifest was needed after all: a dynamic
      `import()` is rewritten by esbuild to the hashed chunk path at build time, so the client
      always knows it. `Client/report/generatorChunks.ts` warms pdfmake + vfs_fonts + pdf-lib after
      a **successful sync** (not on page load — a weak signal at a farm shouldn't drag 2.4 MB over
      mobile data), fire-and-forget so nothing waits on it, skipped when `saveData` is set. The
      fetches go through the SW's existing static branch, so no service-worker change was required.
      ~~Emit a chunk manifest and precache the PDF chunks.~~ Chunk names are content-hashed (`--chunk-names=chunks/[name]-[hash]`) so the list must be generated. Current on-disk dev sizes: `pdfmake-EIRMY33F.js` 2,848,921 B, `vfs_fonts-OBOSLEIK.js` 855,025 B, `es-R5OKT6RQ.js` 830,410 B (prod/minified is roughly 2.4 MB total). **Recommendation:** do not precache on `install` — that is 2.4 MB blocking the first load on rural mobile data. Instead **warm them in the background after the first successful `syncAll()`**, and show a "ready to print offline" state. Gate on `navigator.connection.saveData` if present (unverified support on iPad Safari — treat as best-effort).
- [x] **Replace `cache.addAll(APP_SHELL).catch(()=>{})`** (`sw.js:57`) with per-URL `cache.add` in a `Promise.allSettled`, logging failures. `addAll` is atomic: one 404 silently precaches nothing, and adding a shell HTML entry is exactly the kind of entry that goes missing after a deploy.
- [x] **Recover from a 404 instead of returning an empty 504.** `sw.js:145` returns `new Response('', {status:504})` for any miss; for `<script type="module">` that is a silent blank page. Distinguish a network failure (offline → serve cached or fail loudly) from a 404 (deleted hash → `registration.update()` + one-time reload).
- [x] **Wrap the lazy imports** at `Client/report/testSummaryPdf.ts:418-421,440` in try/catch with a real message ("this device hasn't downloaded the report generator yet — connect once and it'll work offline afterwards").
- [x] **Self-host Montserrat + Open Sans** under `wwwroot/lib/fonts`, replace the Google Fonts `<link>` at `_BrandHead.cshtml:17-19` with local `@font-face`, add the woff2 to `APP_SHELL`. Cosmetic, but the generated offline card at `sw.js:32` already asks for a font it can never have, and it removes a third-party origin from every page load. Also add the three remaining vendored FA woff2 files (only `fa-solid-900` is precached, `sw.js:20`).
- [x] Playwright: after one online load, assert the expected cache keys exist; assert an offline `import()` of the PDF path succeeds after the warm.

**Files:** `wwwroot/sw.js`, `package.json`, a new build script under `src/Autorep.Web/`, `Pages/Shared/_Layout.cshtml`, `Pages/Shared/_BrandHead.cshtml`, `Pages/App/Tests/Index.cshtml`, `Pages/App/Tests/Wizard.cshtml`, `Client/report/testSummaryPdf.ts`, `wwwroot/lib/fonts/*`.
**Estimate: 3–5 days** (the build stamping + manifest is the uncertain half).
**Ships:** a tester who has synced once can print a report on-farm with no signal, on a device that has never printed before; assets keep working across deploys; the app renders branded offline instead of unstyled.

**STATUS 22 Jul 2026 — everything except offline printing is DONE.** Notes for whoever picks up
the remainder:

- `asp-append-version` turned out to have **11 occurrences across 10 files**, not the five listed —
  other sessions added `App/Tests/Company.cshtml`, `App/Tests/View.cshtml` and
  `Admin/Tests/View.cshtml` meanwhile. All removed.
- `tools/stamp-sw.mjs` is wired into both `build` and `build:prod`, reads
  `obj/esbuild-meta.json` (outside `wwwroot`, so it is never served), and **exits non-zero** if the
  `CACHE_VERSION` line ever stops matching — a silent no-op there would ship a stale cache with no
  other symptom. Deterministic: verified that editing a client file changes the stamp and reverting
  restores it, so a no-op rebuild leaves the tree clean.
- Fonts are **variable**, so one woff2 per family per subset covers every weight — 4 files, 180 KB.
  `latin-ext` is **not** optional: precomposed macron vowels (ā ē ī ō ū, from U+0100) live there and
  Māori names run right through NZ farm data. Verified live: both families load from local files,
  macrons render, and there are now **zero** requests to `fonts.googleapis.com`/`gstatic.com`.
- The remaining vendored FA woff2 files (`fa-brands-400`, `fa-regular-400`, `fa-v4compatibility`)
  were deliberately **not** added to `APP_SHELL` — the app renders `fa-solid` classes, and quota on
  iPadOS is a live concern (Phase 4). Add them only if a surface actually needs them.
- Verified after a clean install: 13 shell entries present, `/css/site.css` now appears **once**
  rather than twice, and a `?v=` request matches the precached bare entry.

---

### Phase 2 — Offline navigation: the identity-free shell ✅ DONE Oct 2026

The headline. Cold launch, navigate, resume.

> **STATUS (7 Oct 2026, branch `claude/offline-tester-shell`).** Built and covered by the Playwright
> offline suite (`tests/Autorep.Web.Tests/E2E/OfflineTesterE2ETests.cs`). Where the build departs
> from the bullets below, and why:
>
> - **The identity record is NOT in the per-tester IndexedDB.** That bullet predates the §5a
>   amendment, which is right: `dbName()` needs the tester id to open that database, so reading the id
>   from inside it is circular. It lives in a database of its own, `autorep-identity` — deliberately not
>   `autorep_<x>`, which `purgeStaleLocalData` would treat as a tester store and delete. Still no
>   localStorage. Cleared at sign-out by `wwwroot/js/pwa-register.js`, which also warns (never blocks)
>   when the tester has unsent tests — they are kept.
> - **`@inject UserManager` / `GetUserAsync` stay in `_Layout.cshtml`.** The server banner is still
>   needed on `/Help` and `/Account/Manage`, which don't load the bundle. The shell draws the same banner
>   client-side (`Client/shell/licenceBanner.ts`); the per-request query is a cost, not an offline defect.
> - **Manage and Sign out are not disabled offline** (superseded by §5a "never block sign-out"). Both
>   are links in the shell; offline they land on the branded offline card, because `/Account/*` now
>   reaches the navigation branch. Sign out from the shell goes to a new GET confirmation page
>   (`Pages/Account/Logout.cshtml`) — the shell has no antiforgery token to post with.
> - **Added: an 8 s navigation timeout** on tester routes before the shell answers. Weak signal hangs
>   a request rather than failing it.
> - **Added: the bundle is precached.** `tools/stamp-sw.mjs` writes `BUNDLE_FILES` (entry + static
>   chunks + small lazy chunks) from the metafile, so a worker that has just installed can launch the
>   app offline — a runtime-only copy is thrown away with the previous build's cache.
> - **Added: bundle-load recovery** in `pwa-register.js` — a module graph that fails to load (cached
>   entry, chunk deployed away) fetches the new worker or drops the cached bundle and reloads once;
>   never offline, never more than once a minute.
> - **The tester page bodies are client-rendered on the server pages too** (`/App` home tiles, the My
>   tests header), so the shell and the server draw the same thing. `/App` now loads the bundle.
> - **Testing note:** `context.SetOfflineAsync(true)` does not reach a service worker's own fetches in
>   Chromium (the `PW_EXPERIMENTAL_SERVICE_WORKER_NETWORK_EVENTS` flag reaches only some worker
>   sessions — tried, flaky), so "offline" navigations came back as live pages. The suite's factory
>   (`OfflineE2EWebAppFactory.NetworkDown`) drops every connection server-side as well. The CI E2E job
>   now builds the client bundle; it didn't, and the shell needs it.

- [x] **Add `GET /api/session`** (authenticated, 401 when not) returning `{ testerId, displayName, certificateNo, licenceExpiryDate, serverTime }`. Extend `Api/ProfileController.cs` rather than adding a controller. This doubles as the reconnect gate (re-evaluating the licence/terms checks that only run at sign-in) and as the third connectivity state — today `/health` is anonymous, so `connectivity.ts` cannot distinguish "no network" from "network but dead session".
- [x] **Persist the identity record ~~to the per-tester IndexedDB~~ (its own `autorep-identity` database — see STATUS)** on every successful online load, and have `currentTesterId()` (`testStore.ts:153-156`) fall back to it. See Decision (a) — this is *not* localStorage, and it comes with the explicit single-tester-per-device-offline rule.
- [x] **Build `wwwroot/app-shell.html`**: a static, identity-free document with the header chrome markup, tester nav (`/App`, `/App/Tests/Index`, `/App/Tests/New` — hardcode the literals; `TestListApp.tsx:101,123,157` already hardcodes exactly these), footer, an empty mount point, and `<script type="module" src="/js/dist/autorep.js">`. No name, no initials, no licence banner, no antiforgery token, no `window.__autorep*`.
- [x] **Render the chrome client-side** (in the shell; the server layout keeps its own — see STATUS) from the cached identity record: initials + display name (replacing `_Layout.cshtml:78-79`), and the licence banner (replacing `_Layout.cshtml:89-111`, whose day arithmetic at `:19-22` is trivially client-computable — and will then stay *correct* as days pass offline, which is better than today). Remove the `@inject UserManager` at `_Layout.cshtml:1` and the per-request `GetUserAsync` at `:16` — it is the only DB query on three of the four tester pages.
- [x] **Route navigations to the shell in the SW.** Replace `sw.js:126-129` with: network-first, and on failure, if the pathname is in the tester allowlist, serve the cached shell. The allowlist must be **pathname-based** and must cover `/`, `/App`, `/App/Tests/Index`, `/App/Tests/New`, `/App/Tests/Wizard` — the wizard is always visited with a query string (`main.ts:17-24` reads `?id`/`?farmId`/`?farmName`), so matching on `event.request` would miss. Everything else keeps the current offline card. `/Admin/*` explicitly excluded.
- [x] **Handle the cold launch.** `manifest.webmanifest:5` is `start_url:"/"` and `Pages/Index.cshtml.cs` is a server-side role redirect. **Recommendation:** keep `start_url:"/"` and have the SW serve the shell for a failed `/` navigation — a changed `start_url` may not take effect on already-installed iPad PWAs without a reinstall (unverified; do not risk it on devices already in the field). The shell then does the role branch client-side from the cached identity record.
- [x] **`/Account/*` offline dead ends.** (Moved; the disabling half superseded — see STATUS.) `sw.js:118-120` short-circuits `/Account/` *before* the navigate branch at `:126`, so tapping your own name (`_Layout.cshtml:77`) offline gives the browser's raw network-error page. Move the `/Account/` check after the navigate branch so it at least gets the branded card, and disable the Manage link + Sign out button in the shell when `isServerReachable()` is false.
- [x] **Connectivity + pending-work indicator in the shell chrome** (and the server header: `Client/shell/AppStatus.tsx`; three states — online, offline, signed out — from `/health` polling plus `/api/session` on load, reconnect and focus, never on the 30 s timer, which would keep an idle session alive for ever), driven by `useServerOnline()` (`connectivity.ts:27-57`, already written and polling-aware) plus a count of `syncState === "local-only"` from IndexedDB. Once pages serve from cache, the *absence* of this indicator is actively misleading. Depends on Phase 0 being done or the count lies.
- [x] **Don't block first paint on the reference syncs.** (Each sync is split into apply-cached and refresh; the page mounts on the cached set, and a `autorep:reference-refreshed` event re-renders the wizard if anything changed.) `main.ts:42-44` mounts only in `.finally()` after `Promise.allSettled` of six untimed fetches. Mount first, let the syncs land in the background and re-render — all four reference syncs already apply the cached value before fetching, so early mount is safe. Give each sync the `connectivity.ts` timeout pattern.
- [x] **Widen tester-page detection.** (`isTesterPath` — `/App/*`.) `main.ts:36-41` keys off `test-list-root` / `wizard-root`; from a shell those are absent until the client renders them. Move to a path check or a body-level data attribute so `initFarms`/`initCalibration` still run.
- [x] Playwright offline suite (see §7).

**Files:** `wwwroot/app-shell.html` (new), `wwwroot/sw.js`, `Pages/Shared/_Layout.cshtml`, `Api/ProfileController.cs`, `Client/main.ts`, `Client/db/testStore.ts`, `Client/ui/` (new shell chrome component), `Client/connectivity.ts`, `Pages/App/Index.cshtml`.
**Estimate: 5–8 days.**
**Ships:** the goal statement's first half. Launch the installed app with no signal → land on the tester home → open My tests (already reads purely from IndexedDB, `TestListApp.tsx:40-45`) → resume a saved test in the wizard → capture and print it → sync when back in range. Only *starting* a test is still blocked.

---

### Phase 3 — Start a test offline (client-rendered `/App/Tests/New`) ✅ DONE Oct 2026

> **STATUS (7 Oct 2026, branch `claude/offline-tester-new-test`, stacked on Phase 2).** Covered by
> `OfflineTesterE2ETests` (start a test offline from the cached book; add-a-farm offline vs online),
> `NewFarmTests`, `FarmsControllerTests` and `Client/sync/farmsSync.test.ts`. Departures:
>
> - **The farm book's version stamp is an ETag + 304, not `{version, items}`.** The body stays a plain
>   array, so a device still running the previous bundle keeps working through a deploy. The ETag is
>   over the exact bytes (deterministic order, `ThenBy(Id)`), and the response is `no-store` so the
>   browser's own HTTP cache never keeps farmers' details on a shared device.
> - **One new endpoint after all: `GET /api/farms/new-farm-options`** (regions + milk companies).
>   The "no list endpoints needed" note under Phase 5 assumed the modal stayed server-rendered; once
>   the page is drawn on the device the online-only form still needs its pick lists. Reference data,
>   fetched when the form opens.
> - **Not `Combobox.tsx`:** it works on strings, and two farms can share a name. A small farm picker
>   shows supply number / town / processor so same-named farms can be told apart, and a typed name
>   only counts as a choice when it matches exactly one farm.
> - **The old page's OnPost scope tests are gone with the handler.** Inactive farms: the picker only
>   offers the cached book, which is active farms only; a farm deactivated since the device last
>   synced can still be picked offline, and the sync push lands the test on it rather than losing it
>   (comment updated in `SyncController.ResolveFarmAsync`).
> - **Added to the POST:** an unknown or inactive region/milk company is a 400, not a foreign-key
>   failure; a sync-only session gets 403; the review email isn't tied to the request's cancellation.
> - **Logos:** only the ones the device doesn't already hold are fetched, after a changed book; an
>   unchanged book re-checks once per tab session (tops up a capped or evicted logo cache).
> - **Found on the way — deploy skew.** A page the bundle draws can arrive before the new worker has
>   taken over, so the previous build's cached bundle runs and may not know the page (New test,
>   straight after this ships). Bundle mount points are now marked `data-bundle-root` with a
>   "Loading…" placeholder; when the new build arrives and a root is still empty, `pwa-register.js`
>   reloads into it once instead of showing the banner (nothing was drawn, nothing to lose), and a
>   root still empty 10 s after load gets a "Try again" bar. E2E:
>   `A_page_the_old_bundle_cannot_draw_reloads_into_the_new_build_by_itself`.

`/App/Tests/New` is the only tester page with real server data, and it is therefore the only one the PII rule forbids caching. Rewriting it removes the objection outright and finishes the goal.

- [x] **Replace the inline farm-picker IIFE** (`New.cshtml:97-196`, over the Razor-serialised array at `:100-101`) with a Preact component mounted on an empty root, reading `getCachedFarms()` (`farmsSync.ts:41`). Reuse `Client/ui/Combobox.tsx`. Delete the 5 DB queries in `New.cshtml.cs:144-171` that feed it.
- [x] **Add the bundle to this page** — it currently loads no `autorep.js` at all, which is also why the farm book never refreshes on the one page that needs it.
- [x] **Replace the POST→`RedirectToPage`** (`New.cshtml:15`, handler `New.cshtml.cs:58-79`) with a client-side navigation to `/App/Tests/Wizard?farmId=…&farmName=…` — the wizard already accepts exactly those params (`main.ts:20-21`). The server-side scope re-check is not lost: it still happens on `GET /api/farms/{id}` and on sync push (`SyncController.ResolveFarmAsync`), and an offline picker can only offer farms from the tester's own already-scoped cached book.
- [x] **Render the privacy notice from the client cache** (`Client/config/privacyContent.ts`, already synced by `privacySync.ts`) instead of the DB query at `New.cshtml.cs:169-170`. One query dropped for free.
- [x] **Give `/api/farms` a version stamp.** (As an ETag — see STATUS.) `farmsSync.ts:28-38` re-downloads the entire company farm book on *every* tester page load with no version/ETag — contrast `standardsSync`/`equipmentSync`, which carry one. Once navigation is cached and testers move freely between four screens this is a multi-MB fetch per navigation. Add `{version, items}` to `Api/FarmsController.cs` List and short-circuit when unchanged.
- [x] **Pre-cache milk-company logos.** After a farm-book sync, iterate the distinct `milkCompanyId` values and `fetch('/api/milk-companies/'+id+'/logo')` — the SW's existing stale-while-revalidate rule (`sw.js:80-95`) does the rest, no SW change needed. This is the outstanding M2 item already flagged at `sw.js:5-6` and `plans/build-checklist.md:53`. Note logos currently appear on this one tester surface only (`New.cshtml:112-120`) and nowhere in the wizard or the PDF, so keep it cheap.
- [x] **Add-a-farm modal: online-only, clearly.** (Moved to `POST /api/farms`.) Detect `isServerReachable()` and show "you need signal to add a new farm" instead of a silent 400. **Note:** the antiforgery token read at `New.cshtml:108` comes from the *layout's* logout form (`_Layout.cshtml:81-83` renders before `@RenderBody()` at `:114`), not this page's own form. Removing or client-rendering the shell's sign-out form breaks farm creation. If the handler is kept, move it to a `POST` on `Api/FarmsController.cs` (controllers registered by `AddControllers()` get no antiforgery filter — `Client/sync/syncClient.ts:39` already POSTs with only a Content-Type header).
- [x] Drop the server-rendered role hint (`New.cshtml:83-89`) — always show the review hint; it is accurate for plain Testers and harmlessly redundant for admins. Move validation errors to the modal's own `#farm-modal-errors` div / `showToast`.

**Files:** `Pages/App/Tests/New.cshtml`, `Pages/App/Tests/New.cshtml.cs`, new `Client/ui/NewTestApp.tsx`, `Client/sync/farmsSync.ts`, `Api/FarmsController.cs`, `Client/main.ts`, `Client/config/privacyContent.ts`.
**Estimate: 4–6 days.**
**Ships:** the second half of the goal. A tester with no signal can start a test at any farm already in their book, and no tester page HTML contains PII any more.

---

### Phase 4 — Storage durability ✅ BUILT Oct 2026 (real-device UAT still owed)

> **STATUS (7 Oct 2026, branch `claude/offline-tester-storage`, stacked on Phase 3).** Covered by
> `SyncPagingAndAttachmentTests`, `PulsationPayloadTests`, `syncClient.paging.test.ts`,
> `pulsationAttachment.test.ts` and `durability.test.ts`; the offline E2E suite runs over the paged,
> attachment-free pull unchanged. Departures:
>
> - **Paging is an offset cursor over (created desc, id desc), not a keyset.** EF can't compare
>   GUIDs for a keyset, and migrated tests share timestamps. Rows are never deleted and never leave
>   the `since` window, so a shifting offset can only re-deliver a row (the pull upserts), never skip
>   one. **Revisit when O2 soft-delete lands** — a row leaving the window mid-pull could then be
>   skipped. Without `?limit=` the endpoint answers exactly as before, so older bundles keep working.
>   The device keeps the FIRST page's watermark and saves progress after each page, so an
>   interrupted first sync resumes rather than restarting; a cursor the server rejects restarts once.
> - **The attachment is dropped a week after it's safe, not "after a successful push".** Same-day
>   printing at the farm gate is the common case and often happens out of signal; dropping on push
>   would break it. A test that is complete, synced, and whose PDF has been held more than 7 days
>   lets the bytes go (pointer kept: name/size/date, `onServer`). The first pull asks for
>   `attachments=omit`, so a new device never downloads the PDF history. Printing fetches the bytes
>   back (`GET /api/sync/tests/{clientId}/pulsation-pdf`) and holds them for another week; offline it
>   prints without the attachment and says why. A pull never throws away bytes the device holds.
> - **Re-sending a pointer can't lose the bytes:** the server restores them from the stored copy,
>   the pointer's named test, or the superseded version (a new version made from an original whose
>   bytes had gone). A new version's copied pointer names the original.
> - **Since Oct 2026 the server keeps the bytes in its PDF store, not in `PayloadJson`** (branch
>   `claude/pulsation-pdfs-to-store`; `Services/PulsationAttachments.cs`). The device's side is
>   unchanged: the stored payload is the same pointer (`onServer`, `serverTestId` when another of
>   the tester's tests holds the copy) plus `sha256`, a server-side field naming the stored object
>   (`{testerId}/{holder clientId}/pulsation/{sha256}.pdf` in the `pulsation-data` container) that
>   the device carries along and never reads. "Restores" above now means: a re-sent pointer is
>   matched to the stored copy of the same attachment (name, size, attach time — PR #78's check,
>   kept) and given its hash and holder, never copied; an unmatched pointer keeps no hash. A pull
>   without `attachments=omit` still gets the bytes inline, read back from the store, so an older
>   device keeps working; the read-only view asks for the pointer and fetches the bytes through
>   `GET /api/tests/{id}/pulsation-pdf` (the view's scoping — the tester route is own-tests only).
>   With the store down a push keeps the bytes inline, as before, and a background pass
>   (`PulsationBackfill`, `PdfStore:PulsationBackfill` = Off unless set; DryRun to count, Run to move) moves
>   inline bytes — older rows included — into the store.
> - **Storage-full:** `putTest`/`putReference` turn a `QuotaExceededError` into `StorageFullError`
>   and a sticky red alert ("your latest change could NOT be saved"), instead of a rejected promise
>   nobody caught. Not reproducible in fake-indexeddb, so the detection is unit-tested and the alert
>   is in the real-device UAT.
> - **Cache caps:** the logo and FA caches trim least-recently-stored entries past 60; runtime copies
>   in the versioned cache are keyed by path, so `?v=` variants can't accumulate.


Everything above *adds* to the same origin's storage. This phase makes that survivable on an iPad.

- [x] **Paginate the first test pull.** `SyncController.cs:71-96` has no `Take`/`Skip`; a device with no watermark gets the tester's entire history, every row carrying full `PayloadJson` including base64 pulsation PDFs, written row-by-row at `syncClient.ts:144`. On a new or reset device that is plausibly hundreds of MB in one response. Add a page size + continuation, and pull newest-first in pages so "see their tests" is useful before the tail finishes.
- [x] **Drop the local base64 attachment after a successful push.** (After a week — see STATUS.) The attachment round-trips through `payloadJson` (`syncClient.ts:55`) and is re-stored on every pull. Keep a server pointer, re-fetch on demand.
- [x] **`navigator.storage.persist()`** once, after the tester has real work on-device. (iPadOS answer still unverified — UAT §3a.) There is zero storage-quota awareness anywhere in `src` today — no `estimate()`, no `persist()`, no `QuotaExceededError` handling, no cache cap. Grant behaviour on the target iPadOS version is **unverified**; test it on a real device before relying on it.
- [x] **Surface `navigator.storage.estimate()`** usage/quota next to the sync control on My tests, and wrap `putTest`/`putReference` so a `QuotaExceededError` produces a real message rather than a rejected promise nobody catches.
- [x] **Cap `LOGO_CACHE` and `FA_CACHE`** with a simple LRU on put (`sw.js:9-10`), and stop the unbounded accumulation of superseded `?v=` / hashed-chunk entries — `sw.js:62-71` only prunes caches by *name*, so within one `CACHE_VERSION` nothing is ever evicted.
- [x] **Decide the SW update path** (Decision (c)) — **decided 15 Sep 2026: prompt-to-reload.** `skipWaiting()`/`clients.claim()` stay; `wwwroot/js/pwa-register.js` listens for `controllerchange` (only on a page that already had a controller, so a first install is silent) and shows an "AutoRep has been updated — Reload to get the latest" banner. Found on the way: the install's `cache.add(asset)` went through the browser's HTTP cache, and the unhashed shell assets (`site.css`, `pwa-register.js`) are served without `Cache-Control`, so a new worker could seed its freshly named cache with days-old bytes — the stamp renamed the cache but not its contents. Precache now uses `cache: 'reload'`, and the unversioned `<script>` tags carry `asp-append-version`. Verified end-to-end on 15 Sep: old worker → new worker → banner → reload. Still open from the original note: a tester who loads during a deploy window and then drives out of coverage before the new install finishes.
- [x] Document a real-device UAT case: install to home screen, capture a test, airplane mode, wait >7 days, cold launch. (`plans/test-schedule.md` §3a — still to be RUN.) iOS caps script-writable storage at 7 days for sites without "interaction"; installed PWAs are exempt, but this has **never been tested against the target iPads** and nothing in `plans/test-schedule.md` covers it.

**Files:** `Api/SyncController.cs`, `Client/sync/syncClient.ts`, `Client/db/testStore.ts`, `Client/ui/TestListApp.tsx`, `wwwroot/sw.js`, `wwwroot/js/pwa-register.js`, `plans/test-schedule.md`.
**Estimate: 3–5 days.**
**Ships:** a device that has been in the field for weeks still has its data; a new device's first sync doesn't stall or blow the quota.

---

### Automatic sync on reconnect (PRD story 34) ✅ BUILT Oct 2026

Not a phase in the original plan; Codex flagged it as a contracted gap. Before this, `syncAll()` ran
only from the sign-off attempt and the Sync now buttons, so a test completed offline stayed
device-only until someone remembered to press one. Branch `claude/offline-tester-auto-sync`.

- **`Client/sync/autoSync.ts`.** On tester pages, a sync starts by itself when there are
  `local-only` tests and: the page loads with a good session, the browser's `online` event fires, the
  tab comes back into view, or the session check turns "online" again (after signing back in).
  A failed attempt backs off: 30 s, 1, 2, 5, 10, then every 15 minutes. A success resets it. A lapsed
  session waits for sign-in rather than retrying on a timer. No Background Sync: iPad Safari lacks it.
- **One sync at a time** (`syncAll`). A call made mid-sync gets one more run afterwards, shared by
  every caller meanwhile, so a test marked complete during a background sync is still sent.
- **Found and fixed on the way:** a push marked its *snapshot* as uploaded after the network round
  trip, so an edit made while the request was on the wire was overwritten in IndexedDB — and the
  edited copy marked as sent. With syncs now starting on their own mid-edit, that went from a corner
  case to a likely one. `markSent` re-reads the test and only marks what was actually sent; an edit
  made meanwhile stays `local-only` for the next push.
- The header shows "Online · sending N…" while a sync runs.
- Tests: `autoSync.test.ts` (triggers, backoff, sign-in pause, no overlap), `syncClient.race.test.ts`
  (edit during push; one at a time), E2E
  `A_test_captured_offline_goes_up_by_itself_when_the_connection_returns`. The sign-out E2E now
  keeps its test unsent with a server-side "refuse pushes" switch, since online work sends itself.

---

### Codex review follow-ups (7 Oct 2026)

Codex reviewed #73, #75, #76 and #77; they merged before its comments were addressed. Six held up
and are fixed on `claude/offline-codex-followups`; one didn't.

- **Lapsed licence offline (#73, P1).** `/Account/FinishSync` now refreshes the identity record, and
  the shell treats a pure tester whose cached licence date has passed as sync-only whatever the record
  says (`homeFor`, same rule as `LicenceScope.IsSyncOnly`) — a licence can lapse while offline.
- **Two testers' data mixing (#73, P1).** In the shell, account-scoped refreshes (farm book, profile,
  calibration, branding) wait for the session check, and the page reloads as the tester actually
  signed in before any of their data is fetched. **Found while fixing it, and worse:** a sync started
  in that window would have pushed the previous tester's unsent tests under the new tester's sign-in,
  filing them as the new tester's work. `syncAll` now refuses when the page's store belongs to
  someone other than the signed-in tester (`StoreOwnerChangedError`). E2E
  `A_device_changing_hands_never_mixes_two_testers_data` fails without the fix.
- **Wrong analyser PDF on restore (#76, P1).** The server restores a pointer's bytes only from a
  stored copy of the same attachment (name, size, attach time); otherwise the pointer stays, and the
  report prints without the PDF rather than with someone else's.
- **Duplicate farms on retry (#75, P2).** The add-farm form sends one id per farm; a retry returns the
  farm already saved (no second row, no second email), and the timeout message no longer claims
  nothing was saved.
- **First capture unprotected (#76, P2).** The persistent-storage request also fires on the page's
  first test write, not only at startup.
- **Edits stranded after an auto-sync (#77, P2).** After a successful sync, if anything is still
  unsent (an edit made while it ran), a follow-up runs 15 s later.
- **Not changed (#73):** "honour sync-only for dual-role testers". The server never marks an
  administrator who also tests as sync-only (`LicenceScope.IsSyncOnly`), so the client already
  matches it.

---

### Phase 5 — Offline farm creation — ~~CONDITIONAL~~ **CUT (22 Jul 2026)**

**Decided: farm creation stays online-only.** Josh confirmed with a tester that farms are not set
up on-farm without signal, which closes the open question at `plans/build-checklist.md:175`. This
phase is **not being built** — it was the single highest blow-up risk in the plan (6–10 days) and
removing it takes the total from 25–40 days down to 19–30.

Consequences to hold onto, since they are now permanent design constraints rather than temporary gaps:

- **The add-farm modal stays a live `fetch`.** It must fail *honestly* offline rather than showing
  the current bare "Could not add farm." — see Phase 3, which now carries that item.
- **`SyncController.ResolveFarmAsync` still mints farms** and must NOT be removed. It is not a
  user-facing creation path; it is the safety net that stops completed field work being stranded
  when a pushed test cannot be linked. It correctly flags those farms for review
  (`PendingReviewSince` + `FarmReviewNotifier`, PR #35), which is exactly why that work mattered.
  So "online-only" is a product rule, not an enforceable invariant.
- **No `/api/regions` or `/api/milk-companies` list endpoints are needed.** Neither exists today
  and neither is now required, because the only surface that wanted them offline was the modal.
- **No `pendingFarms` store and no `DB_VERSION` bump.** The IndexedDB schema stays at v2, which
  removes the migration-path risk for devices holding unsynced work.
- **A tester still needs signal once, at the start of a visit,** to pick a farm that is not yet in
  their cached book. Phase 3 narrows that to genuinely-new farms only.

---

## 5. Hard design decisions

### (a) Auth / session offline

**Constraint.** The cookie is the only credential the client uses; it is HttpOnly, so JS can never read it or test it (`Program.cs:66-74`, `syncClient.ts:8`). Identity reaches the client only as a server-rendered script tag (`_Layout.cshtml:157`). The JWT + refresh-token stack exists server-side but is completely orphaned — nothing calls `/api/auth/*` and no `[Authorize]` sets `AuthenticationSchemes`, so `Identity.Application` remains the default scheme.

**Options.**
1. Mirror `__autorepTesterId` to **localStorage** and fall back to it. Five of six mapping passes recommended this as "low risk". **It is not.** With an identity-free shell plus a localStorage fallback, tester B cold-launching offline on tester A's device resolves `dbName()` to `autorep_<A>` and opens A's tests and A's cached farm book — and the purge no-ops because `last === current`. Offline there is no way to distinguish "same tester, no server" from "different tester, no server".
2. Wire up JWT so the client holds a readable token with `sub`/`exp`. Gives a real assertion and the PRD's 7-day window, at the cost of a bearer credential at rest on a shared device.
3. Local unlock: PBKDF2/WebCrypto PIN verifier set at online login, required at offline launch. Strongest, largest build, needs lockout + recovery.

**DECIDED 22 Jul 2026 — "online handover" (option 1, no PIN).** Josh's framing: a shared device is realistically always online between users. That is stronger than it first appears, because **login is inherently an online event** — `/Account/Login` is a server POST, `Login.cshtml` sets `Layout = null` and loads no bundle, and `sw.js` excludes `/Account/*`. So identity can only ever be *established* or *changed* online. "Offline = one tester per device" is therefore not a rule we impose; it is a consequence. Option 3 (PIN/local unlock, 4–6 days) is **not needed**.

Stress-tested from four adversarial angles (data loss, isolation/PII, auth edge cases, operational reality). All four returned *holds-with-changes*: none could break the identity claim itself, and each broke machinery it leans on. The corrections are folded in below and into Phase 2.

**Two blockers that must be fixed before the post-login gate ships — otherwise it instructs testers to do something impossible:**

- **Expired licence is an unrecoverable lockout.** `Login.cshtml.cs:73-79` signs a pure tester out when their licence has expired, and `AuthController.cs` blocks the JWT path identically. So "have them sign in and sync" cannot be done by exactly the tester most likely to be stranded, and their captures are lost permanently. `_Layout.cshtml:95` already promises *"You can finish syncing existing tests"*, which is false today. **Fix: a sync-only session for an expired pure tester** — issue the cookie with a marker claim and land them on a page that can only run `syncAll()`. The API has no licence check, so the push succeeds and attribution stays correct.
- **The identity record cannot live in the per-tester IndexedDB.** `dbName()` needs the tester id to open the database, so reading the id from inside it is circular. It must go in localStorage or a dedicated unnamespaced `autorep_identity` database, holding only `{testerId, displayName, licenceExpiry, writtenAt}` — never farm data, so the shared location stays low-sensitivity.

**Amendments to the design as proposed:**

- **Never *block* sign-out** (proposed item 6). `isServerReachable()` reads a captive portal's 200 on `/health` as "online", and after an offline day the 8h cookie is usually dead so the sign-out POST cannot reach the server anyway. Blocking creates a handover deadlock whose learned workaround is "clear website data", which destroys the work. Warn with an explicit acknowledgement instead.
- **Make the reachability probe authenticated.** Use the planned `GET /api/session` rather than anonymous `/health`, and require a JSON content-type, so a captive portal reads as offline and the shell gets the third state it needs: network-but-dead-session.

**What no code can fix.** If tester A hands an unlocked iPad to tester B mid-run and nobody signs out, B captures tests inside A's session and they are attributed to A — a certification traceability failure, since the sign-off carries a named, certificated tester. No login or sign-out occurs, so every gate in this design is bypassed. This is a **process** control (one device, one tester, per test), but two cheap changes make the rule enforceable rather than aspirational: render the session tester's name and certificate number *inside* the Review & Sign-off card (`ReviewSignOffStep.tsx` currently shows farm and plant, never who is signing), and carry that into the attestation so it round-trips into `PayloadJson` and is auditable server-side.

Still true regardless: no client-side scheme survives a determined holder of an unlocked device. The cached identity is a routing hint, not a credential; device passcode/MDM is the real control.

Also: **the 8-hour sliding cookie (`Program.cs:70-71`) is shorter than a field day.** Lengthening it is an auth decision with a stolen-device cost (force-logout via `UpdateSecurityStampAsync` only reaches an online device). Recommendation: fix the 401 behaviour first (Phase 0), then decide the lifetime with Josh — the failure mode after Phase 0 is a clear "sign in again to sync", with all work safe on-device, which may be acceptable at 8 hours.

### (b) App shell vs client-side router

**RECOMMENDATION: cached identity-free shell, no router.**

Reasoning: there are only four tester routes, and three of them carry zero server data — `Pages/App/Index.cshtml` is static tiles with an empty PageModel, `Pages/App/Tests/Index.cshtml` is one `<div id="test-list-root">`, `Pages/App/Tests/Wizard.cshtml` is one `<div id="wizard-root">`. The mounting contract is already URL-driven (`main.ts:17-24`), so a router would have to reproduce it. Each route mounts an independent Preact tree with no shared client state, and IndexedDB is the state store — a full document load costs a re-render, nothing more. A router would not solve the actual blocker (`New.cshtml`'s server data), whereas Phase 3 solves it for either approach. Shells also keep the online experience byte-identical, so there is no regression surface.

The one real argument for a router — keeping the tester's name out of cached HTML — is answered by making the shell identity-free, which we have to do anyway.

Caveat to carry: the layout's two inline scripts (`_Layout.cshtml:121-132` burger, `:133-154` table filter) bind once at parse time. They cache fine under this design; they would need rework under a router.

### (c) `asp-append-version` / the stale-bundle problem

**The failure mode.** `asp-append-version` mints a new URL per deploy; `caches.match` at `sw.js:135` matches on the full URL including the query. So today three precache entries are permanently dead, and offline coverage of CSS/JS is *accidental* — it works only because the runtime branch stored the versioned URL on a previous online load. Add a cached shell and it gets worse: the shell pins a `?v=` and a content-hashed chunk graph that the next deploy deletes (`wwwroot/js/dist` is gitignored and the App Service deploy replaces the directory), and a miss returns an empty 504 for a `<script type="module">` — a silent blank page.

**RECOMMENDATION: all four, they are complements not alternatives.**
1. `{ ignoreSearch: true }` on the same-origin static match — one line, makes hash churn survivable offline.
2. Drop `asp-append-version` from the assets the SW owns; let `CACHE_VERSION` be the busting mechanism.
3. Build-stamp `CACHE_VERSION` from the esbuild metafile. A hand-typed constant that no script touches will be forgotten.
4. A runtime guard: a `/js/dist/` request that misses cache and 404s on the network triggers `registration.update()` + one reload, instead of a 504.

Deliberately **not** recommended: keeping a previous build's chunks on the server for N releases. It would help, but it means changing the deploy to merge rather than replace, which is a bigger blast radius than the guard.

### (d) What is safe to cache, given shared devices and farm PII

The rule stated at `sw.js:23-25` stands and should be written into the shell's own header comment:

- **Safe:** the compiled bundle and its chunks, `site.css`, vendored FA CSS + webfonts, self-hosted brand fonts, brand SVGs, PWA icons and splash images, the manifest, milk-company logos (reference data), and a **static identity-free shell document**.
- **Not safe, ever:** any rendered Razor output of `/App/*` or `/Admin/*`; any `/api/*` JSON containing farms or tests. Those belong in the per-tester IndexedDB.

Two consequences the plan already carries: the shell must render name/initials/licence client-side (Phase 2), and `/App/Tests/New` must stop inlining the farm book (Phase 3). Add one more: **clear the shell/API caches on sign-out** — `Pages/Account/Logout.cshtml.cs` calls `SignOutAsync` only, and there is no `caches.delete` anywhere outside `sw.js`.

The Playwright suite must assert this mechanically: no cached `Response` body contains the tester's name/email or any farm name. If that test cannot be written, the shell strategy is not verifiable and should not ship.

### (e) Storage quota and eviction

**The exposure.** Nothing in `src` calls `navigator.storage.estimate()` or `persist()`, handles `QuotaExceededError`, or caps a cache. This plan proposes to add a shell, ~2.4 MB of PDF chunks, the full farm book on every load, and an unpaginated test history with embedded base64 PDFs — all to the same origin. On WebKit an origin-wide eviction takes IndexedDB with the Cache API, i.e. **unsynced captures**.

**RECOMMENDATION:** treat quota as a Phase 4 gate on the whole feature, not a nice-to-have. Specifically: paginate the first pull (the single biggest lever), drop local attachment copies after upload, request persistence, surface usage, cap the logo/FA caches, and **warm** rather than **precache** the PDF chunks. Then run the real-device 7-day test before declaring M2 done.

This is the item most likely to produce "worked in testing, failed in the field", and the hardest to catch in CI.

### (f) Offline farm creation

**RECOMMENDATION: defer.** Ship "pick from the cached book" (Phase 3) and take the answer to NZMPTA. The degraded path already exists via `ResolveFarmAsync`. Full-fidelity creation is 6–10 days across two new endpoints, two syncs, a DB version bump with a migration for devices holding unsynced work, a sync-envelope change, and an unresolved id-reconciliation question. It should not ride along inside another phase.

---

## 6. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Shell caching destroys a tester's IndexedDB via `purgeStaleLocalData` (`testStore.ts:183`) | **High** if Phase 0 is skipped; near-zero after | Phase 0 lands first and is independently shippable; add a Vitest case for the null-identity path |
| Offline identity lets tester B open tester A's data on a shared device | Medium | Explicit "offline = one tester per device" rule (Decision a); identity in the per-tester DB, not localStorage; unsynced-work check before any handover purge |
| A deploy strands cached shells → blank page for every tester until they clear storage | Medium-high without Phase 1 | `ignoreSearch`, build-stamped `CACHE_VERSION`, 404→update-and-reload guard, `cache.add` per URL |
| iOS evicts the origin (7-day rule or quota) and takes unsynced tests with it | Medium, **high impact** | Phase 4: persistence request, pagination, attachment drop, cache caps; real-device UAT case; keep the shell small |
| Cached shell out-of-sync with an 8-hour cookie → signed-in chrome for a dead session | High (a field day exceeds 8h) | Phase 0 401 handling + a distinct "sign in again to sync" state; decide the cookie lifetime separately |
| PII lands in the shared Cache API by accident (a future page, a careless SW rule) | Medium | Path allowlist in the SW, and a Playwright assertion that no cached body contains tester or farm strings |
| `/App/Tests/New` rewrite is larger than "the farm picker" | Medium | Phase 3 enumerates all seven things that page supplies (picker, region/milk selects, privacy notice, role hint, error block, antiforgery token, logo `<img>`, POST handoff); the antiforgery token in particular comes from the **layout's** logout form |
| Offline farm creation balloons | Medium if started | Gated behind an NZMPTA answer; separate phase; not bundled |
| Warming 2.4 MB of PDF chunks on rural mobile data annoys testers / burns their plan | Medium | Warm after first successful sync, not on install; show a "ready to print offline" state; honour `saveData` where available |
| None of this is covered by tests, so regressions are invisible | **High today** | §7 — the Playwright offline suite is a deliverable, not a footnote |

---

## 7. Testing strategy

Current coverage of anything in this document: **zero**. `tests/Autorep.Web.Tests/E2E/` is three files (`E2EWebAppFactory.cs`, `FarmAutocompleteE2ETests.cs`, `StubNzPostHandler.cs`), and grepping the tests tree for `sw.js` / `serviceWorker` / `offline` returns only comment hits. On the client side there are twelve Vitest files, and **`Client/sync/farmsSync.ts` has none** — no test for `initFarms`, `getCachedFarms` or `getCachedFarm`, despite the farm book being the data source for the entire offline picker. `Client/sync/syncClient.ts` has none either.

**Vitest (fast, runs in CI already):**
- [x] New `Client/sync/farmsSync.test.ts` (Phase 3, plus the ETag/304 and logo-warming cases) — full replace on success, cache untouched on non-ok, cache untouched on throw, `getCachedFarm` hit/miss, empty-on-never-synced. `fake-indexeddb` is already a devDependency.
- [x] New `Client/sync/syncClient.test.ts` (Phase 0) — continue-on-push-failure, pull still runs after a failed push, redirect-to-HTML treated as failure (not `uploaded`), watermark only advanced after all rows stored.
- [x] Extend `Client/db/testStore.test.ts` (Phase 0, `testStore.purge.test.ts`; the shell's identity-record path in `testStore.identity.test.ts`) — purge no-ops on null identity; purge refuses when the outgoing DB holds `local-only` tests.

**Playwright (offline is the point):**
- [x] `context.setOffline(true)` is the primary lever — **but not enough on its own**: in Chromium it doesn't reach the service worker's fetches, so the suite also drops connections server-side (`OfflineE2EWebAppFactory.NetworkDown`; Phase 2 STATUS). Sequence per case: load online as a seeded tester → wait for the SW to control the page (`navigator.serviceWorker.ready` + a `controllerchange` await) → `setOffline(true)` → act.
- [x] Cold launch offline: `setOffline(true)`, `page.goto('/')`, assert the shell renders and the nav is present.
- [x] Navigate offline: home → My tests → open a saved test in the wizard → assert steps render and a value persists across a reload.
- [x] Capture offline (a test started offline and edited, not completed end to end — the wizard happy path is T5's job): complete a test end to end, assert it lands in IndexedDB with `syncState:"local-only"`, then `setOffline(false)` and assert it pushes.
- [x] Print offline: after one online sync (which warms the chunks), go offline and assert the PDF blob is produced.
- [x] **PII assertion:** enumerate `caches.keys()` → every entry's body, assert none contains the tester's display name/email or any seeded farm name. This is the test the whole shell strategy rests on.
- [x] Deploy-churn recovery (simulated by caching an entry whose chunk the server doesn't have; the renderer's memory cache has to be switched off or Chromium never asks the worker — a real cold launch has nothing in it): change the bundle hash and delete the old chunk, then assert the app recovers rather than 504-looping.
- [x] SW cache contents after first online load match the expected key set (now including `/app-shell.html` and the bundle).

**xUnit:**
- [x] `/api/*` returns 401 (not a 302 to login) for an unauthenticated request — the Phase 0 fix (`ApiChallengeTests`, now including `/api/session`).
- [ ] `GET /api/sync/tests` pagination: page size honoured, continuation correct, no rows dropped or duplicated across pages.
- [x] `GET /api/session` shape + 401 when unauthenticated (`SessionEndpointTests`, `ApiChallengeTests`).

CI already runs Vitest + typecheck and E2E as its own job (`.github/workflows/app.yml`), so the offline cases slot into the existing E2E job with no new infrastructure.

---

## 8. Estimate and contract position

| Phase | Days | Notes |
|---|---|---|
| 0 — Sync + purge correctness | 2–3 | Live data-loss fixes; ship first regardless |
| 1 — Cache honesty + build coupling + offline printing | 3–5 | Build stamping is the uncertain half |
| 2 — Identity-free shell + offline navigation | 5–8 | The headline |
| 3 — Client-rendered New-test page | 4–6 | Removes the PII objection outright |
| 4 — Storage durability | 3–5 | Gate on real-device UAT |
| Playwright offline harness | 2–3 | Could be folded into Phase 2 |
| **Total (Phases 0–4)** | **19–30 days** | |
| ~~5 — Offline farm creation~~ | ~~6–10~~ | **CUT 22 Jul 2026** — tester confirmed farms are set up online only |

**Contract position.** The build is M1–M6 + O1 + O2 + O3 (~$43,750). Essentially all of this is **already-contracted M2**, not new scope:

- *"render tester pages offline"* is an **unticked M2 item** — it is written verbatim into `plans/build-checklist.md:53`, alongside *"pre-cache all active logos on sync"* (Phase 3).
- `plans/build-checklist.md:50` currently claims **✅ "Service worker caches app shell for offline UI"**. That line is wrong and should be downgraded to 🟡 — no page HTML is cached and navigations fail. This plan is what makes that tick honest.
- Offline/online connectivity banner: M2, `build-checklist.md:61` ("full offline/online connectivity banner still to come") → Phase 2.
- Offline reprint of historical tests: M2, `build-checklist.md:60` → the pagination and attachment work in Phase 4, plus the chunk warming in Phase 1.
- Background/login sync: M2, `build-checklist.md:59` → partly Phase 2 (mount-first, background syncs).
- Offline lifecycle testing: M2, `build-checklist.md:62` ("offline lifecycle still manual UAT") → §7.

**Genuinely new scope, arguably:**
- **Phase 0** is bug-fixing on already-delivered M2 sync work. Not new scope; it is warranty.
- **Phase 5 (offline farm creation)** is the only candidate for a variation, and only if NZMPTA answers `build-checklist.md:175` with "testers create farms on-farm". It touches the domain model (new store, DB version bump, sync envelope change) and is the one place where the honest answer is "this is bigger than the M2 line item assumed".
- **Cookie-lifetime / offline-session policy** (Decision a) is a security decision, not a build item, and needs Josh's call before Phase 2 finishes.

---

## 9. Open questions

Carry these into the next NZMPTA/Josh conversation. Each one changes a phase.

1. **Do testers create farms on-farm?** (`build-checklist.md:175`) Decides Phase 5 entirely.
2. **What is the intended maximum offline stretch?** 8 hours (current cookie), a full day, or multi-day? Decides whether the cookie lifetime changes.
3. **Is "offline = one tester per device" acceptable?** If not, Phase 2 grows a PIN/local-unlock (Decision a option 3) and the estimate goes up by ~4–6 days.
4. **Must the licence-expiry banner be accurate offline?** It is advisory; enforcement is at login. If online-only is acceptable, drop it from the shell entirely.
5. **Does `navigator.storage.persist()` get granted on the target iPadOS?** Unverified. Needs a real-device check before Phase 4 is scheduled.
6. **Does a changed `start_url` require a reinstall on already-installed iPad PWAs?** Unverified — the plan avoids the question by keeping `/` and handling it in the SW, but confirm before any manifest change.
7. **PDF logo branding** is a known upcoming item. If a logo is added to the report it becomes a new offline asset dependency; decide now whether it is embedded in the bundle or cached as a data URI, not after this work lands.