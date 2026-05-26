# CI/CD setup — Infra + App workflows

One-time setup to wire the GitHub Actions workflows so they can deploy on your behalf.

**Auth model**: GitHub Actions exchanges an OIDC token for an Azure access token via a federated credential on an Entra ID App Registration. No long-lived client secrets stored in GitHub.

## Workflows at a glance

| File | When it runs | What it does |
|---|---|---|
| `.github/workflows/infra.yml` | PR touching `infra/**`, push to `main`, manual | Bicep build + `what-if` against staging. On main push, also deploys staging. |
| `.github/workflows/infra-prod.yml` | Manual only (`workflow_dispatch`) | `what-if` then deploy against prod, gated by the `prod` GitHub Environment (require reviewers). |
| `.github/workflows/app.yml` | PR touching `src/**`/`tests/**`, push to `main`, manual | `dotnet build` + `dotnet test` + `dotnet publish`. On main push, deploys to App Service `app-nzmpta-autorep-staging` and runs a health check against `/health`. |
| `.github/workflows/app-prod.yml` | Manual only (`workflow_dispatch`) | Builds from `main`, deploys to App Service `app-nzmpta-autorep-prod`, gated by the `prod` GitHub Environment. Requires typing "deploy to production" as a confirmation input. |

The same federated identity covers all four workflows — no additional setup needed if the infra one-time setup (below) is already done.

## One-time setup

You'll need: subscription Owner role + the ability to create Entra ID app registrations. ~10 minutes.

### 1. Create the App Registration + Service Principal

```powershell
$APP_NAME = "github-actions-nzmpta-autorep"

az ad app create --display-name $APP_NAME

$APP_ID = az ad app list --display-name $APP_NAME --query "[0].appId" -o tsv
az ad sp create --id $APP_ID

$SP_OBJECT_ID = az ad sp show --id $APP_ID --query id -o tsv

Write-Host "App ID (use for AZURE_CLIENT_ID):    $APP_ID"
Write-Host "Tenant ID (use for AZURE_TENANT_ID): $(az account show --query tenantId -o tsv)"
Write-Host "Sub ID (use for AZURE_SUBSCRIPTION_ID): $(az account show --query id -o tsv)"
```

Note the three values — they go into GitHub repo variables in step 4.

### 2. Add federated credentials (no secrets exchanged)

One per identity context the workflow runs under:

```powershell
$REPO = "jpedersen-pedgroup/nzmpta-autorep"

# PRs (any branch in the repo)
az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "github-pr",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:'$REPO':pull_request",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Pushes to main
az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:'$REPO':ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Staging environment (workflows that use `environment: staging`)
az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "github-env-staging",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:'$REPO':environment:staging",
  "audiences": ["api://AzureADTokenExchange"]
}'

# Prod environment
az ad app federated-credential create --id $APP_ID --parameters '{
  "name": "github-env-prod",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:'$REPO':environment:prod",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

If you see "ResourceQuotaExceeded" errors above, the app already has 20 credentials — list with `az ad app federated-credential list --id $APP_ID` and delete duplicates.

### 3. Grant Owner role on the resource groups

The Bicep creates RBAC role assignments (App Service MI → KV / Storage), which requires **Owner** (Contributor is insufficient because it can't grant roles).

```powershell
$SUB_ID = az account show --query id -o tsv

az role assignment create `
  --assignee $SP_OBJECT_ID `
  --role Owner `
  --scope "/subscriptions/$SUB_ID/resourceGroups/rg-nzmpta-autorep-staging"

az role assignment create `
  --assignee $SP_OBJECT_ID `
  --role Owner `
  --scope "/subscriptions/$SUB_ID/resourceGroups/rg-nzmpta-autorep-prod"
```

Owner is scoped to the RGs only — not the whole subscription.

### 4. Set GitHub repo variables

GitHub repo → **Settings → Secrets and variables → Actions → Variables tab → New repository variable**:

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | App ID from step 1 |
| `AZURE_TENANT_ID` | Tenant ID from step 1 |
| `AZURE_SUBSCRIPTION_ID` | Sub ID from step 1 |

(These are variables, not secrets — they're identifiers, not credentials.)

Or do it from CLI:

```powershell
gh variable set AZURE_CLIENT_ID --body $APP_ID
gh variable set AZURE_TENANT_ID --body (az account show --query tenantId -o tsv)
gh variable set AZURE_SUBSCRIPTION_ID --body $SUB_ID
```

### 5. Create the GitHub Environments + secrets

In GitHub repo → **Settings → Environments → New environment**:

- **staging** — no protection rules needed (auto-deploys on every main push).
- **prod** — add a **Required reviewer** (yourself) so prod deploys can't fire without approval.

For each environment, add one **secret**:

| Name | Value |
|---|---|
| `SQL_ADMIN_PASSWORD` | The SQL admin password for that env. **Must match** what you used at first deploy — different password rotates the SQL admin login. |

You can retrieve the staging password you already set from Key Vault (temporarily allow your IP through the KV firewall, then):

```powershell
az keyvault secret show --vault-name kv-nzmptaautorepstaging --name sql-admin-password --query value -o tsv
```

Then in CLI:

```powershell
gh secret set SQL_ADMIN_PASSWORD --env staging --body "<password>"
gh secret set SQL_ADMIN_PASSWORD --env prod    --body "<prod password — generate fresh if prod not yet deployed>"
```

## Verification

After all setup, push a trivial change under `infra/` (or trigger manually):

```powershell
gh workflow run "Infra (validate + staging deploy)"
gh run watch
```

Should see: `validate` job runs successfully and `deploy-staging` is skipped (not a push to main).

To test the actual deploy: merge a PR with an infra change (e.g. add a tag), watch the `deploy-staging` job complete, then check Azure for the change.

## Common failures

- **`AADSTS70021: No matching federated identity record found`** — the federated credential's `subject` doesn't match the workflow context. The most common cause is forgetting to add the per-environment credential. Re-read step 2.
- **`AuthorizationFailed: ... does not have authorization to perform action 'Microsoft.Authorization/roleAssignments/write'`** — the SP only has Contributor, needs Owner. Step 3.
- **`Required environment 'prod' could not be found`** — create the prod environment in GitHub repo settings. Step 5.
- **Workflow runs but `what-if` says "no changes"** — that's success! Means staging matches your local infra.

## Future: app build/deploy

When app code lands (Phase 1 dev work), add a `.github/workflows/app.yml` that builds the .NET solution and publishes to the App Service. Use the **same** federated identity — just add a `workload-identity-federation` claim role to the App Service if needed and grant the SP the `Website Contributor` role on the App Service resource.
