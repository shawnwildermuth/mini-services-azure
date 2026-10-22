param baseName string
param location string
param tags object

// User-assigned managed identities, created before any app so that every role (rbac.bicep) and the SQL database
// users (scripts/grant-sql-access.cs) can be in place before the first container starts, and so that their client ids
// are plain deployment outputs. Least privilege: the two web front ends can only pull images.
resource api 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${baseName}-api'
  location: location
  tags: tags
}

resource functions 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${baseName}-func'
  location: location
  tags: tags
}

resource web 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${baseName}-web'
  location: location
  tags: tags
}

output api object = { name: api.name, id: api.id, clientId: api.properties.clientId, principalId: api.properties.principalId }
output functions object = { name: functions.name, id: functions.id, clientId: functions.properties.clientId, principalId: functions.properties.principalId }
output web object = { name: web.name, id: web.id, clientId: web.properties.clientId, principalId: web.properties.principalId }
