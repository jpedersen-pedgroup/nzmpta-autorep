// Key Vault with public network access disabled and a Private Endpoint.
// RBAC authorization model (not the legacy access-policies model).
// Soft delete + purge protection enabled — required by many enterprise policies and not reversible.

@description('Azure region')
param location string

@description('Resource base name')
param resourceBase string

@description('Tags')
param tags object

@description('Subnet ID hosting the Private Endpoint NIC')
param privateEndpointSubnetId string

@description('Private DNS Zone ID for Key Vault')
param privateDnsZoneId string

@description('Log Analytics workspace ID for diagnostic settings')
param logAnalyticsWorkspaceId string

// KV names: 3–24 chars, alphanumeric + hyphens, globally unique.
// Strip hyphens from base and prefix with "kv-" — caller may need to tweak if conflict.
var keyVaultName = 'kv-${take(replace(resourceBase, '-', ''), 21)}'

resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

resource pe 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: 'pe-kv-${resourceBase}'
  location: location
  tags: tags
  properties: {
    subnet: { id: privateEndpointSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'pe-kv'
        properties: {
          privateLinkServiceId: keyVault.id
          groupIds: ['vault']
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
        name: 'keyVault'
        properties: { privateDnsZoneId: privateDnsZoneId }
      }
    ]
  }
}

resource diag 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: keyVault
  name: 'send-to-log-analytics'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      { categoryGroup: 'audit', enabled: true }
      { categoryGroup: 'allLogs', enabled: true }
    ]
    metrics: [{ category: 'AllMetrics', enabled: true }]
  }
}

output keyVaultName string = keyVault.name
output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
