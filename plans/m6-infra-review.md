# NZMPTA AutoRep — M6 Infrastructure & Security Review

> **When:** during **M6 (Hardening, security review & go-live)**, before production cutover and before the O1 data migration loads real data.
> **Owner:** Pedersen Group. **Sign-off:** required before Go-Live.
> **Scope:** the Azure footprint (`infra/`), application security, and operational readiness. Functional testing lives in `plans/test-schedule.md`; this is the non-functional / hardening pass.
> Legend: ✅ confirmed · 🟡 partial · ⬜ to do at M6 · ⚠️ risk/decision

## Carried-forward findings (from the 5 Jun 2026 early check)
- ✅ **Data residency** — prod region corrected `australiaeast` → `newzealandnorth` (`infra/parameters/prod.bicepparam`). **Verify the deployed prod is actually in NZ North; if it was ever deployed in AU, redeploy and delete the AU resources before migration.**
- ✅ SQL + Storage backups are LRS/in-region (no cross-Tasman leak).
- ⬜ Proposal §11 says "geo-redundant backup within NZ" — reality is LRS (in-region, residency-safe). Reconcile the wording (Requirements v1.2) or accept LRS as the residency-correct choice.

## 1. Data residency & compliance
- [ ] ✅ Prod App Service, SQL, Storage, Key Vault, Log Analytics all in **newzealandnorth** (confirm each resource, not just the param)
- [ ] No geo-replication / failover group pointing outside NZ (Azure's NZ North pair is AU East — keep backups LRS/zone, not Geo)
- [ ] App Insights / Log Analytics data stored in NZ; retention configured (no export to non-NZ region)
- [ ] Confirm no third-party services (email, monitoring) move PII outside NZ, or document/accept where they do

## 2. Network & ingress
- [ ] ⬜ **WAF** — add Azure Front Door Standard + WAF in front of App Service (deferred from Phase 1; SKU includes WAF). Enable managed rule sets
- [ ] ⬜ Geo-filter ingress to NZ-only (or justify open ingress for roaming testers)
- [ ] ✅ Backend `publicNetworkAccess = Disabled` on SQL, Key Vault, Storage (verify still true post-any-change)
- [ ] ✅ Private Endpoints live for SQL, KV, Blob; Private DNS zones resolving from the VNet
- [ ] App Service HTTPS-only + HSTS enabled; HTTP→HTTPS redirect
- [ ] Storage `networkAcls.bypass = AzureServices` reviewed; confirm no unintended public paths
- [ ] ⚠️ SQL `restrictOutboundNetworkAccess` is `Disabled` — consider enabling with allowed FQDNs (optional hardening)

## 3. Identity, secrets & access
- [ ] ✅ App authenticates to SQL/Storage/KV via **System-Assigned Managed Identity** (no connection-string secrets)
- [ ] ✅ No credentials in source — re-scan `appsettings*.json` and client code for secrets; all secrets via Key Vault references
- [ ] ⬜ **Remove the personal Azure AD admin from the SQL server** after the one-time MI-user setup (README post-deploy step 2)
- [ ] SQL break-glass admin password stored in Key Vault; rotation plan documented
- [ ] Key Vault RBAC least-privilege (only the App MI + named admins); soft-delete + purge protection ON
- [ ] App Service RBAC grants reviewed (least privilege to KV/Storage/SQL)
- [ ] Secret/key rotation policy agreed (JWT signing key, SQL admin, any API keys)

## 4. Application & transport security
- [ ] ✅ TLS 1.2+ enforced on App Service, SQL, Storage (in IaC — verify at runtime)
- [ ] Security headers on the web app: HSTS, X-Content-Type-Options, Referrer-Policy, and a **CSP compatible with the PWA service worker**
- [ ] CORS on `/api/*` locked to the app origin
- [ ] Auth hardening: account lockout active; password complexity; **2FA enforcement for admins (every 30 days / new device)** — confirm built & on (also a build item)
- [ ] Token policy verified in prod config: 1h access / 7d refresh (tester), 2h / 1d (admin); rotation + revoke-on-logout/force-logout
- [ ] Dependency vulnerability scan (NuGet + npm) clean; no high/critical CVEs
- [ ] ⚠️ Decide scope of "security review": internal checklist only, or external penetration test (contract M6 says "security review")

## 5. Data protection & backups
- [ ] ✅ Azure SQL PITR / automated backups retained **35 days**; ✅ Storage soft-delete + versioning (35 days)
- [ ] ⬜ **Perform a test restore** of the SQL DB (prove backups are recoverable, not just configured)
- [ ] Encryption at rest confirmed (SQL TDE on; Storage SSE on)
- [ ] Soft-delete behaviour for Final Report / Pulsation blobs verified
- [ ] ⬜ **7-year audit retention** (proposal §11) — `AuditEntry` rows are written, but confirm the long-term retention/archival strategy (DB growth vs archive to storage)

## 6. Monitoring, alerting & audit
- [ ] ✅ App Insights wired; diagnostic settings → Log Analytics on SQL & Storage
- [ ] ⬜ **Alert rules** configured and routed to Pedersen Group: availability, 5xx rate, response time, SQL DTU/vCore, failed logins spike, storage errors
- [ ] Availability/uptime test against the health endpoint (used by the deploy workflow) feeding the 99.98% measurement
- [ ] Log Analytics retention set (cost vs compliance); admin login + admin actions captured
- [ ] Microsoft Defender for Cloud / Defender for SQL (threat detection, vulnerability assessment) — enable or consciously skip

## 7. Resilience & performance
- [ ] ⬜ **Load test 20–30 concurrent tester/admin sessions** + sync bursts within targets (proposal §11 concurrency; contract M6 performance testing)
- [ ] ⚠️ SQL `zoneRedundant: false` — confirm whether NZ North supports zones; enable for prod resilience if available
- [ ] Prod SQL `autoPause` disabled (no cold start) — confirm; staging auto-pause acceptable
- [ ] App Service Plan (P0v3) right-sized; autoscale rules considered for peak season
- [ ] ⬜ Confirm prod SKUs (P0v3 plan, `GP_S_Gen5_4` SQL) are **available in NZ North** at deploy time
- [ ] Documented uptime-measurement method + any service-credit position (see MSA clause 5)

## 8. Cost & governance
- [ ] ⬜ Replace `CostCentre: 'TODO'` in `prod.bicepparam` tags (and confirm staging tags)
- [ ] Cost review vs the ~$100–200/month operating estimate; set an Azure **budget + cost alert**
- [ ] Off-season scale-down path tested (SQL tier down → ~$100/mo) with no redeploy
- [ ] Resource locks on prod RG (prevent accidental deletion)

## 9. Custom domain, DNS & certificates
- [ ] ⬜ Bind prod custom domain (e.g. `autorep.nzmpta.org.nz`) + DNS validation (README post-deploy step 3)
- [ ] App Service Managed Certificate issued + SNI-bound; auto-renew confirmed
- [ ] PWA `start_url`/`scope` and manifest match the final domain; service worker re-validated on the real host

## 10. Operational readiness & cutover
- [ ] ⬜ **Cutover runbook**: pre-checks, migration run command, post-checks, **rollback procedure** (also an O1 deliverable)
- [ ] Parallel-run period agreed with NZMPTA; legacy decommission plan + date
- [ ] DNS cutover steps + TTL lowered ahead of switch
- [ ] On-call / incident channel live (ties to MSA SLA); contacts confirmed
- [ ] Tester onboarding comms + docs ready (install PWA, first-login password reset)

## Sign-off
- [ ] All ⬜/⚠️ items resolved or risk-accepted in writing
- [ ] Security review outcome documented
- [ ] Pedersen Group + NZMPTA (Maria Scott) go-live approval recorded
