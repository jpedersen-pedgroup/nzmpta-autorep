# Infra — NZMPTA AutoRep Core Azure Infrastructure

Bicep IaC for the AutoRep platform's Azure footprint. Two environments: **staging** and **prod**. Each gets its own resource group, VNet, App Service (public ingress with managed TLS, custom domain via post-deploy), SQL DB, Key Vault, Storage, Log Analytics and App Insights. Front Door and WAF are deferred to M6 hardening — App Service alone covers everything needed for a single-region NZ-only deployment.

This is the infrastructure layer for **Issue [#2](https://github.com/jpedersen-pedgroup/nzmpta-autorep/issues/2)** (Phase 1: Walking skeleton).

## Topology

```
       Public Internet
              │
              ▼
       [ App Service ]   ← public ingress, HTTPS-only, managed TLS cert, custom domain (post-deploy)
        (System MI)        ← System-Assigned Managed Identity
              │ VNet integration (outbound)
              ▼
   ┌────────── VNet (10.10.0.0/16) ──────────┐
   │  snet-appsvc-integration  (10.10.1.0/24) │
   │  snet-private-endpoints    (10.10.2.0/24) │
   │                                            │
   │   Private Endpoints:                       │
   │     • Azure SQL   (privatelink.database.windows.net) │
   │     • Key Vault   (privatelink.vaultcore.azure.net)  │
   │     • Storage     (privatelink.blob.*)               │
   └────────────────────────────────────────────┘

   Diagnostics → Log Analytics workspace → App Insights (workspace-based)
```

Backend services have `publicNetworkAccess = Disabled` and are only reachable from the VNet via Private Endpoints. App Service has public ingress (required — Testers reach it from the internet); defense-in-depth (WAF, rate limiting, geo-filtering) is deferred to M6 hardening, at which point a Front Door + WAF can be added in front.

## Files

| Path | Purpose |
|------|---------|
| `main.bicep` | Orchestrator. Targets `resourceGroup` scope. |
| `modules/network.bicep` | VNet + subnets. |
| `modules/private-dns.bicep` | Private DNS zones for KV, SQL, Blob; VNet-linked. |
| `modules/monitoring.bicep` | Log Analytics + App Insights. |
| `modules/key-vault.bicep` | Key Vault (RBAC mode, soft-delete + purge protection) + PE. |
| `modules/sql.bicep` | Azure SQL Server + DB (Serverless GP) + PE. |
| `modules/storage.bicep` | Storage Account + blob containers + PE. |
| `modules/app-service.bicep` | App Service Plan + Web App + VNet integration + System MI + RBAC grants. |
| `parameters/staging.bicepparam` | Staging parameters. |
| `parameters/prod.bicepparam` | Prod parameters. |

## Prerequisites

- **Azure CLI** 2.60+ with the `bicep` extension (`az bicep upgrade`).
- **Subscription** with Owner role (RBAC role assignments in the Bicep require this; Contributor is insufficient).
- **Resource provider registrations** in the subscription:
  - `Microsoft.Web`, `Microsoft.Sql`, `Microsoft.KeyVault`, `Microsoft.Storage`,
  - `Microsoft.Network`, `Microsoft.Insights`, `Microsoft.OperationalInsights`.
  - Register with: `az provider register --namespace <provider>`.
- A **resource group** per environment, pre-created (Bicep targets the RG scope):
  ```bash
  az group create --name rg-nzmpta-autorep-staging --location australiaeast
  az group create --name rg-nzmpta-autorep-prod    --location australiaeast
  ```
- (For custom domain post-deploy) DNS control over the domain you intend to use, so you can add the validation TXT and target CNAME records.

## Deploy

### Generate the SQL admin password

The SQL admin login is used only for break-glass (the app authenticates via Managed Identity). Generate a strong random password once per env and stash it:

```bash
export SQL_ADMIN_PASSWORD="$(openssl rand -base64 32)"
```

Keep this somewhere secure — you'll store it in Key Vault after deploy and shouldn't need it again unless something breaks.

### Deploy staging

```bash
az deployment group create \
  --resource-group rg-nzmpta-autorep-staging \
  --template-file infra/main.bicep \
  --parameters infra/parameters/staging.bicepparam
```

### Deploy prod

```bash
az deployment group create \
  --resource-group rg-nzmpta-autorep-prod \
  --template-file infra/main.bicep \
  --parameters infra/parameters/prod.bicepparam
```

Expect ~10–15 min for the first deploy (SQL Server + Front Door are the slow ones).

### Capture outputs

The deployment outputs the resource names you'll need for post-deploy steps:

```bash
az deployment group show \
  --resource-group rg-nzmpta-autorep-staging \
  --name main \
  --query properties.outputs
```

## Post-deploy steps

These are intentionally outside Bicep because they create circular dependencies or require interactive/runtime decisions.

### 1. Store the SQL admin password in Key Vault

```bash
KV_NAME="$(az deployment group show -g rg-nzmpta-autorep-staging -n main --query properties.outputs.keyVaultName.value -o tsv)"
az keyvault secret set --vault-name "$KV_NAME" --name sql-admin-password --value "$SQL_ADMIN_PASSWORD"
```

You may need to temporarily allow your laptop IP on the Key Vault firewall first (`az keyvault network-rule add --vault-name "$KV_NAME" --ip-address $(curl -s ifconfig.me)`); revert after.

### 2. Set up the App Service Managed Identity as a SQL user

App Service's Managed Identity needs to be a SQL user with appropriate role. This is a one-time per-env step run from any machine with SQL connectivity (you'll need VPN/Bastion since SQL is private):

