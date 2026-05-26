# PRD: NZMPTA AutoRep Rebuild — Tester PWA, Wizard, Reports, Admin Portal, Data Migration & Admin Edit (M2–M5 + O1–O3)

## Problem Statement

NZMPTA testers currently use AutoRep Plus, a Windows-only desktop application built by a prior vendor with whom NZMPTA has no ongoing relationship. The current application:

- Cannot be installed on iPads, macOS or Android. Testers either carry a Windows laptop on-farm or wait until they're back at a workstation to enter their results.
- Walks every Tester through every test section regardless of whether the equipment is present on the machine, producing Reports cluttered with N/A regions and creating opportunities for mis-entry.
- Ships with hardcoded database credentials, has unresolved bugs in the sync and reporting pipeline (Vacuum Pump Speed/Capacity fields not populated, broken admin-edits-not-reaching-Testers flow), and carries only group-level fault severity.
- Requires developer involvement to update Reference Data (Equipment Catalogues, Vendor Specifications) and the Test Standard Manual, even though these change only once or twice a year.
- Has no NZMPTA-wide reporting and no per-Testing-Company administrator visibility.

NZMPTA needs a modern web-based platform that works offline on any device a Tester carries to the farm, with Reference Data managed by NZMPTA themselves, and Reports that reflect the Machine actually under test. The upstream **Requirements & Scope** document (v1.1, 26 May 2026, in `docs/NZMPTA_AutoRep_Rebuild_Requirements_v1_1.docx`) defines *what* the rebuild does; this PRD specifies *how* the committed scope (M2 + M3 + M4 + M5 + O1 + O2 + O3 — $43,750 ex GST) will be implemented.

## Solution

A web-based platform built on .NET 10 / ASP.NET Core Razor Pages + API controllers, hosted in Azure (NZ North), delivered to Testers as a Progressive Web App. Two surfaces share a codebase and database but are role-gated:

- **Tester PWA** — installable on Windows, macOS, iPad and Android; fully offline-capable; the Tester walks through a **Wizard** that shows only the **Wizard Steps** relevant to the **Machine Configuration** declared upfront; live pass/fail computed from cached **Vendor Specifications**; **Final Reports** generated on-device on Mark-as-Complete so the Tester can print/download with no connectivity; completed **Machine Tests** sync when the **Device** is next online.

- **Admin Portal** — online-only NZMPTA administration surface for **Tester** / **Testing Company** / **Equipment Catalogue** / **Vendor Specification** / **Test Standard Manual** / **Standard Recommendation Wording** management. **NZMPTA Super-Administrator** can also browse, view and edit any **Machine Test** with full audit trail (O2).

A standalone **Migration Tool** (O1) ports historical **Machine Tests** and **Vendor Specifications** from the legacy Azure SQL database into the new schema, with per-row data-quality reports and dry-run cycles before cutover.

The architecture extracts ten deep modules — **Pass/Fail Calculator**, **Wizard Step Resolver**, **Fault Aggregator**, **Sync Reconciliation Engine**, **Test Versioning Engine**, **Reference Data Snapshot Service**, **Audit Recorder**, **Migration Tool**, **Report Generator** and **Final Report Composer** — tested in isolation. The **Report Generator** and **Final Report Composer** are TypeScript modules that run in the browser (used by both the Tester PWA and the Admin Portal); the others are .NET. UI and surface modules are tested end-to-end through Playwright and integration tests.

## User Stories

### Tester — authentication and licence

1. As a Tester, I want to install the PWA on my iPad once while online, so that I can use it offline at every farm I visit thereafter.
2. As a Tester, I want to log in with my email and a password I set, so that I have one credential to remember.
3. As a Tester, I want a self-service password reset by email, so that I'm not blocked when I forget my password.
4. As a Tester migrated from the legacy system, I want a mandatory password reset on first login, so that legacy credentials can't be used against the new platform.
5. As a Tester, I want a reminder six months before my Tester Licence expires, with the reminder shown on every login within the final two months, so that I have plenty of time to renew before I'm locked out.
6. As a Tester, I want the reminder to keep showing until my Tester Licence is renewed (it cannot be permanently dismissed), so that the reminder isn't lost in habit clicks.
7. As a Tester, I want my Tester Licence to be checked at login (not on every action), so that an expiry falling while I'm offline on-farm doesn't lock me out mid-Test — I can still finish and sync the work in progress.
8. As a Tester, I want my session to extend automatically when I sync (seven-day sliding refresh window), so that an actively-working Tester never has to re-authenticate.
9. As a Tester, I want my Refresh Token to expire after seven days of inactivity, so that a lost or stolen device loses access within a bounded window.

### Tester — creating and editing a Machine Test

