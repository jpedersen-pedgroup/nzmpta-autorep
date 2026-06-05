using '../main.bicep'

param environment = 'prod'

// Region. NZ data residency is a CONTRACTUAL requirement (all data stays in NZ); staging already runs here.
// PRD §Hosting requires NZ data residency for prod; this is the deciding parameter.
param location = 'newzealandnorth'

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
