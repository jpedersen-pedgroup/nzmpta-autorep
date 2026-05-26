// =============================================================================
// NZMPTA AutoRep — Core Azure Infrastructure
// =============================================================================
//
// Orchestrates: VNet + subnets, Private DNS Zones, Log Analytics + App Insights,
// Key Vault, Azure SQL, Storage, App Service (with VNet integration + System MI
// + public ingress + managed TLS).
//
// Deploy with:
//   az deployment group create \
//     --resource-group <rg-name> \
//     --template-file infra/main.bicep \
//     --parameters infra/parameters/<env>.bicepparam
//
// See infra/README.md for full prerequisites and post-deploy steps.
// =============================================================================

targetScope = 'resourceGroup'

@description('Environment name: staging or prod')
@allowed(['staging', 'prod'])
param environment string

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Naming prefix for resources. Final names = {abbrev}-{namingPrefix}-{environment}.')
param namingPrefix string = 'nzmpta-autorep'

@description('Tags applied to every resource')
param tags object

@description('SQL administrator login. Used only for break-glass; app uses Managed Identity.')
param sqlAdminLogin string = 'autorep-sqladmin'

@secure()
@description('SQL administrator password. Provide via env var at deploy time; store in Key Vault after deploy.')
param sqlAdminPassword string

var resourceBase = '${namingPrefix}-${environment}'

// -----------------------------------------------------------------------------
// Network: VNet + subnets
// -----------------------------------------------------------------------------
module network 'modules/network.bicep' = {
  name: 'network'
  params: {
    location: location
    resourceBase: resourceBase
    tags: tags
  }
}

// -----------------------------------------------------------------------------
// Monitoring: Log Analytics + App Insights
// -----------------------------------------------------------------------------
module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    resourceBase: resourceBase
    tags: tags
  }
}

// -----------------------------------------------------------------------------
// Private DNS Zones for the Private Endpoints
// -----------------------------------------------------------------------------
module privateDns 'modules/private-dns.bicep' = {
  name: 'privateDns'
  params: {
    vnetId: network.outputs.vnetId
    tags: tags
  }
}

// -----------------------------------------------------------------------------
// Key Vault (with Private Endpoint, RBAC mode)
// -----------------------------------------------------------------------------
module keyVault 'modules/key-vault.bicep' = {
  name: 'keyVault'
  params: {
    location: location
    resourceBase: resourceBase
    tags: tags
    privateEndpointSubnetId: network.outputs.privateEndpointsSubnetId
    privateDnsZoneId: privateDns.outputs.keyVaultZoneId
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
  }
}

// -----------------------------------------------------------------------------
// Azure SQL (with Private Endpoint, Serverless GP)
// -----------------------------------------------------------------------------
module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    resourceBase: resourceBase
    environment: environment
    tags: tags
    privateEndpointSubnetId: network.outputs.privateEndpointsSubnetId
    privateDnsZoneId: privateDns.outputs.sqlZoneId
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
  }
}

// -----------------------------------------------------------------------------
// Storage (blob containers for Final Reports + Pulsation PDFs)
// -----------------------------------------------------------------------------
module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    resourceBase: resourceBase
    tags: tags
    privateEndpointSubnetId: network.outputs.privateEndpointsSubnetId
    privateDnsZoneId: privateDns.outputs.blobZoneId
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
  }
}

// -----------------------------------------------------------------------------
// App Service (with VNet integration + System Managed Identity)
// -----------------------------------------------------------------------------
module appService 'modules/app-service.bicep' = {
  name: 'appService'
  params: {
    location: location
    resourceBase: resourceBase
    environment: environment
    tags: tags
    appServiceIntegrationSubnetId: network.outputs.appServiceIntegrationSubnetId
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsWorkspaceId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    storageAccountName: storage.outputs.storageAccountName
    sqlServerFqdn: sql.outputs.sqlServerFqdn
    sqlDatabaseName: sql.outputs.sqlDatabaseName
  }
}

// -----------------------------------------------------------------------------
// Outputs
// -----------------------------------------------------------------------------
output appServiceDefaultHostName string = appService.outputs.defaultHostName
output appServicePrincipalId string = appService.outputs.appServicePrincipalId
output keyVaultName string = keyVault.outputs.keyVaultName
output storageAccountName string = storage.outputs.storageAccountName
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output sqlDatabaseName string = sql.outputs.sqlDatabaseName
