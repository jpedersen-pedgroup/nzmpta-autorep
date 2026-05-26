// Azure SQL Server + Database (Serverless General Purpose), private-endpoint only.
// Staging: 0.5–1 vCore auto-pausing after 60 min idle.
// Prod: 1–4 vCore, no auto-pause (so first morning request isn't a cold start).

@description('Azure region')
param location string

@description('Resource base name')
param resourceBase string

@allowed(['staging', 'prod'])
param environment string

@description('Tags')
param tags object

@description('Subnet ID hosting the Private Endpoint NIC')
param privateEndpointSubnetId string

@description('Private DNS Zone ID for SQL')
param privateDnsZoneId string

@description('Log Analytics workspace ID for diagnostic settings')
param logAnalyticsWorkspaceId string

@description('SQL administrator login (used only for break-glass)')
param sqlAdminLogin string

@secure()
@description('SQL administrator password')
param sqlAdminPassword string

var sqlServerName = 'sql-${resourceBase}'
var sqlDatabaseName = 'sqldb-${resourceBase}'

var dbConfig = {
  staging: {
    skuName: 'GP_S_Gen5_1'
    skuTier: 'GeneralPurpose'
    skuFamily: 'Gen5'
    skuCapacity: 1
    maxSizeBytes: 34359738368 // 32 GB
    autoPauseDelay: 60
    minCapacity: '0.5'
    zoneRedundant: false
  }
  prod: {
    skuName: 'GP_S_Gen5_4'
    skuTier: 'GeneralPurpose'
    skuFamily: 'Gen5'
    skuCapacity: 4
    maxSizeBytes: 107374182400 // 100 GB
    autoPauseDelay: -1 // disabled
    minCapacity: '1.0'
    zoneRedundant: false // single-AZ in some regions; flip on in regions that support it
  }
}

resource sqlServer 'Microsoft.Sql/servers@2024-05-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    publicNetworkAccess: 'Disabled'
    version: '12.0'
    minimalTlsVersion: '1.2'
    restrictOutboundNetworkAccess: 'Disabled'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2024-05-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: tags
  sku: {
    name: dbConfig[environment].skuName
    tier: dbConfig[environment].skuTier
    family: dbConfig[environment].skuFamily
    capacity: dbConfig[environment].skuCapacity
  }
  properties: {
    maxSizeBytes: dbConfig[environment].maxSizeBytes
    autoPauseDelay: dbConfig[environment].autoPauseDelay
    minCapacity: json(dbConfig[environment].minCapacity)
    zoneRedundant: dbConfig[environment].zoneRedundant
    requestedBackupStorageRedundancy: 'Local'
  }
}

resource pe 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: 'pe-sql-${resourceBase}'
  location: location
  tags: tags
  properties: {
    subnet: { id: privateEndpointSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'pe-sql'
        properties: {
          privateLinkServiceId: sqlServer.id
          groupIds: ['sqlServer']
        }
      }
    ]
  }
}

resource peDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: pe
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'sql'
        properties: { privateDnsZoneId: privateDnsZoneId }
      }
    ]
  }
}

resource diag 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: sqlDb
  name: 'send-to-log-analytics'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      { category: 'SQLInsights', enabled: true }
      { category: 'AutomaticTuning', enabled: true }
      { category: 'QueryStoreRuntimeStatistics', enabled: true }
      { category: 'QueryStoreWaitStatistics', enabled: true }
      { category: 'Errors', enabled: true }
      { category: 'DatabaseWaitStatistics', enabled: true }
      { category: 'Timeouts', enabled: true }
      { category: 'Blocks', enabled: true }
      { category: 'Deadlocks', enabled: true }
    ]
    metrics: [{ category: 'AllMetrics', enabled: true }]
  }
}

output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDb.name
