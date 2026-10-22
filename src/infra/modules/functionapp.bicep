param location string
param tags object

@description('Globally unique, 2-60 characters')
param functionAppName string
param planName string
param storageAccountName string
param deploymentContainerUrl string

@description('The function app\'s user-assigned managed identity')
param identity object

param sqlServerFqdn string
param sqlDatabaseName string
param serviceBusNamespace string
param appInsightsConnectionString string

// Flex Consumption: scales to zero, scales out on queue depth, and supports .NET 10 isolated.
resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: { reserved: true }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: deploymentContainerUrl
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: identity.id
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 20
        instanceMemoryMB: 2048
      }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      http20Enabled: true
      ftpsState: 'Disabled'
      // Flex Consumption takes the runtime from functionAppConfig; FUNCTIONS_WORKER_RUNTIME etc. must not be set here.
      appSettings: [
        // Identity-based host storage (no connection string / key).
        { name: 'AzureWebJobsStorage__accountName', value: storageAccountName }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: identity.clientId }
        // The Service Bus triggers use the identity-based connection named "ServiceBus"...
        { name: 'ServiceBus__fullyQualifiedNamespace', value: '${serviceBusNamespace}.servicebus.windows.net' }
        { name: 'ServiceBus__credential', value: 'managedidentity' }
        { name: 'ServiceBus__clientId', value: identity.clientId }
        // ...and our own code (publishing payment-captured) uses DefaultAzureCredential, which honours this.
        { name: 'AZURE_CLIENT_ID', value: identity.clientId }
        { name: 'ConnectionStrings__StoreDb', value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${identity.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
      ]
    }
  }
}

output functionAppName string = functionApp.name
