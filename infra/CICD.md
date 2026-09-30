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

One per GitHub Environment. Every job that logs in to Azure declares `environment: staging` or `environment: prod`, which makes its token's subject `repo:<repo>:environment:<name>` — so these two are all Azure needs to trust. Don't add `pull_request` or `ref:refs/heads/main` credentials; see [Who can get an Azure token](#who-can-get-an-azure-token).

```powershell
$REPO = "jpedersen-pedgroup/nzmpta-autorep"

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

Owner is scoped to the RGs only — not the whole subscription. Better still, give prod its own identity so nothing that can reach staging can reach prod: [Recommended hardening](#recommended-hardening), item 2.

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
- **prod** — add a **Required reviewer** (yourself) so prod deploys can't fire without approval, and under **Deployment branches and tags** pick **Selected branches and tags** → `main`, so only `main`'s copy of a workflow can deploy to prod.

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

## Who can get an Azure token

GitHub only issues an OIDC token to a job with `id-token: write`, and Azure only accepts one whose `subject` matches a federated credential. Both are kept narrow:

- **Permissions.** Each workflow is read-only at the top (`permissions: contents: read`), and only the jobs that run `azure/login` opt back in with their own `permissions` block. Build, test and guard jobs — and the npm/NuGet code they execute — can't mint a token. A new job that needs Azure needs that block *and* an `environment:`.
- **Subjects.** Every Azure job runs in a GitHub Environment, so its token's subject is `repo:<repo>:environment:staging` or `…:environment:prod`, and those are the only credentials step 2 creates. There's deliberately no `pull_request` or `ref:refs/heads/main` credential: no Azure job presents those subjects, so all they would do is let jobs *outside* an environment in.

The permissions keep compromised dependencies out, not people with write access: a same-repo PR or branch push runs its own copy of the workflow, so its author can ask for any permission and name any environment. (PRs from forks can't request a token or read secrets.) What limits them is which environments a run can enter and what the identity behind each one can touch — hence the steps below.

### Recommended hardening

1. **Delete the PR and `main` credentials** if you created them from an earlier version of step 2. Nothing uses them.

   ```powershell
   az ad app federated-credential delete --id $APP_ID --federated-credential-id github-pr
   az ad app federated-credential delete --id $APP_ID --federated-credential-id github-main
   ```

2. **Give prod its own identity.** Step 3 makes one service principal Owner on both resource groups, and `staging` can't be limited to `main` because `infra.yml` runs PR what-ifs in it — so anyone who can push a branch can get an `environment:staging` token and, through it, Owner on the prod resource group. (Staging's `SQL_ADMIN_PASSWORD` is exposed the same way — don't reuse it for prod.) Move prod to a second App Registration that trusts only the `prod` environment. With the variables from steps 1–3 still set:

   ```powershell
   $PROD_APP_NAME = "github-actions-nzmpta-autorep-prod"
   az ad app create --display-name $PROD_APP_NAME
   $PROD_APP_ID = az ad app list --display-name $PROD_APP_NAME --query "[0].appId" -o tsv
   az ad sp create --id $PROD_APP_ID
   $PROD_SP_OBJECT_ID = az ad sp show --id $PROD_APP_ID --query id -o tsv
   ```

   Add the `github-env-prod` credential from step 2 to it (same command with `--id $PROD_APP_ID`), then:

   ```powershell
   az role assignment create `
     --assignee $PROD_SP_OBJECT_ID `
     --role Owner `
     --scope "/subscriptions/$SUB_ID/resourceGroups/rg-nzmpta-autorep-prod"

   # An environment variable overrides the repo-level one, so prod jobs switch
   # identity without any workflow change.
   gh variable set AZURE_CLIENT_ID --env prod --body $PROD_APP_ID

   # Leave the original identity staging-only.
   az ad app federated-credential delete --id $APP_ID --federated-credential-id github-env-prod
   az role assignment delete `
     --assignee $SP_OBJECT_ID `
     --role Owner `
     --scope "/subscriptions/$SUB_ID/resourceGroups/rg-nzmpta-autorep-prod"
   ```

3. **Lock down `prod` and `main`.** Give the `prod` environment required reviewers *and* limit its deployment branches to `main` (step 5). The branch rule matters because a run dispatched from another branch uses that branch's copy of the workflow — `app-prod.yml`'s `ref: main` checkout doesn't help if the workflow itself was edited. The rule is only as strong as `main`, so protect `main` too (require a pull request before merging).

## Common failures

- **`AADSTS70021: No matching federated identity record found`** — the federated credential's `subject` doesn't match the workflow context. The most common cause is forgetting to add the per-environment credential. Re-read step 2. A job that logs in to Azure without an `environment:` fails the same way — by design there's no PR or `main` credential, so give the job an environment.
- **`Unable to get ACTIONS_ID_TOKEN_REQUEST_URL env variable`** (from `azure/login`) — the job has no `id-token: write`. Workflows are read-only by default; give the job its own `permissions` block with `id-token: write` and `contents: read`.
- **`AuthorizationFailed: ... does not have authorization to perform action 'Microsoft.Authorization/roleAssignments/write'`** — the SP only has Contributor, needs Owner. Step 3.
- **`Required environment 'prod' could not be found`** — create the prod environment in GitHub repo settings. Step 5.
- **Workflow runs but `what-if` says "no changes"** — that's success! Means staging matches your local infra.

## Future: app build/deploy

When app code lands (Phase 1 dev work), add a `.github/workflows/app.yml` that builds the .NET solution and publishes to the App Service. Use the **same** federated identity — just add a `workload-identity-federation` claim role to the App Service if needed and grant the SP the `Website Contributor` role on the App Service resource.
