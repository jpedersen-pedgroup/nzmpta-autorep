// VNet with subnets for App Service VNet-integration (outbound) and Private Endpoints (inbound NICs).
// Address space: 10.10.0.0/16 — change per env if peering with other VNets.

@description('Azure region')
param location string

@description('Resource base name (e.g. nzmpta-autorep-prod)')
param resourceBase string

@description('Tags')
param tags object

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-${resourceBase}'
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: ['10.10.0.0/16']
    }
    subnets: [
      {
        name: 'snet-appsvc-integration'
        properties: {
          addressPrefix: '10.10.1.0/24'
          delegations: [
            {
              name: 'app-service-delegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
          privateEndpointNetworkPolicies: 'Enabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
      }
      {
        name: 'snet-private-endpoints'
        properties: {
          addressPrefix: '10.10.2.0/24'
          // PE policies must be disabled on the subnet hosting PE NICs
          privateEndpointNetworkPolicies: 'Disabled'
          privateLinkServiceNetworkPolicies: 'Enabled'
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output appServiceIntegrationSubnetId string = '${vnet.id}/subnets/snet-appsvc-integration'
output privateEndpointsSubnetId string = '${vnet.id}/subnets/snet-private-endpoints'
