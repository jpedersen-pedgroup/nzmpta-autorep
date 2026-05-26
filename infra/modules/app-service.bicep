// Linux App Service Plan + Web App with VNet integration + System-Assigned Managed Identity.
// MI is granted Key Vault Secrets User on the KV and Storage Blob Data Contributor on the storage account.
// SQL access via Managed Identity needs a post-deploy SQL command (see infra/README.md).
//
// Tier choice:
//   staging — B1 Basic (~$13/mo, supports VNet integration; no autoscale or deployment slots, fine for non-prod)
//   prod    — P0v3 Premium (~$130/mo, modern v3 plan, autoscale headroom, deployment slots, daily backups)

@description('Azure region')
param location string

@description('Resource base name')
param resourceBase string

@allowed(['staging', 'prod'])
param environment string

@description('Tags')
param tags object

@description('Subnet ID for App Service VNet integration (outbound)')
param appServiceIntegrationSubnetId string

@description('Log Analytics workspace ID')
param logAnalyticsWorkspaceId string

@description('App Insights connection string')
param appInsightsConnectionString string

@description('Key Vault name (existing) for MI grant')
param keyVaultName string

@description('Storage account name (existing) for MI grant')
param storageAccountName string

@description('SQL server FQDN')
param sqlServerFqdn string

@description('SQL database name')
param sqlDatabaseName string

var planConfig = {
  staging: {
    skuName: 'B1'
    skuTier: 'Basic'
    skuCapacity: 1
  }
  prod: {
    skuName: 'P0v3'
    skuTier: 'Premium0V3'
    skuCapacity: 1
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-${resourceBase}'
  location: location
  tags: tags
  sku: {
    name: planConfig[environment].skuName
    tier: planConfig[environment].skuTier
    capacity: planConfig[environment].skuCapacity
  }
  kind: 'linux'
  properties: {
    reserved: true // linux
    zoneRedundant: false
  }
}

resource appService 'Microsoft.Web/sites@2024-04-01' = {
  name: 'app-${resourceBase}'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    virtualNetworkSubnetId: appServiceIntegrationSubnetId
    publicNetworkAccess: 'Enabled'
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|9.0' // bump to 10.0 once App Service catches up
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      vnetRouteAllEnabled: true
      use32BitWorkerProcess: false
      healthCheckPath: '/health'
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environment == 'prod' ? 'Production' : 'Staging'
        }
        {
          name: 'AzureKeyVault__VaultUri'
          value: 'https://${keyVaultName}${az.environment().suffixes.keyvaultDns}/'
        }
        {
          name: 'AzureStorage__AccountName'
          value: storageAccountName
        }
        {
          name: 'AzureStorage__FinalReportsContainer'
          value: 'final-reports'
        }
        {
          name: 'AzureStorage__PulsationDataContainer'
          value: 'pulsation-data'
        }
        {
          name: 'ConnectionStrings__SqlDatabase'
          value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
        }
      ]
    }
  }
}

// -----------------------------------------------------------------------------
// RBAC: App Service MI → Key Vault Secrets User
// -----------------------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
}

resource kvSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, appService.id, 'KeyVaultSecretsUser')
  properties: {
    principalId: appService.identity.principalId
    principalType: 'ServicePrincipal'
    // Key Vault Secrets User
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

// -----------------------------------------------------------------------------
// RBAC: App Service MI → Storage Blob Data Contributor (account-scoped)
// -----------------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: storageAccountName
}

resource blobDataContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, appService.id, 'StorageBlobDataContributor')
  properties: {
    principalId: appService.identity.principalId
    principalType: 'ServicePrincipal'
    // Storage Blob Data Contributor
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}

// -----------------------------------------------------------------------------
// Diagnostics
// -----------------------------------------------------------------------------
resource diag 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: appService
  name: 'send-to-log-analytics'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      { category: 'AppServiceHTTPLogs', enabled: true }
      { category: 'AppServiceConsoleLogs', enabled: true }
      { category: 'AppServiceAppLogs', enabled: true }
      { category: 'AppServiceAuditLogs', enabled: true }
      { category: 'AppServiceIPSecAuditLogs', enabled: true }
      { category: 'AppServicePlatformLogs', enabled: true }
    ]
    metrics: [{ category: 'AllMetrics', enabled: true }]
  }
}

output appServiceId string = appService.id
output appServiceName string = appService.name
output defaultHostName string = appService.properties.defaultHostName
output appServicePrincipalId string = appService.identity.principalId
