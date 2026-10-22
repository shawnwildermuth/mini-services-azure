// Who may do what. Every permission an app has is listed here, and nothing uses keys or passwords.
// These are granted to user-assigned identities that exist before the apps do, so everything is in place at first start:
//   web identity ... pull images from the registry
//   api identity ... pull images, read the Redis secret, wrap/unwrap the data-protection key, send + receive Service Bus messages
//   func identity .. pull nothing; send + receive Service Bus messages, use its storage account
// SQL access is granted separately, inside the database (infra/scripts/grant-sql-access.cs).

param registryName string
param keyVaultName string
param serviceBusName string
param storageAccountName string

param apiPrincipalId string
param webPrincipalId string
param functionPrincipalId string

// Built-in role definition ids
var roles = {
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  keyVaultCryptoUser: '12338af0-0e69-4776-bea7-57ae8d297424'
  serviceBusDataSender: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
  serviceBusDataReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageQueueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  storageTableDataContributor: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = { name: registryName }
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = { name: keyVaultName }
resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = { name: serviceBusName }
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = { name: storageAccountName }

func roleId(roleGuid string) string => subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleGuid)

// ---- image pulls ----
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for p in [
  { key: 'api', id: apiPrincipalId }
  { key: 'web', id: webPrincipalId }
]: {
  name: guid(registry.id, p.id, roles.acrPull)
  scope: registry
  properties: {
    roleDefinitionId: roleId(roles.acrPull)
    principalId: p.id
    principalType: 'ServicePrincipal'
  }
}]

// ---- API: Key Vault ----
resource apiSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, apiPrincipalId, roles.keyVaultSecretsUser)
  scope: vault
  properties: {
    roleDefinitionId: roleId(roles.keyVaultSecretsUser)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource apiCryptoUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, apiPrincipalId, roles.keyVaultCryptoUser)
  scope: vault
  properties: {
    roleDefinitionId: roleId(roles.keyVaultCryptoUser)
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ---- Service Bus: the API publishes (outbox) and administers dead letters; the functions consume and publish ----
resource serviceBusAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for a in [
  { who: 'api', principal: apiPrincipalId, role: roles.serviceBusDataSender }
  { who: 'api', principal: apiPrincipalId, role: roles.serviceBusDataReceiver }
  { who: 'func', principal: functionPrincipalId, role: roles.serviceBusDataSender }
  { who: 'func', principal: functionPrincipalId, role: roles.serviceBusDataReceiver }
]: {
  name: guid(serviceBus.id, a.principal, a.role)
  scope: serviceBus
  properties: {
    roleDefinitionId: roleId(a.role)
    principalId: a.principal
    principalType: 'ServicePrincipal'
  }
}]

// ---- Function App host storage (blob for the deployment package + leases, queues and tables for the host) ----
resource functionStorageAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for role in [
  roles.storageBlobDataOwner
  roles.storageQueueDataContributor
  roles.storageTableDataContributor
]: {
  name: guid(storage.id, functionPrincipalId, role)
  scope: storage
  properties: {
    roleDefinitionId: roleId(role)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}]
