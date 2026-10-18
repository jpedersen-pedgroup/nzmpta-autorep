using '../main.bicep'

param environment = 'staging'

// Region. Change to 'newzealandnorth' once you confirm GA availability in your subscription.
// `az account list-locations --query "[?metadata.regionCategory=='Recommended'].name" -o tsv | grep -i zealand`
param location = 'newzealandnorth'

param namingPrefix = 'nzmpta-autorep'

param tags = {
  Project: 'NZMPTA AutoRep'
  Environment: 'staging'
  Owner: 'Pedersen Group'
  ManagedBy: 'Bicep'
}

param sqlAdminLogin = 'autorep-sqladmin'

// Provided at deploy time via env var. See infra/README.md.
param sqlAdminPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD')

// Password reset and farm review emails. The app's managed identity holds Graph Mail.Send in the
// Pedersen Group tenant. Interim: moves to noreply@milkpumping.co.nz once MPNZ's tenant is set up.
param graphSendingMailbox = 'noreply@pedersengroup.co.nz'
