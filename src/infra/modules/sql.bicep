@description('Globally unique, lowercase')
param serverName string
param databaseName string
param location string
param tags object

@description('SQL administrator login (also runs migrations and is what the apps connect as)')
param adminLogin string
@secure()
@description('Password for the SQL administrator login')
param adminPassword string
@description('Key Vault that receives the connection string as a secret')
param keyVaultName string

@description('Log Analytics workspace that receives the audit log')
param logAnalyticsWorkspaceId string
@description('Microsoft Defender for SQL threat detection (billed per server per month; see the pricing page)')
param enableThreatDetection bool = true

param skuName string = 'S0'
param skuTier string = 'Standard'

// SQL authentication: a single administrator login/password. The apps read the connection string from Key Vault.
resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administratorLogin: adminLogin
    administratorLoginPassword: adminPassword
  }
}

// Lets Azure-hosted services (App Service, Functions) reach the server. A private endpoint is the stronger option
// for later; the firewall plus a strong password is the baseline.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: server
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    requestedBackupStorageRedundancy: 'Local'
  }
}

// Audit trail: who signed in (or failed to), and every permission, role-membership and schema change. Deliberately NOT
// every query (BATCH_COMPLETED_GROUP): that is very high volume and mostly noise for an application database.
resource auditing 'Microsoft.Sql/servers/auditingSettings@2023-08-01' = {
  parent: server
  name: 'default'
  properties: {
    state: 'Enabled'
    isAzureMonitorTargetEnabled: true
    auditActionsAndGroups: [
      'SUCCESSFUL_DATABASE_AUTHENTICATION_GROUP'
      'FAILED_DATABASE_AUTHENTICATION_GROUP'
      'DATABASE_PERMISSION_CHANGE_GROUP'
      'DATABASE_ROLE_MEMBER_CHANGE_GROUP'
      'SCHEMA_OBJECT_CHANGE_GROUP'
    ]
  }
}

// With the Azure Monitor target, server audit events are routed through a diagnostic setting on the master database.
resource master 'Microsoft.Sql/servers/databases@2023-08-01' existing = {
  parent: server
  name: 'master'
}

resource auditToLogAnalytics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: master
  name: 'sql-audit-to-log-analytics'
  dependsOn: [ auditing ]
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      { category: 'SQLSecurityAuditEvents', enabled: true }
    ]
  }
}

resource threatDetection 'Microsoft.Sql/servers/securityAlertPolicies@2023-08-01' = if (enableThreatDetection) {
  parent: server
  name: 'Default'
  properties: {
    state: 'Enabled'
    emailAccountAdmins: true
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// The apps read this through a Key Vault reference, so the password never appears in app settings or deployment outputs.
resource connectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'sql-connection-string'
  properties: {
    value: 'Server=tcp:${server.properties.fullyQualifiedDomainName},1433;Database=${database.name};User ID=${adminLogin};Password=${adminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30'
    contentType: 'SQL Server connection string'
  }
}

output connectionStringSecretName string = connectionStringSecret.name
output serverName string = server.name
output fullyQualifiedDomainName string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
