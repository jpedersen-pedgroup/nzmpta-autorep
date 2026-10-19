using '../main.bicep'

param environment = 'staging'

// Region. Change to 'newzealandnorth' once you confirm GA availability in your subscription.
// `az account list-locations --query "[?metadata.regionCategory=='Recommended'].name" -o tsv | grep -i zealand`
param location = 'newzealandnorth'

// Staging was built before NZMPTA became MPNZ and keeps its nzmpta resource names and URL for now.
// Changing this would deploy a second, empty set of resources, not rename the existing ones.
param namingPrefix = 'nzmpta-autorep'

param tags = {
  Project: 'MPNZ AutoRep'
  Environment: 'staging'
  Owner: 'Pedersen Group'
  ManagedBy: 'Bicep'
}

param sqlAdminLogin = 'autorep-sqladmin'

// Provided at deploy time via env var. See infra/README.md.
param sqlAdminPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD')

// Password reset and farm review emails. The app's managed identity holds Graph Mail.Send in the
// Pedersen Group tenant. Interim: moves to noreply@milkingpumping.co.nz once MPNZ's tenant is set up.
param graphSendingMailbox = 'noreply@pedersengroup.co.nz'
