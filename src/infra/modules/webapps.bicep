param location string
param tags object

@description('Names of the three apps (globally unique; they become <name>.azurewebsites.net)')
param apiAppName string
param storeAppName string
param adminAppName string
param planName string
@description('App Service plan SKU shared by the three container apps, e.g. B1, P0v3')
param skuName string = 'B1'
@description('Instances in the plan. Use 2 or more for failover; every instance shares the token-signing keys through SQL.')
@minValue(1)
param instanceCount int = 1

param registryLoginServer string
@description('User-assigned identity of the API (SQL, Key Vault, Service Bus, image pulls)')
param apiIdentity object
@description('User-assigned identity of the two web front ends (image pulls only)')
param webIdentity object
@description('Tag of the store-api, store-web and admin-web images (the pipeline passes the commit SHA)')
param imageTag string

// ---- wiring from the other modules ----
@description('Name of the Key Vault secret holding the SQL connection string')
param sqlSecretName string
param serviceBusNamespace string
param keyVaultUri string
param redisSecretName string
param dataProtectionKeyUri string
param appInsightsConnectionString string

// ---- Microsoft Entra ID for the admin site ----
param tenantId string
@description('Client id of the app registration used by the admin site and as the API audience')
param adminClientId string

var apiUrl = 'https://${apiAppName}.azurewebsites.net'
var storeUrl = 'https://${storeAppName}.azurewebsites.net'
var adminUrl = 'https://${adminAppName}.azurewebsites.net'

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: skuName
    capacity: instanceCount
  }
  properties: { reserved: true }
}

// Settings every container app needs. WEBSITES_PORT must match the port the image listens on (8080 for all three).
var commonSettings = [
  { name: 'WEBSITES_PORT', value: '8080' }
  { name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE', value: 'false' }
]

resource api 'Microsoft.Web/sites@2024-04-01' = {
  name: apiAppName
  location: location
  tags: tags
  kind: 'app,linux,container'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${apiIdentity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    keyVaultReferenceIdentity: apiIdentity.id
    clientAffinityEnabled: false // stateless: no sticky sessions needed
    siteConfig: {
      linuxFxVersion: 'DOCKER|${registryLoginServer}/store-api:${imageTag}'
      acrUseManagedIdentityCreds: true
      acrUserManagedIdentityID: apiIdentity.clientId
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health/live'
      appSettings: concat(commonSettings, [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        // DefaultAzureCredential (Service Bus client, Key Vault data-protection key) picks this identity.
        { name: 'AZURE_CLIENT_ID', value: apiIdentity.clientId }
        // SQL authentication: the connection string (with password) is a Key Vault secret, read via Key Vault reference.
        { name: 'ConnectionStrings__StoreDb', value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/${sqlSecretName}/)' }
        { name: 'ConnectionStrings__Redis', value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/${redisSecretName}/)' }
        { name: 'ServiceBus__FullyQualifiedNamespace', value: '${serviceBusNamespace}.servicebus.windows.net' }
        { name: 'DataProtection__KeyVaultKeyUri', value: dataProtectionKeyUri }
        { name: 'AzureAd__TenantId', value: tenantId }
        { name: 'AzureAd__ClientId', value: adminClientId }
        { name: 'Cors__AllowedOrigins__0', value: storeUrl }
        { name: 'Cors__AllowedOrigins__1', value: adminUrl }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
      ])
    }
  }
}

resource store 'Microsoft.Web/sites@2024-04-01' = {
  name: storeAppName
  location: location
  tags: tags
  kind: 'app,linux,container'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${webIdentity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOCKER|${registryLoginServer}/store-web:${imageTag}'
      acrUseManagedIdentityCreds: true
      acrUserManagedIdentityID: webIdentity.clientId
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/healthz'
      appSettings: concat(commonSettings, [
        { name: 'API_BASE_URL', value: apiUrl }
      ])
    }
  }
}

resource admin 'Microsoft.Web/sites@2024-04-01' = {
  name: adminAppName
  location: location
  tags: tags
  kind: 'app,linux,container'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${webIdentity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOCKER|${registryLoginServer}/admin-web:${imageTag}'
      acrUseManagedIdentityCreds: true
      acrUserManagedIdentityID: webIdentity.clientId
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/healthz'
      appSettings: concat(commonSettings, [
        { name: 'API_BASE_URL', value: apiUrl }
        { name: 'AUTH_MODE', value: 'msal' }
        { name: 'TENANT_ID', value: tenantId }
        { name: 'CLIENT_ID', value: adminClientId }
        { name: 'API_SCOPE', value: 'api://${adminClientId}/access_as_admin' }
      ])
    }
  }
}

output apiUrl string = apiUrl
output storeUrl string = storeUrl
output adminUrl string = adminUrl
