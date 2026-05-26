# Ubiquitous Language

Source: [NZMPTA AutoRep Rebuild Requirements v1.1](../../../docs/NZMPTA_AutoRep_Rebuild_Requirements_v1_1.docx) (26 May 2026).

## Organisations and people

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **NZMPTA** | New Zealand Milking and Pumping Trade Association — owner of the platform and governing body for testers. | NZ MPTA, the association |
| **Pedersen Group** | Builder and operator of the platform on NZMPTA's behalf. | the vendor, the developer |
| **Testing Company** | An organisation that employs one or more **Testers**. Owns a view of its own testers' work but does not administer the platform. | company (when unqualified), firm, employer |
| **Equipment Vendor** | A manufacturer of milking-machine equipment whose **Equipment Models** appear in the **Equipment Catalogue**. | vendor (unqualified — collides with prior software vendor), supplier, manufacturer |
| **Farmer** | The owner of a **Farm** under test. Optionally a recipient of upcoming-test reminders. | farm owner, client, customer |
| **Farm** | A dairy operation at a specific location, against which **Machine Tests** are performed. | site, property |

## Roles

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Tester** | A registered individual licensed by NZMPTA to perform **Machine Tests**. Roughly 150 in total. Edits only their own **Tests**. | inspector, technician, field user |
| **Company Administrator** | Read-mostly role scoped to one **Testing Company**: views that company's **Tests** and **Testers** only. May edit Farm Details and final summary / recommendations on those **Tests**. | company admin (when ambiguous), company manager |
| **NZMPTA Super-Administrator** | Single platform-wide administrator role: manages **Users**, **Reference Data** and the **Test Standard Manual**, and views all **Tests**. | super-admin, NZMPTA admin, administrator (unqualified), the admin |

## Test lifecycle

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Machine Test** | The unit of work: a single inspection of one **Machine** at one **Farm** by one **Tester** on one occasion. | test (when ambiguous with sub-tests), inspection, job |
| **Wizard** | The configuration-driven sequence of capture steps that replaces the legacy section-by-section form. | form, flow, questionnaire |
| **Wizard Step** | One numbered step of the **Wizard** (Test Setup, Machine Configuration, … Upload & Reports). | page, section (collides with report sections) |
| **Machine Configuration** | The Wizard Step 2 answers (machine type, pumps, pulsator, claw, liner, etc.) that gate which downstream **Wizard Steps** are shown. | setup, profile |
| **Mark as Complete** | The Tester action that finalises a **Machine Test**, enabling **Report** generation and queuing the test for **Sync**. | submit, finish, close |
| **Test Version** | A snapshot of a completed **Machine Test** preserved when the **Test** is edited post-completion, for audit traceability. | revision, edit, copy |
| **Next Test** | Workflow that creates a new **Machine Test** pre-populated from a prior one for the same **Farm** / **Machine**. | repeat test, copy test |
| **Next Test Date** | The recommended date for the next **Machine Test** at this **Farm**, set by the **Tester** at completion (default twelve months). | due date, follow-up date |
| **Authoritative Copy** | The server-side record of an uploaded **Machine Test**, considered the source of truth after a successful **Sync**. | master copy, source, canonical record |

## Equipment

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Machine** | The milking machine under test. Typed as **Rotary** or **Herringbone**. | system, plant |
| **Equipment Model** | A specific make/model of a piece of equipment (e.g. a particular pulsator model). | product, item |
| **Equipment Catalogue** | The managed master list of **Equipment Models** for a given equipment type (Vacuum Pumps, Pulsators, Liners, Shells, Claws, Jetters, ACRs, Milk Meters, Milk Flow Indicators, Regulators, Releaser Pumps). | catalog, lookup, dropdown |
| **Vendor Specification** | The numerical pass/fail standards published for an **Equipment Model**, used during a **Machine Test** to compute live pass/fail indicators. | spec, equipment standard, vendor data |
| **"Other"** | The standard catch-all option on every **Equipment Catalogue** dropdown, allowing the **Tester** to record a model that isn't catalogued. | freeform, custom, manual entry |

Equipment terms-of-art used in the doc and assumed glossary-stable: **ACR** (Automatic Cluster Remover), **BMCC** (Bulk Milk Cell Count), **VSD** (Variable Speed Drive), **Claw**, **Liner**, **Shell**, **Milkline**, **Pulsator**, **Vacuum Pump**, **Releaser Pump**, **Regulator**, **Receiver**, **Jetter**, **Cluster**, **Pulse Tube**, **Bail Area**.

