@description('Globally unique, 3-24 lowercase letters and digits')
param name string
param location string
param tags object

// Host storage for the Function App. Shared-key access is off: the function identity gets data-plane roles (rbac.bicep).
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

// Where Flex Consumption keeps the deployed code package.
resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'deploymentpackage'
}

output name string = storage.name
output deploymentContainerUrl string = '${storage.properties.primaryEndpoints.blob}${deploymentContainer.name}'