```sql
-- Connected to sqldb-nzmpta-autorep-{env} as the Azure AD admin (set below first)
CREATE USER [app-nzmpta-autorep-staging] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [app-nzmpta-autorep-staging];
```

Before that works you need an Azure AD admin on the SQL server. Set yourself temporarily:

```bash
az sql server ad-admin create \
  --resource-group rg-nzmpta-autorep-staging \
  --server-name sql-nzmpta-autorep-staging \
  --display-name "$(az ad signed-in-user show --query userPrincipalName -o tsv)" \
  --object-id "$(az ad signed-in-user show --query id -o tsv)"
```

Then connect via `sqlcmd` or Azure Data Studio over VPN/Bastion, run the SQL above, and consider revoking your AAD admin role afterwards (production hygiene).

### 3. (Prod, when ready) Add the custom domain to App Service

When you've got the prod domain decided (e.g. `autorep.nzmpta.org.nz`):

```bash
APP_NAME="app-nzmpta-autorep-prod"
RG="rg-nzmpta-autorep-prod"
DOMAIN="autorep.nzmpta.org.nz"

# 1. Add the verification TXT record on your DNS:
#      asuid.${DOMAIN}  TXT  <value from `az webapp config hostname get-external-ip`>
#    Or follow the portal's "Add custom domain" flow which prints the exact TXT value.

# 2. Add the CNAME on your DNS:
#      ${DOMAIN}  CNAME  ${APP_NAME}.azurewebsites.net

# 3. Add the hostname binding once DNS is in place:
az webapp config hostname add \
  --resource-group "$RG" \
  --webapp-name "$APP_NAME" \
  --hostname "$DOMAIN"

# 4. Create the free App Service Managed Certificate for it:
az webapp config ssl create \
  --resource-group "$RG" \
  --name "$APP_NAME" \
  --hostname "$DOMAIN"

# 5. Bind the cert to the hostname (SNI):
THUMB="$(az webapp config ssl list --resource-group "$RG" --query "[?subjectName=='$DOMAIN'].thumbprint" -o tsv)"
az webapp config ssl bind \
  --resource-group "$RG" \
  --name "$APP_NAME" \
  --certificate-thumbprint "$THUMB" \
  --ssl-type SNI
```

The Managed Certificate auto-renews. Repeat the flow for staging if you give it a custom domain too.

## Costs (rough monthly estimate, both envs running)

| Service | Staging | Prod | Notes |
|---|---|---|---|
| App Service Plan | ~$13 (B1) | ~$130 (P0v3) | Always-on; can scale up/out later. App Service Managed Cert is free. Staging B1 has no autoscale or deployment slots — fine for non-prod. |
| Azure SQL | ~$15 (Serverless 0.5–1 vCore, auto-pause) | ~$200 (Serverless 1–4 vCore, no pause) | Prod cost dominated by always-on min capacity. |
| Storage | ~$2 | ~$5 | Pay-per-use; Final Report PDFs only. |
| Key Vault | ~$1 | ~$1 | Pay-per-transaction. |
| Log Analytics + App Insights | ~$10 | ~$30 | Volume-driven; tune retention if cost grows. |
| Private Endpoints (3 ea.) | ~$22 | ~$22 | $7.30/PE-month. |
| Private DNS Zones (3) | shared | shared | ~$3 total across both envs. |
| **Total** | **~$50/mo** | **~$390/mo** | **Combined: ~$440/mo** |

That's still higher than the PRD's $200/mo peak-season figure — the prod SQL with always-on min capacity is the main driver. Levers:
- Drop prod SQL to auto-pause (cuts ~$150/mo, but adds cold-start lag on the first morning request).
- Shut down staging outside business hours (separate Bicep + automation).
- Bump prod App Service Plan if traffic warrants — P0v3 has headroom for now.

## Open caveats

- **Region**: parameter files default to `australiaeast`. PRD §Hosting calls for NZ data residency; switch to `newzealandnorth` once you confirm it's GA in your subscription. (`az account list-locations -o table` to verify.)
- **App Service public ingress is fully open by default**. Defense-in-depth (WAF, rate limiting, geo-filtering to NZ-only) is deferred to M6 hardening. When you're ready, add Azure Front Door Standard in front and enable its built-in WAF (Standard tier includes WAF at no SKU upgrade). The App Service itself stays unchanged.
- **Backend services unreachable from your laptop**: SQL, KV, Storage have `publicNetworkAccess = Disabled`. For ad-hoc admin you'll need to either (a) temporarily enable public access with an IP allowlist, (b) deploy Azure Bastion in the VNet, or (c) use a point-to-site VPN.
- **Azure AD admin on SQL**: post-deploy step 2 requires manual setup once per env. Consider scripting via deployment script resource (`Microsoft.Resources/deploymentScripts`) in a future iteration.
- **Tags**: `CostCentre` is a placeholder in both parameter files — fill in your org's required value before deploy.
- **Custom domain** is handled post-deploy via `az webapp` commands (step 3), not in Bicep, because validation requires DNS to be in place first.

## Refs

- PRD §Architecture, §Sync protocol, §Authentication & authorization, §Hosting & cross-cutting.
- Plan §Architectural decisions (`plans/autorep-rebuild.md`).
- Phase 1 issue: [#2](https://github.com/jpedersen-pedgroup/nzmpta-autorep/issues/2).