## Faults and recommendations

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Fault** | A defect identified during a **Machine Test**. Includes both **Visual Faults** and issues surfaced by numerical tests. | issue, defect, problem, finding |
| **Visual Fault** | A **Fault** captured in the pre-start or running visual-inspection **Wizard Steps**. A subtype of **Fault**. | visual issue, observation |
| **Fault Severity** | The per-**Fault** classification — one of **Critical**, **Major**, or **Minor**. Replaces the legacy group-level severity. | priority, rating, level |
| **Recommendation** | Tester-entered remediation text attached to a specific **Fault**. | comment, action, note |
| **Standard Recommendation Wording** | Curated drop-down suggestions for **Recommendations**, overridable by the **Tester**. | recommendation template, canned text |
| **Compliance Disclaimer** | Mandatory legal text appearing on the **Test Summary** of every generated **Report**. | safety note, legal text |

## Reports

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Report** | A PDF artefact generated from a completed **Machine Test**. Used in the plural for the seven-document set. | document, output |
| **Test Summary** | The headline single-page **Report** carrying fault summary, severities, **Next Test Date** and **Compliance Disclaimer**. | summary report, cover sheet |
| **Pulsation Data PDF** | An externally-produced pulsation chart that the **Tester** uploads via the summary **Wizard Step**. | pulsator graph PDF, pulsation graph, pulse chart |
| **Final Report** | The combined PDF produced by appending the **Pulsation Data PDF** to the generated **Reports**. | combined PDF, full report, output PDF |
| **Test Standard Manual** | The versioned NZMPTA-published PDF rulebook that **Testers** must work against. Cached on every **Device**. | manual, standards doc, rulebook |

Named reports in the seven-document set: **Test Summary**, **Test Report Results**, **Visual Faults Checklist**, **Test Record** (report — see ambiguity below), **Additional Testing**, **Individual Cluster Airflow Test**, **Pulsation System Result**.

## Reference data

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **Reference Data** | The managed lookups (companies, **Equipment Catalogues**, **Vendor Specifications**, **Standard Recommendation Wording**, **Test Standard Manual**) cached on **Devices** for offline use. | lookups, master data, seed data |

## Identity and access

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **User** | An authentication identity in the system. Every **Tester**, **Company Administrator** and **NZMPTA Super-Administrator** is a **User**; the distinction is the **Role**. | account (overloaded), login |
| **Role** | The capability set assigned to a **User**: **Tester**, **Company Administrator**, or **NZMPTA Super-Administrator**. | permission, group |
| **Tester Licence** | The active NZMPTA registration period for a **Tester**, expressed as a **Licence Expiry Date**. Renewed by updating the date. | certification, registration |
| **Licence Expiry Date** | The date past which a **Tester** can no longer log in until renewed. Enforced at the next authentication boundary, not instantly. | expiry, end date |
| **Access Token** | Short-lived bearer token (one hour for **Testers**, two hours for administrators) authorising calls to the **Sync** endpoints. | session token (ambiguous), JWT |
| **Refresh Token** | Sliding-window token (seven days for **Testers**, one day for administrators) used to mint new **Access Tokens** without re-authentication. | session refresh, long token |
| **Force-Logout** | Administrator action that invalidates a **User**'s tokens immediately. | kick, revoke session |

## Sync and devices

| Term | Definition | Aliases to avoid |
|------|-----------|-----------------|
| **PWA** | Progressive Web App — the installable, offline-capable web client used by **Testers**. | tester app, mobile app, AutoRep app |
| **Admin Portal** | The online-only NZMPTA administration surface — distinct in capability and audit posture from the **PWA**, though delivered by the same web application. | admin site, back office |
| **Device** | A specific installation of the **PWA** on one **Tester**'s machine (Windows / macOS / iPad / Android). | client, install |
| **Sync** | The bidirectional exchange between **Device** and server: upload of completed **Machine Tests**, download of updated **Reference Data** and the **Test Standard Manual**. | upload, refresh |
| **Sync Lifecycle** | The defined sequence: login while online → reference download → offline work → reconnect → upload → reference refresh. | sync flow |
| **Cutover** | The migration event at which the legacy desktop application is decommissioned and the rebuild becomes the system of record. | go-live (related but distinct), launch |
| **Go-Live** | The point at which the new platform is in production use by **Testers**. The **Cutover** is the event that produces **Go-Live**. | launch, release |

## Relationships