10. As a Tester, I want to start a new Machine Test by selecting an existing Farm or creating one, so that repeat-Farm Tests don't require re-keying farm details.
11. As a Tester, I want a "Next Test" button on the Tests list that pre-populates Farm and Machine details from a prior Test, so that the second-and-subsequent visits to a Farm are faster.
12. As a Tester, I want to declare the Machine Configuration upfront (machine type, pump set, pulsator, claw, shell, liner, milkline size, herd size, last BMCC, etc.), so that the Wizard only asks me about equipment actually present.
13. As a Tester, I want the Wizard to hide subsequent Wizard Steps corresponding to equipment I don't have, so that I'm not entering N/A into fields that don't apply.
14. As a Tester, I want to pick "Other" on any Equipment Catalogue dropdown when the model isn't listed, so that I'm never blocked by an out-of-date catalogue.
15. As a Tester, I want to record Visual Faults at both the pre-start and running inspection stages, so that issues found at each stage are captured at the right time.
16. As a Tester, I want a "Guards Installed on Pulsators" boolean during the Visual Faults running step, so that this safety-critical item is captured uniformly and carried through to the Report.
17. As a Tester, I want a "Check all as verified" control at the bottom of any Wizard Step containing a checklist of items (notably the Visual Faults — Pre-Start and Visual Faults — Running Inspection steps, and any Machine Configuration sub-checklists), so that I can confirm all items in one action when the entire checklist is in order.
18. As a Tester, when I activate "Check all as verified", I want a confirmation prompt requiring me to attest something like "I have inspected all items on this page and confirm they have been seen, tested and are in order" before the bulk-tick is applied, so that one-tap completion still carries an explicit attestation and isn't an accidental click. After confirming I can still uncheck or amend individual items.
19. As a Tester, I want to enter numerical readings for the Vacuum & Reserve tests (Sections 1–7) and the Additional Tests (Sections 8–16), so that I have a structured place to capture every reading.
20. As a Tester, I want live pass/fail indicators next to each numerical entry, so that I know in real time whether a reading is within standard before I finish the section.
21. As a Tester, I want per-pulsator rates, ratios, fastest/slowest and highest/lowest ratio to be captured, with "enter only faulty rows" or "enter all" options, so that I can be efficient when most pulsators are passing.
22. As a Tester, I want to optionally skip Wizard Step 9 (Individual Cluster Tests), so that I'm not forced through it on jobs where it doesn't apply.
23. As a Tester, I want every Fault I record to carry its own Fault Severity (Critical / Major / Minor), so that I can accurately describe an installation where most issues are minor but one is critical.
24. As a Tester, I want a Fault Summary that auto-populates from issues I recorded earlier, grouped by section, so that I don't have to re-enter them at the end.
25. As a Tester, I want to choose Recommendations from a curated Standard Recommendation Wording dropdown (with the option to type my own), so that I can write Reports faster without losing the ability to write site-specific advice.
26. As a Tester, I want to set a Next Test Date when I complete a Machine Test (pre-populated to twelve months out and editable), so that the Farmer and their Company Administrator know when they're next due.
27. As a Tester, I want a final Review & Sign-Off Wizard Step that summarises the entire Test for verification before Mark-as-Complete, so that I can catch errors before committing.
28. As a Tester, I want to navigate back to any prior Wizard Step before Mark-as-Complete, so that I can correct mistakes mid-flight.
29. As a Tester, I want to delete an in-progress Machine Test I created in error, so that abandoned starts don't clutter my list.
30. As a Tester, I want to edit a completed Machine Test later (creating a new Test Version, preserving the original), so that I can correct an error without destroying the audit trail.
31. As a Tester, I want Mark-as-Complete to enable the Print / Download PDF buttons, so that there's a clear gate between draft state and Report generation.

### Tester — offline and sync

32. As a Tester, I want the PWA to remain functional with no internet (creating new Tests, editing in-progress Tests, generating Final Reports), so that connectivity gaps on-farm don't block my work.
33. As a Tester, I want to see clearly when I'm offline, when I'm syncing, and when sync has succeeded, so that I never finish my day uncertain whether my Tests are uploaded.
34. As a Tester, I want completed Machine Tests to upload automatically when the Device next has connectivity, so that I don't have to remember to trigger sync manually.
35. As a Tester, I want the Vendor Specifications I cached at the start of a Machine Test to apply to that Test all the way through, regardless of later updates, so that pass/fail thresholds don't shift under me mid-job.
36. As a Tester, I want Reference Data and the Test Standard Manual updates to download automatically every time I sync, so that I'm always working against the current standards on Tests started after a sync.
37. As a Tester, I want a copy of every completed Final Report retained on my Device, so that I can re-open, show or print it without internet.

