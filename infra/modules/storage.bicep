// Storage Account (StorageV2, Standard_LRS) with public access disabled and a Private Endpoint for blob.
// Shared-key access disabled — only Managed Identity / Azure AD auth.
// Two containers seeded: final-reports (Final Report PDFs) and pulsation-data (uploaded Pulsation PDFs),
// both immutable (version-level WORM) for pdfRetentionDays — ten years — per blob version.

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

// Ten years (Josh, 8 Oct 2026 — beyond the PRD's seven-year audit window): 10 × 365 + 3, the most
// leap days any ten years can hold. Every version of a Final Report or a pulsation analyser PDF is
// immutable (WORM) for this long after it is written: it can't be deleted, and an overwrite keeps
// the old version. The policy is UNLOCKED — an Owner can still shorten or remove it — until NZMPTA
// confirms the period; locking it is a separate, irreversible step.
@description('Days each version of a PDF in final-reports and pulsation-data stays immutable (WORM).')
@minValue(1)
@maxValue(146000)
param pdfRetentionDays int = 3653

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

// Version-level immutability (WORM) on both PDF containers, with a default time-based retention
// policy that every new blob version inherits (pdfRetentionDays, unlocked). What it means for the app
// (Services/Pdfs/BlobPdfStore.cs): a put of new bytes over an existing key — a Final Report sent again
// with different bytes — still succeeds and keeps the previous version; a delete fails (the app never
// deletes); pulsation keys are content-addressed, so they are never overwritten at all.
//
// ONE-WAY: once a container supports version-level immutability it can't be turned off, and the
// storage account can't be deleted while such a container holds blobs. A NEW container gets it at
// creation (prod). An EXISTING container (staging) must be migrated before this deploys, or the
// deployment fails — see the PR that added this for the two commands per container.
resource finalReportsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'final-reports'
  properties: {
    publicAccess: 'None'
    immutableStorageWithVersioning: { enabled: true }
  }
}

resource finalReportsRetention 'Microsoft.Storage/storageAccounts/blobServices/containers/immutabilityPolicies@2024-01-01' = {
  parent: finalReportsContainer
  name: 'default'
  properties: {
    immutabilityPeriodSinceCreationInDays: pdfRetentionDays
    allowProtectedAppendWrites: false
  }
}

resource pulsationDataContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'pulsation-data'
  properties: {
    publicAccess: 'None'
    immutableStorageWithVersioning: { enabled: true }
  }
}

resource pulsationDataRetention 'Microsoft.Storage/storageAccounts/blobServices/containers/immutabilityPolicies@2024-01-01' = {
  parent: pulsationDataContainer
  name: 'default'
  properties: {
    immutabilityPeriodSinceCreationInDays: pdfRetentionDays
    allowProtectedAppendWrites: false
  }
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