- A **Tester** belongs to exactly one **Testing Company**.
- A **Company Administrator** can view **Machine Tests** only for **Testers** in their own **Testing Company**.
- A **NZMPTA Super-Administrator** can view all **Machine Tests** across all **Testing Companies**.
- A **Machine Test** is created by exactly one **Tester**, on exactly one **Farm**, against exactly one **Machine**.
- A **Machine Test** captures zero or more **Faults**; each **Fault** has exactly one **Fault Severity** and zero or one **Recommendation**.
- A completed **Machine Test** produces a set of **Reports** combined into one **Final Report** when the **Pulsation Data PDF** is uploaded.
- A post-completion edit of a **Machine Test** produces a new **Test Version**; prior versions are retained for audit.
- An **Equipment Model** belongs to one **Equipment Catalogue** and has exactly one current **Vendor Specification**.
- **Reference Data** lives authoritatively on the server and is cached on every **Device** to enable offline pass/fail calculation.
- A **Tester Licence** belongs to one **Tester**; the **Licence Expiry Date** gates login but not in-progress offline work.

## Example dialogue

> **Dev:** "When a **Tester** marks a **Machine Test** as complete offline, what's actually committed?"
> **Domain expert:** "Nothing leaves the **Device** until **Sync**. The **Test** is sealed locally, the **Reports** can be generated and printed, but until the upload succeeds there is no **Authoritative Copy** on the server."
>
> **Dev:** "And if the **Tester** opens that same **Test** the next morning and corrects a **Fault Severity** before syncing?"
> **Domain expert:** "When it uploads, the server creates a **Test Version** — both states are retained. The latest version becomes the **Authoritative Copy**, but the prior one is still in the audit trail."
>
> **Dev:** "What if a **Pulsator** model isn't in the **Equipment Catalogue**?"
> **Domain expert:** "The **Tester** picks **Other** and types the model name. Their per-Test calculations fall back to defaults because there's no **Vendor Specification** to compare against. Afterwards the **NZMPTA Super-Administrator** can add the model with a proper **Vendor Specification** from the **Equipment Vendor**, and the next **Sync** ships it to every **Device**."
>
> **Dev:** "And if the **Tester**'s **Licence Expiry Date** passes while they're on-farm?"
> **Domain expert:** "They can finish and **Sync** the **Test** they're already inside, because the licence check runs at login — not on each action. But once their **Refresh Token** runs out or they log out, they're locked out until **NZMPTA** renews the **Tester Licence**."
>
> **Dev:** "Who sees the **Test** once it's uploaded?"
> **Domain expert:** "The **Tester** sees their own. Their **Company Administrator** sees all **Tests** by **Testers** in the same **Testing Company**, but can only edit Farm Details and the final summary. The **NZMPTA Super-Administrator** sees everything across all **Testing Companies**."

## Flagged ambiguities

- **"vendor"** is used for two distinct things: the **previous software vendor** (whose codebase NZMPTA is leaving) and an **Equipment Vendor** (manufacturer of catalogued equipment). Always qualify — prefer **Equipment Vendor** for the manufacturer and avoid the bare word for the software supplier.
- **"company"** appears unqualified in the requirements doc but always refers to a **Testing Company**. Standardise on **Testing Company** in code, schemas, UI labels and conversation; reserve "company" for prose where context is already locked.
- **"administrator" / "admin"** is overloaded across **Company Administrator** and **NZMPTA Super-Administrator** — two roles with very different scopes. Never use the bare word; always pick the role.
- **"Test Record"** has two meanings in the doc: one of the seven named **Reports** (the legacy "Test Record" form), and a generic synonym for a **Machine Test** entity (e.g. "historical test records"). For the entity, prefer **Machine Test**; reserve **Test Record** for the named report only.
- **"Test"** is used freely as shorthand for **Machine Test**, but is also nested inside sub-test names (**Vacuum & Reserve Tests**, **Pulsator Tests**, **Individual Cluster Tests**). Where the surrounding context could resolve to either, write **Machine Test** for the entity and the full sub-test name for the subordinate test.
- **"Pulsator graph PDF"** (used in scope item O3) and **"Pulsation Data PDF"** (used in Section 9.2) refer to the same artefact. Standardise on **Pulsation Data PDF**.
- **"User"** vs **"Tester"**: every **Tester** is a **User**, but not every **User** is a **Tester** — administrators are also **Users**. Use **User** when speaking about authentication / identity, **Tester** when speaking about the role that performs tests.
- **"Standard"** is overloaded: the **Test Standard Manual** is the published rulebook, the **Vendor Specifications** are numerical equipment standards, and **"NZ Safety Standard"** in the compliance disclaimer is external legislation. Always qualify.
- **"Sign-off"**: Wizard Step 11 is **Review & Sign-Off** (a **Tester** action on a single **Machine Test**), distinct from **UAT sign-off** by NZMPTA on the build delivery. Different actors, different scopes — qualify which is meant.
- **"Session token policy"** in §5.1.1 covers both the **Access Token** and the **Refresh Token**. The bare term **session token** is ambiguous; in code use the specific name.
- **"Report"** singular sometimes means one named PDF (the **Test Summary**) and sometimes means the combined **Final Report**. Prefer the specific name when accuracy matters.
