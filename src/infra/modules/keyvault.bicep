@description('Globally unique, 3-24 characters')
param name string
param location string
param tags object
@description('Purge protection is irreversible once on; turn it off only for throwaway environments')
param enablePurgeProtection bool = true

// Access is by Azure RBAC (assigned in rbac.bicep), not legacy access policies.
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 30
    enablePurgeProtection: enablePurgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
  }
}

// Wraps the ASP.NET Data Protection keys (customer token signing keys) stored in SQL, so a copy of that table is useless.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [ 'wrapKey', 'unwrapKey' ]
  }
}

output name string = vault.name
output uri string = vault.properties.vaultUri
output dataProtectionKeyUri string = dataProtectionKey.properties.keyUri
