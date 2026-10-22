@description('Globally unique, 5-50 lowercase letters and digits')
param name string
param location string
param tags object

// Apps pull images with their managed identities (AcrPull, assigned in rbac.bicep), so the admin account stays off.
resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: name
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

output name string = registry.name
output loginServer string = registry.properties.loginServer
