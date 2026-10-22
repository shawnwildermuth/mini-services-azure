@description('Globally unique, lowercase')
param serverName string
param databaseName string
param location string
param tags object

@description('Object id of the Entra user, group or application that administers the server (also runs migrations)')
param adminObjectId string
@description('Display name / login of that administrator')
param adminLogin string
@allowed([ 'User', 'Group', 'Application' ])
param adminType string = 'Group'

@description('Log Analytics workspace that receives the audit log')
param logAnalyticsWorkspaceId string
@description('Microsoft Defender for SQL threat detection (billed per server per month; see the pricing page)')
param enableThreatDetection bool = true

param skuName string = 'S0'
param skuTier string = 'Standard'

// Microsoft Entra authentication only: there is no SQL password anywhere. Apps connect with their managed identities.
resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: adminType
      login: adminLogin
      sid: adminObjectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

// Lets Azure-hosted services (App Service, Functions) reach the server. A private endpoint is the stronger option
// for later; the firewall plus Entra-only authentication is the baseline.
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

output serverName string = server.name
output fullyQualifiedDomainName string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
