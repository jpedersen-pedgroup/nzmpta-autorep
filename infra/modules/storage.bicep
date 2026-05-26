// Storage Account (StorageV2, Standard_LRS) with public access disabled and a Private Endpoint for blob.
// Shared-key access disabled — only Managed Identity / Azure AD auth.
// Two containers seeded: final-reports (Final Report PDFs) and pulsation-data (uploaded Pulsation PDFs).

@description('Azure region')
param location string

@description('Resource base name')
param resourceBase string

@description('Tags')
param tags object

@description('Subnet ID hosting the Private Endpoint NIC')
param privateEndpointSubnetId string

@description('Private DNS Zone ID for Blob')
param privateDnsZoneId string

@description('Log Analytics workspace ID for diagnostic settings')
param logAnalyticsWorkspaceId string

// Storage account names: 3–24 chars, lowercase alphanumeric only, globally unique.
var storageAccountName = take('st${replace(resourceBase, '-', '')}', 24)

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    publicNetworkAccess: 'Disabled'
    supportsHttpsTrafficOnly: true
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
    encryption: {
      services: {
        blob: { enabled: true }
      }
      keySource: 'Microsoft.Storage'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 35 }
    containerDeleteRetentionPolicy: { enabled: true, days: 35 }
    changeFeed: { enabled: true }
    isVersioningEnabled: true
  }
}

resource finalReportsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'final-reports'
  properties: { publicAccess: 'None' }
}

resource pulsationDataContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'pulsation-data'
  properties: { publicAccess: 'None' }
}

resource pe 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: 'pe-st-${resourceBase}'
  location: location
  tags: tags
  properties: {
    subnet: { id: privateEndpointSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'pe-st-blob'
        properties: {
          privateLinkServiceId: storage.id
          groupIds: ['blob']
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
        name: 'blob'
        properties: { privateDnsZoneId: privateDnsZoneId }
      }
    ]
  }
}

resource diag 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: blobService
  name: 'send-to-log-analytics'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      { category: 'StorageRead', enabled: true }
      { category: 'StorageWrite', enabled: true }
      { category: 'StorageDelete', enabled: true }
    ]
    metrics: [{ category: 'Transaction', enabled: true }]
  }
}

output storageAccountName string = storage.name
output storageAccountId string = storage.id