### Tester — Reports

38. As a Tester, I want each named Report (Test Summary, Test Report Results, Visual Faults Checklist, Test Record, Additional Testing, Individual Cluster Airflow Test, Pulsation System Result) generated **client-side in the PWA on-device** from a completed Machine Test, so that I can produce, view, print and download the **Final Report** with no connectivity at the farm.
39. As a Tester, I want sections corresponding to equipment not present on the Machine hidden in the Report (not shown as N/A), so that the Report is cleaner and shorter.
40. As a Tester, I want Vacuum Pump Speed and Capacity fields correctly populated on the Report, so that the legacy bug is fixed.
41. As a Tester, I want individual Fault Severity to appear per Fault on the Test Summary, replacing the legacy group-level severity, so that the Report accurately reflects priority.
42. As a Tester, I want the Next Test Date displayed on the Test Summary, so that the Farmer can see when they're next due.
43. As a Tester, I want the Compliance Disclaimer printed on the Test Summary of every Report, so that I'm not legally exposed for omitting it.
44. As a Tester, I want to upload the Pulsation Data PDF on the summary Wizard Step (drop-zone), so that the Final Report is one combined document.
45. As a Tester, I want the uploaded Pulsation Data PDF appended to the generated Reports as the last pages of the **Final Report**, with the merge happening on-device, so that the single Final Report PDF is available offline.
46. As a Tester, I want to download or print the Final Report from the summary Wizard Step (once Mark-as-Complete), so that I can leave a copy with the Farmer before leaving the farm.

### Company Administrator

47. As a Company Administrator, I want to view all Machine Tests by Testers in my Testing Company, so that I have visibility into my team's work without seeing other companies' data.
48. As a Company Administrator, I want to be unable to view data belonging to other Testing Companies, so that confidentiality between competitors is enforced.
49. As a Company Administrator, I want to edit Farm Details and the final summary / Recommendations on Tests by my Testers, so that I can correct a Farm address or polish a Recommendation before it goes to the client.
50. As a Company Administrator, I want my edits captured as a new Test Version with a full audit trail (actor, timestamp, before/after), so that every change is traceable.
51. As a Company Administrator, I want to view company-level reports of my team's Test activity, so that I can manage utilisation and quality.
52. As a Company Administrator, I want to be unable to manage Users or Reference Data, so that platform administration remains NZMPTA's responsibility.

### NZMPTA Super-Administrator — Users and Testing Companies

53. As an NZMPTA Super-Administrator, I want to create, deactivate, and reset passwords for Testers across all Testing Companies, so that I can onboard and offboard Testers as they enter and leave the industry.
54. As an NZMPTA Super-Administrator, I want to set and renew a Tester Licence Expiry Date by simply updating a date field, so that licence renewals don't require a code change.
55. As an NZMPTA Super-Administrator, I want to force-logout a User immediately (invalidating their tokens), so that a lost or stolen Device can be locked out within minutes.
56. As an NZMPTA Super-Administrator, I want a list of Tester Licences expiring soon, so that I can proactively prompt renewals.
57. As an NZMPTA Super-Administrator, I want to create, edit and deactivate Testing Companies, so that the company directory reflects current industry membership.
58. As an NZMPTA Super-Administrator, I want two-factor authentication on my account, required every 30 days and on any new or unrecognised Device, so that an attacker who phishes my password can't access the platform.

### NZMPTA Super-Administrator — Reference Data

59. As an NZMPTA Super-Administrator, I want to manage Equipment Catalogues (Vacuum Pumps, Pulsators, Liners, Shells, Claws, Jetters, ACRs, Milk Meters, Milk Flow Indicators, Regulators, Releaser Pumps), so that new equipment models can be added without a developer.
60. As an NZMPTA Super-Administrator, I want each Vendor Specification to carry an Effective Date, so that I can schedule a future update without it taking effect immediately and disrupting in-progress Tests.
61. As an NZMPTA Super-Administrator, I want to upload a new version of the Test Standard Manual (PDF) via the Admin Portal, so that the updated rulebook is pushed to every Tester on next sync without a developer.
62. As an NZMPTA Super-Administrator, I want every prior version of the Test Standard Manual retained and downloadable, so that historical Tests remain reproducible against the manual version they were performed against.
63. As an NZMPTA Super-Administrator, I want to manage the curated Standard Recommendation Wording dropdown options, so that Testers always have current canonical recommendations available.

### NZMPTA Super-Administrator — Test browse, edit and audit (O2)

