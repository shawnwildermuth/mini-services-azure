// Parts Store: everything in one resource group, single region, single environment.
//
//   az group create -n rg-partsstore-prod -l <region>
//   az deployment group create -g rg-partsstore-prod -f main.bicep -p main.example.bicepparam
//
// See ../docs/deployment.md for the one-time setup and how the CI/CD pipeline uses this.

targetScope = 'resourceGroup'

@description('Short environment name used in resource names, e.g. prod')
@minLength(2)
@maxLength(8)
param environmentName string = 'prod'

param location string = resourceGroup().location

@description('Tag of the container images to run. The pipeline passes the commit SHA; "latest" suits a manual first deployment.')
param imageTag string = 'latest'

// ---- Azure SQL administrator (SQL authentication; also the login that applies migrations) ----
@description('SQL administrator login name')
param sqlAdminLogin string
@secure()
@description('SQL administrator password. Pass it on the command line or from a pipeline secret; never commit it.')
param sqlAdminPassword string

// ---- admin site sign-in ----
@description('Application (client) id of the Entra app registration for the admin site (see docs/entra-setup.md)')
param adminClientId string
param tenantId string = tenant().tenantId

// ---- sizing ----
@description('Plan for the three container web apps')
param appServiceSku string = 'B1'
param sqlSkuName string = 'S0'
@description('Microsoft Defender for SQL threat detection (adds a per-server monthly charge)')
param sqlThreatDetection bool = true
@minValue(1)
param appServiceInstances int = 1
param sqlSkuTier string = 'Standard'
@description('Azure Managed Redis SKU')
param redisSku string = 'Balanced_B0'
@description('Purge protection is irreversible once on; set false only for throwaway environments')
param keyVaultPurgeProtection bool = false
@description('Change this (e.g. "2") to get a new Key Vault name when the old one is still soft-deleted after a teardown')
param keyVaultNameSalt string = 'abadaba'

param tags object = {
  app: 'partsstore'
  environment: environmentName
}

var baseName = 'partsstore-${environmentName}'
var suffix = uniqueString(resourceGroup().id)
var shortSuffix = take(suffix, 6)

var names = {
  registry: 'acr${replace(baseName, '-', '')}${shortSuffix}'
  // Max 24 chars: 'kv-' + project/environment (trimmed to 18 in all) + '-' + 5-char suffix. The salt lets you pick a fresh
  // name after a teardown, because a deleted vault keeps its name reserved while soft-deleted (and forever if purge-protected).
  keyVault: '${take('kv-${baseName}', 18)}-${take(uniqueString(resourceGroup().id, keyVaultNameSalt), 5)}'
  sqlServer: 'sql-${baseName}-${shortSuffix}'
  sqlDatabase: 'partsstore'
  serviceBus: 'sb-${baseName}-${shortSuffix}'
  redis: 'redis-${baseName}-${shortSuffix}'
  webPlan: 'asp-${baseName}'
  apiApp: 'app-${baseName}-api-${shortSuffix}'
  storeApp: 'app-${baseName}-store-${shortSuffix}'
  adminApp: 'app-${baseName}-admin-${shortSuffix}'
  functionPlan: 'asp-func-${baseName}'
  functionApp: 'func-${baseName}-${shortSuffix}'
  functionStorage: take('st${replace(environmentName, '-', '')}${suffix}', 24)
}

module identities 'modules/identities.bicep' = {
  name: 'identities'
  params: { baseName: baseName, location: location, tags: tags }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: { name: names.functionStorage, location: location, tags: tags }
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: { baseName: baseName, location: location, tags: tags }
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: { name: names.registry, location: location, tags: tags }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: { name: names.keyVault, location: location, tags: tags, enablePurgeProtection: keyVaultPurgeProtection }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    serverName: names.sqlServer
    databaseName: names.sqlDatabase
    location: location
    tags: tags
    adminLogin: sqlAdminLogin
    adminPassword: sqlAdminPassword
    keyVaultName: keyVault.outputs.name
    skuName: sqlSkuName
    skuTier: sqlSkuTier
    logAnalyticsWorkspaceId: monitoring.outputs.workspaceId
    enableThreatDetection: sqlThreatDetection
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus'
  params: { name: names.serviceBus, location: location, tags: tags }
}

module redis 'modules/redis.bicep' = {
  name: 'redis'
  params: { name: names.redis, location: location, tags: tags, skuName: redisSku, keyVaultName: keyVault.outputs.name }
}

// Every permission is granted to the (already existing) identities before any app is created.
module rbac 'modules/rbac.bicep' = {
  name: 'rbac'
  params: {
    registryName: registry.outputs.name
    keyVaultName: keyVault.outputs.name
    serviceBusName: serviceBus.outputs.name
    storageAccountName: storage.outputs.name
    apiPrincipalId: identities.outputs.api.principalId
    webPrincipalId: identities.outputs.web.principalId
    functionPrincipalId: identities.outputs.functions.principalId
  }
}

module webApps 'modules/webapps.bicep' = {
  name: 'webapps'
  dependsOn: [ rbac ]
  params: {
    location: location
    tags: tags
    planName: names.webPlan
    skuName: appServiceSku
    instanceCount: appServiceInstances
    apiAppName: names.apiApp
    storeAppName: names.storeApp
    adminAppName: names.adminApp
    registryLoginServer: registry.outputs.loginServer
    apiIdentity: identities.outputs.api
    webIdentity: identities.outputs.web
    imageTag: imageTag
    sqlSecretName: sql.outputs.connectionStringSecretName
    serviceBusNamespace: serviceBus.outputs.name
    keyVaultUri: keyVault.outputs.uri
    redisSecretName: redis.outputs.connectionStringSecretName
    dataProtectionKeyUri: keyVault.outputs.dataProtectionKeyUri
    appInsightsConnectionString: monitoring.outputs.connectionString
    tenantId: tenantId
    adminClientId: adminClientId
  }
}

module functions 'modules/functionapp.bicep' = {
  name: 'functions'
  dependsOn: [ rbac ]
  params: {
    location: location
    tags: tags
    functionAppName: names.functionApp
    planName: names.functionPlan
    storageAccountName: storage.outputs.name
    deploymentContainerUrl: storage.outputs.deploymentContainerUrl
    identity: identities.outputs.functions
    keyVaultUri: keyVault.outputs.uri
    sqlSecretName: sql.outputs.connectionStringSecretName
    serviceBusNamespace: serviceBus.outputs.name
    appInsightsConnectionString: monitoring.outputs.connectionString
  }
}
// What the deployment pipeline needs to know about what was created.
output registryName string = registry.outputs.name
output registryLoginServer string = registry.outputs.loginServer
output apiAppName string = names.apiApp
output storeAppName string = names.storeApp
output adminAppName string = names.adminApp
output functionAppName string = functions.outputs.functionAppName
output sqlServerName string = sql.outputs.serverName
output sqlServerFqdn string = sql.outputs.fullyQualifiedDomainName
output sqlDatabaseName string = sql.outputs.databaseName
output apiUrl string = webApps.outputs.apiUrl
output storeUrl string = webApps.outputs.storeUrl
output adminUrl string = webApps.outputs.adminUrl
output keyVaultName string = keyVault.outputs.name
