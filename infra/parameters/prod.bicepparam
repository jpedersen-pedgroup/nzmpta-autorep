using '../main.bicep'

param environment = 'prod'

// Region. Change to 'newzealandnorth' once you confirm GA availability in your subscription.
// PRD §Hosting requires NZ data residency for prod; this is the deciding parameter.
param location = 'australiaeast'

param namingPrefix = 'nzmpta-autorep'

param tags = {
  Project: 'NZMPTA AutoRep'
  Environment: 'prod'
  Owner: 'Pedersen Group'
  ManagedBy: 'Bicep'
  CostCentre: 'TODO' // <-- fill in your cost centre tag if your org requires one
}

param sqlAdminLogin = 'autorep-sqladmin'

// Provided at deploy time via env var. See infra/README.md.
param sqlAdminPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD')