64. As an NZMPTA Super-Administrator, I want to browse and filter Machine Tests across all Testing Companies (by Tester, Testing Company, Farm, date range, status), so that I can find any Test quickly.
65. As an NZMPTA Super-Administrator, I want to view any Machine Test in full, so that I have complete oversight across the platform.
66. As an NZMPTA Super-Administrator, I want to edit any field of any Machine Test, so that I can correct a Test on a Tester's behalf when needed.
67. As an NZMPTA Super-Administrator, I want every edit captured as a new Test Version with full before/after audit, so that no change can be made without an audit record.
68. As an NZMPTA Super-Administrator, I want an audit panel on every Machine Test showing the full version history with diffs, so that I can see who changed what and when.
69. As an NZMPTA Super-Administrator, I want the audit panel to indicate whether each checklist item was individually verified or bulk-confirmed via "Check all as verified", so that I can see how thoroughly the original Test was completed.
70. As an NZMPTA Super-Administrator, I want to soft-delete a completed Machine Test with a reason, so that erroneous Tests can be hidden from normal views without destroying the audit record.
71. As an NZMPTA Super-Administrator, I want every administrative login logged, so that there's an audit trail of who accessed the platform and when.
72. As an NZMPTA Super-Administrator, I want all administrative actions (User management, Reference Data changes, Test edits) logged with actor, timestamp and before/after state for seven years, so that any historical decision is traceable.
73. As an NZMPTA Super-Administrator, I want my Admin Portal Test edits to regenerate the Final Report using the same engine the Tester PWA uses, so that the Admin Portal and Tester PWA always produce identical Reports for the same Test data.

### NZMPTA Super-Administrator — Migration and cutover (O1)

