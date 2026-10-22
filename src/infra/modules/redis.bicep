@description('Globally unique cluster name')
param name string
param location string
param tags object
@description('Azure Managed Redis SKU, e.g. Balanced_B0 (smallest), Balanced_B1, Balanced_B3')
param skuName string = 'Balanced_B0'
@description('Key Vault that receives the connection string as a secret')
param keyVaultName string

// Azure Managed Redis is Microsoft's current Redis offering (Azure Cache for Redis Basic/Standard/Premium is being retired).
resource cluster 'Microsoft.Cache/redisEnterprise@2025-04-01' = {
  name: name
  location: location
  tags: tags
  sku: { name: skuName }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource database 'Microsoft.Cache/redisEnterprise/databases@2025-04-01' = {
  parent: cluster
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
    port: 10000
    clusteringPolicy: 'EnterpriseCluster'
    // The catalog cache entries all carry a TTL, and the single version key does not: only expiring keys are evictable.
    evictionPolicy: 'VolatileLRU'
    accessKeysAuthentication: 'Enabled'
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// The apps read this through a Key Vault reference, so the key never appears in app settings or deployment outputs.
resource connectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'redis-connection-string'
  properties: {
    value: '${cluster.properties.hostName}:10000,password=${database.listKeys().primaryKey},ssl=True,abortConnect=False'
    contentType: 'StackExchange.Redis connection string'
  }
}

output hostName string = cluster.properties.hostName
output connectionStringSecretName string = connectionStringSecret.name