74. As an NZMPTA Super-Administrator, I want all historical Machine Tests in the legacy database migrated to the new platform, so that years of test history are not lost at cutover.
75. As an NZMPTA Super-Administrator, I want all historical Vendor Specifications migrated, so that report regeneration of historical Tests produces the same pass/fail verdicts.
76. As an NZMPTA Super-Administrator, I want all legacy Testing Companies, Tester accounts and Farms migrated, so that the platform is populated from day one.
77. As an NZMPTA Super-Administrator, I want a data-quality report on every dry-run migration listing rows that failed validation and why, so that I can confirm which records will (or won't) survive cutover before go-live.
78. As an NZMPTA Super-Administrator, I want to run the migration multiple times in a staging environment, so that I can review sample Tests and confirm Reports match originals before authorising cutover.
79. As an NZMPTA Super-Administrator, I want all migrated Tester accounts to require a password reset on first login, so that legacy credentials cannot be used against the new platform.

### Cross-cutting (any User)

80. As any User, I want my password to meet complexity requirements and my account to lock after repeated failed logins, so that a brute-force attack is bounded.
81. As any User, I want the platform to use TLS 1.2+ throughout, so that my credentials and Test data can't be intercepted on the wire.
82. As any User, I want the PWA to remain installable from the latest two major versions of Chrome, Edge, Safari and Firefox (on iOS 16.4+, Android 10+, Windows 10+, macOS 13+), so that I'm not blocked by browser version constraints.
83. As any User, I want all data to remain in New Zealand (no cross-region replication outside NZ), so that data residency compliance is met.

## Implementation Decisions

### Architecture

- A single ASP.NET Core (.NET 10) application hosts both surfaces (Tester PWA and Admin Portal). Role-gating distinguishes which Razor Pages and API controllers each role can reach.
- The Tester PWA uses a service worker + IndexedDB for offline operation. The service worker caches the application shell; IndexedDB stores Reference Data, the Tester's in-progress and recently completed Machine Tests, cached Vendor Specifications, the current Test Standard Manual, and cached **Final Report** PDFs.
- The PWA Sync Client is written in TypeScript and is the only consumer of the API sync controllers — keeping the sync surface narrow and testable.
- **Reports are generated client-side in the PWA** so that Testers can produce **Final Reports** while offline. Library choice: **pdfmake** (declarative document → PDF) for generating each named Report, plus **pdf-lib** for merging the uploaded **Pulsation Data PDF** into the **Final Report**. Both are MIT-licensed, ~600 KB combined, work in iPad Safari, Android Chrome, and the desktop browser matrix. The Report's fonts are bundled into the PWA so generation is fully deterministic and self-contained on the Device.
- The same TypeScript **Report Generator** and **Final Report Composer** run in the **Admin Portal** browser, so Super-Administrator Test edits produce Reports identical to the Tester PWA. The server itself does not generate Reports. **The platform does not email Reports** — Testers download the **Final Report** PDF and send it to the Farmer via their own email client (using the browser/OS share sheet on iPad, or a regular email attachment on desktop). This removes the need for any server-side Report-rendering path entirely.
- Authentication uses ASP.NET Core Identity with email + password. Custom claims carry Role. A custom login pipeline extension enforces Tester Licence Expiry. Tokens are JWT-style Access Tokens (1h Tester / 2h Admin) + sliding Refresh Tokens (7d Tester / 1d Admin), with Refresh Tokens rotated on use and invalidated immediately on password change, explicit logout, account deactivation, or Super-Administrator force-logout.

### Ten deep modules (extracted and unit-tested in isolation)

1. **Pass/Fail Calculator** (.NET, also mirrored in TypeScript for the Wizard's live indicators) — pure function `(measurement, VendorSpecification) → PassFail verdict`. Stateless. When the Equipment Model is "Other", returns `NoStandardAvailable` and the UI/Report shows the reading without a verdict. The TypeScript mirror is generated from the .NET source or vice versa; a shared fixture-set ensures the two implementations agree.
2. **Wizard Step Resolver** (.NET, mirrored in TypeScript) — pure function `MachineConfiguration → ordered list of Wizard Steps with per-step subsection visibility`. The configuration-driven engine; tested table-driven against fixture configurations on both sides.
3. **Fault Aggregator** (.NET) — pure function `MachineTest → grouped FaultSummary` with per-Fault severity and source-section provenance, combining Visual Faults and numerical-test issues.
4. **Sync Reconciliation Engine** (.NET) — pure function `(incomingTestFromDevice, currentServerTest) → merge result + audit entries + conflict record`. Field-level merge: Tester-owned fields and admin-owned fields (Farm Details, final summary, Recommendations) live in separate field groups. Non-overlapping edits merge cleanly. Overlapping edits resolve last-writer-wins at field granularity, with both states preserved as Test Versions and flagged in the conflict record.
5. **Test Versioning Engine** (.NET) — given an existing Test, an edit and an actor, produces a new TestVersion row capturing the full prior state, plus audit entries with actor + timestamp + per-field before/after. The Authoritative Copy is always the latest version; prior versions are immutable.
6. **Reference Data Snapshot Service** (.NET) — given a date, returns the bundle of Vendor Specifications effective as of that date plus the current Test Standard Manual version. A Machine Test snapshots this bundle at Test start and persists the version-ids; later edits do not rebind.
7. **Audit Recorder** (.NET) — implemented as an EF Core `SaveChangesInterceptor`. Captures actor (from the current `ClaimsPrincipal`) + before/after JSON for every entity write within the same transaction scope as the write itself, so audit cannot be lost.
8. **Migration Tool** (.NET, standalone console) — reads the legacy Azure SQL database via a configured connection string, maps legacy tables to the new schema, writes good rows to the target, and quarantines bad rows to a separate output table with row-level error reasons. Idempotent dry-run mode (writes to staging) and cutover mode (writes to production, runs once).
9. **Report Generator** (TypeScript, runs in the browser on both Tester PWA and Admin Portal) — pure function `(MachineTest + ReferenceDataSnapshot + brandingAssets) → PDF bytes`. One named-Report template per Report type, each producing a section of the **Final Report**. Library: pdfmake. Tested in Vitest/Node.js with golden-file PDF fixtures.
10. **Final Report Composer** (TypeScript, runs in the browser) — pure function `(generatedReportPdf + uploadedPulsationDataPdf?) → mergedFinalReportPdf`. Library: pdf-lib. The uploaded Pulsation Data PDF is appended verbatim as the last pages. Tested with fixtures: known generated Report + known Pulsation PDF in, merged PDF with the expected page count and Pulsation pages at the tail out.

### Domain model highlights

- **MachineTest** stores `testStartedAt`, `markedCompleteAt`, `testStandardManualVersionId`, plus a collection of **VendorSpecificationSnapshot** rows (one per Equipment Model tested). Each snapshot stores `vendorSpecificationId + effectiveDate + snapshottedAt` so the exact thresholds the Tester saw on-device are reproducible years later.
- **VendorSpecification** carries an **EffectiveDate** column. The "currently effective" spec for an Equipment Model is the most-recent row with `effectiveDate <= now`. Super-Administrator scheduling a future spec change writes a row with a future `effectiveDate`.
- **TestStandardManual** has explicit version numbers, an upload timestamp, and an immutable PDF blob per version. Every prior version is retained.
- **TestVersion** rows store the full state of a completed MachineTest at the moment of each edit. The MachineTest table holds the current Authoritative Copy; TestVersion rows are immutable history.
- **MachineTest** has `isDeleted`, `deletedBy`, `deletedReason`, `deletedAt`, set only by Super-Administrator soft-delete. Deleted Tests are excluded from all default queries but retained for the 7-year audit window.
- **Tester** references **TesterLicence**; **TesterLicence** has **LicenceExpiryDate**. Login pipeline reads the current licence and refuses login if `licenceExpiryDate < today`.
- **Farm** is an entity in its own right (not embedded in MachineTest) so Farm Details edits propagate sensibly; MachineTest references Farm at the time of test creation.
- **Fault** carries `severity` (Critical / Major / Minor enum), a free-text description and an optional `recommendationText` that may be drawn from **StandardRecommendationWording** or freeform-entered by the Tester.
- **ChecklistAttestation** rows record each use of "Check all as verified" on a Machine Test: `wizardStepId + checkedAt + actor + attestationText`. Separate from the individual item-check state.
- **FinalReportBlob** holds the most-recent client-uploaded **Final Report** PDF per Machine Test (uploaded on sync), so the Admin Portal can display/download what the Tester actually printed without regenerating.

### "Check all as verified" UX (Wizard checklist steps)

- On any Wizard Step containing a list of checkbox items (notably Visual Faults — Pre-Start, Visual Faults — Running Inspection, and any Machine Configuration sub-checklists), a **"Check all as verified"** control appears at the bottom of the step.
- Activating it raises a modal confirmation requiring the Tester to attest explicit text along the lines of: *"I have inspected all items on this page and confirm they have been seen, tested and are in order."* (Exact wording finalised with NZMPTA.)
- On confirmation, every checkbox on the step is ticked. The Tester can still uncheck or amend individual items afterwards.
- The use of "Check all as verified" is recorded as a **ChecklistAttestation** audit event on the Machine Test (separate from the individual tick state). The Admin Portal's audit panel (user story 69) shows whether each item was individually verified or bulk-attested, so that the integrity of the Test record is preserved without removing the efficiency gain for routine Tests.

### Sync protocol

- The PWA holds a per-Test `syncState` in IndexedDB: `local-only` → `uploading` → `uploaded`, with `merge-conflict` as an alternate terminal state requiring Tester attention.
- The upload endpoint accepts a payload containing the client's last-known `serverVersionId` for each Test plus the generated **Final Report** PDF blob. The server delegates the merge to the **Sync Reconciliation Engine** and stores the PDF blob in **FinalReportBlob**.
- Reference data refresh is a separate endpoint that takes the client's `referenceDataAsOf` cursor and returns the delta: any new/updated Vendor Specifications with `effectiveDate <= now`, plus the current Test Standard Manual version if newer than what the client holds.
- Conflict surfacing: the rare merge-conflict case (overlapping Tester + Super-Administrator edits to the same field) is recorded in a `SyncConflict` row visible in the Admin Portal audit panel; the Tester is shown a non-blocking notice on the affected Test.

### Admin Portal surface

- **Tester management**: create, deactivate, reset password, force-logout, set/renew **Licence Expiry Date**, assign to **Testing Company**, view active sessions.
- **Testing Company management**: name, contact details, deactivation.
- **Equipment Catalogue management** per equipment type: add, edit, and deprecate **Equipment Models**. Deprecated models stay visible on historical Tests but don't appear in new Test dropdowns.
- **Vendor Specification editor**: per Equipment Model, with an **Effective Date** picker (defaults to today; can be set in the future).
- **Test Standard Manual upload**: new versions replace "current"; every prior version is retained and downloadable.
- **Standard Recommendation Wording**: simple list CRUD.

### O2 Admin Test browse & edit

- Super-Administrator only.
- List view with filter chips: Tester, Testing Company, Farm, date range, status, has-conflicts.
- Detail view renders the Test in the same Wizard-style layout the Tester saw, with every field editable.
- Each save → new Test Version + audit entries.
- Audit panel on the side of the detail view shows the version timeline with per-field diffs and ChecklistAttestation events.
- On save, the Admin Portal regenerates the **Final Report** using the same TypeScript **Report Generator** the Tester PWA uses, and re-merges the cached Pulsation Data PDF, so the Report stays in sync with the latest Test state.
- Soft-delete control with mandatory reason field.

### O3 Pulsation Data PDF upload

- File-drop zone on the summary Wizard Step accepts PDFs up to a configured size limit.
- Stored client-side in IndexedDB attached to the Machine Test; uploaded to Azure Storage on next sync.
- The **Final Report Composer** appends the Pulsation Data PDF as the last pages of the **Final Report**, on-device, immediately on Mark-as-Complete.

### Authentication details

- TOTP via authenticator app for Super-Administrator two-factor, with email-code fallback (open for NZMPTA confirmation).
- Password reset: email link with one-hour TTL.

### Cross-cutting

- All secrets in Azure Key Vault. No credentials in client code.
- Azure SQL with Private Endpoint. TLS 1.2+ everywhere.
- Azure Application Insights for monitoring; alerts to Pedersen Group.
- Geo-redundant backups within NZ; 35-day retention.

## Testing Decisions

### What makes a good test
Tests verify external behaviour, not implementation details. A test that breaks when an internal helper is renamed is a bad test. A test that breaks when the **Pass/Fail Calculator** starts returning the wrong verdict for a known measurement is a good test. The PRD commits to the aggressive test posture: ten deep modules unit-tested in isolation, the sync surface integration-tested end-to-end, Reports pinned via golden-file PDF tests, and the happy paths of the Wizard and Admin Portal driven by Playwright.

### Modules with explicit test coverage

1. **Pass/Fail Calculator** — table-driven unit tests covering every supported equipment type, boundary conditions on the pass/fail threshold, and the `NoStandardAvailable` case (Equipment Model = "Other"). Shared fixture-set drives both the .NET and TypeScript implementations to guarantee parity.
2. **Wizard Step Resolver** — table-driven unit tests on both implementations, covering: minimum-config Machines, fully-loaded Machines, every optional ancillary on/off, and pathological combinations (e.g. Herringbone with no ACRs and no milk meters).
3. **Fault Aggregator** — unit tests for: zero faults, single fault, faults from each section, faults of each severity, and grouping behaviour across sections.
4. **Sync Reconciliation Engine** — unit tests for: Tester-only edit (no admin change), Admin-only Farm Details edit (no Tester change), non-overlapping concurrent edits (merge cleanly), overlapping concurrent edits (last-writer-wins at field, both states preserved as Test Versions), and the conflict-record output.
5. **Test Versioning Engine** — unit tests verifying: every edit produces a new version, the prior version is immutable, audit entries carry actor + timestamp + per-field before/after, and the latest version is the Authoritative Copy.
6. **Reference Data Snapshot Service** — unit tests with date fixtures: spec effective in the past, spec effective in the future (excluded), spec effective today (included), multiple specs for one Equipment Model returning the most-recent-effective.
7. **Audit Recorder** — integration tests verifying audit rows are written in the same transaction as the entity write, contain the correct actor and per-field before/after, and that a failed write produces no audit row. Includes ChecklistAttestation events.
8. **Migration Tool** — golden-record tests: a known legacy database snapshot in, a known new-schema state + data-quality CSV out. Coverage for: clean rows migrate, malformed rows quarantine, idempotent re-runs produce identical output.
9. **Report Generator** — golden-file PDF tests under Vitest/Node.js, one per named Report (Test Summary, Test Report Results, Visual Faults Checklist, Test Record, Additional Testing, Individual Cluster Airflow Test, Pulsation System Result). Given a known Machine Test fixture, the generated PDF must match a checked-in expected PDF (normalised — strip timestamps, fonts embedded). Protects the pixel-equivalent contract against accidental regression on either surface (Tester PWA or Admin Portal).
10. **Final Report Composer** — fixture tests: known generated Report PDF + known Pulsation Data PDF in, asserting the merged Final Report's total page count, that the Pulsation pages appear at the tail, and that the generated Report pages are bit-identical to the input.
11. **Sync API** — integration tests round-tripping representative PWA payloads (including the Final Report blob) through the API, asserting on the persisted server state and the response. Covers the merge cases above end-to-end.
12. **Wizard UI (Playwright)** — smoke tests for two representative paths: a Rotary Machine with the full set of ancillaries, and a Herringbone Machine with minimal ancillaries. Each path: log in, create Machine Test, complete every visible Wizard Step (exercising "Check all as verified" on at least one Visual Faults step), upload a fixture Pulsation Data PDF, Mark-as-Complete, download Final Report, assert non-empty PDF and that the Pulsation pages are present at the tail.
13. **Admin Portal CRUD (Playwright)** — for each managed entity (Tester, Testing Company, Equipment Model, Vendor Specification, Test Standard Manual, Standard Recommendation Wording): create via the UI, edit, deactivate/deprecate, assert the change is reflected on next page load and audit entries are written. Plus an O2 path: edit a synced Machine Test as Super-Administrator and assert the regenerated Final Report differs from the original (by golden-file diff).

### Out of test scope

- Visual regression of the Tester PWA shell (UAT covers this).
- Offline behaviour of the PWA (service worker + IndexedDB lifecycle) — verified manually during UAT and pilot Tester rollout.
- Browser compatibility matrix — verified manually across the supported browsers.
- Hardware integration (none in scope).

### Prior art

Greenfield rebuild — no in-repo prior art. PDF golden-file pattern: render via pdfmake → normalise (strip generation timestamps, embed fonts) → compare bytes with checked-in expected PDF. Standard Vitest pattern for the TypeScript modules; `Microsoft.AspNetCore.Mvc.Testing` `WebApplicationFactory` for .NET integration tests; Playwright for browser-driven smoke tests.

## Out of Scope

- **M1 Foundation, data model & shared platform** — covered by the foundation SOW item; this PRD assumes Azure infrastructure (App Service, SQL, Key Vault, Private Endpoint, Application Insights), EF Core domain model, CI/CD pipeline, ASP.NET Core Identity scaffold, and audit infrastructure are all in place when this PRD's work begins.
- **M6 Hardening, UAT support, security review & go-live** — separate operational workstream; performance testing, security review, tester onboarding communications, parallel-run period and cutover are tracked there.
- **O4 Proactive notification schedule** — not in committed scope. The Next Test Date field is captured (M3) and the recipient model leaves room for Farmer association, but the configurable reminder system that emails Company Administrators / Farmers about upcoming Next Test Dates is deferred. Note: sending **Final Reports** to Farmers is not a platform feature in any phase — Testers handle that via their own email client from the downloaded PDF.
- **O5 Vendor self-service portal** — Phase 2.
- **Native mobile applications** — PWA covers iOS, Android, Windows and macOS from a single codebase.
- **Direct integration with milking machine hardware or test meters** — readings remain manually entered by the Tester.
- **Photo evidence on Visual Faults or Machine Tests** — explicitly deferred per the interview; can be added in a later phase if Testers request it.
- **Continued support of the legacy Windows desktop application beyond cutover** — retired at go-live.

## Further Notes

### Decisions made in interview, captured here for the record
- **Sync conflict resolution**: field-level merge. Common case (Tester edits Test data, Company Administrator edits Farm Details) merges cleanly because the editable field sets do not overlap. Super-Administrator overlapping edits fall back to last-writer-wins at field level, with both states preserved as Test Versions and a SyncConflict record visible in the Admin Portal.
- **Reference-data freshness**: snapshot at Test start. Vendor Specifications gain an **Effective Date** column so that future changes are scheduled rather than immediate, naturally avoiding the in-progress-Test edge case.
- **Test deletion**: Tester deletes their own in-progress Tests only; Super-Administrator soft-deletes completed Tests with a reason; no one hard-deletes.
- **Photo evidence**: out of scope for this PRD.
- **Report generation locus**: **client-side in the PWA**, not server-side. Reports must be produceable offline at the farm. The same TypeScript **Report Generator** + **Final Report Composer** modules run in both the **Tester PWA** and the **Admin Portal**, guaranteeing identical output for the same Test data. The server stores the most-recent client-uploaded **Final Report** PDF for retrieval but does not generate Reports itself.
- **"Check all as verified"**: standard pattern on every Wizard Step containing a list of checkbox items. Bulk-tick gated by a mandatory attestation prompt; recorded as a ChecklistAttestation audit event so historical Tests can show whether items were individually verified or bulk-confirmed.
- **Report email**: not a platform feature in any phase. The Tester downloads the **Final Report** PDF from the PWA and emails it to the Farmer via their own email client (browser/OS share sheet on iPad, regular attachment on desktop). This eliminates the need for any server-side Report-rendering path; the server's only role with Reports is to receive and store the client-generated PDF blob on sync, for Admin Portal display.

### Open items still needing NZMPTA input (from §14 of the requirements doc)
- **Access to the legacy Azure SQL database (read-only) and sample Tester accounts** — required for the **Migration Tool (O1)** to be specified precisely and for the new schema's Vendor Specification model to be validated against real data. Blocks O1 detailed design.
- **Brand assets (logo, colours, typography)** — required before the Admin Portal and Reports are finalised. The Report Generator consumes the brand assets as a bundled resource, so a brand swap before go-live is a single config change. Workaround: build against a placeholder brand.
- **Two-factor mechanism confirmation** — defaulting to TOTP via authenticator app with email-code fallback; confirm with NZMPTA.
- **Attestation wording for "Check all as verified"** — defaulting to *"I have inspected all items on this page and confirm they have been seen, tested and are in order."* — NZMPTA to confirm exact wording, which may need to differ between Visual Faults Pre-Start, Visual Faults Running, and Machine Configuration checklists.

### Cross-references
- `docs/NZMPTA_AutoRep_Rebuild_Requirements_v1_1.docx` — the upstream scope document this PRD specifies *how* to implement.
- `UBIQUITOUS_LANGUAGE.md` — domain glossary; every capitalised domain term in this PRD conforms to it.

### Indicative commercial framing (per requirements §13)
- Committed scope (M2 + M3 + M4 + M5 + O1 + O2 + O3): $43,750 ex GST.
- Out of this PRD: M1 Foundation ($7,500), M6 Hardening ($5,000), O4 Notifications ($2,500), O5 Vendor portal ($3,500).
- Ongoing operating cost: ~$200/month peak season, ~$100/month off-season (Azure infrastructure + transactional email).
