@description('Globally unique, 6-50 characters')
param name string
param location string
param tags object

// Standard is the lowest tier with duplicate detection, which the publishers rely on (message ids are deterministic).
// Shared access keys are disabled: the API and the Function App authenticate with their managed identities.
resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

var queueNames = [
  'order-placed'      // API outbox -> billing
  'payment-captured'  // billing -> fulfillment
]

resource queues 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = [for queueName in queueNames: {
  parent: namespace
  name: queueName
  properties: {
    maxDeliveryCount: 5                          // then dead-letter (the admin site's "failed messages")
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P7DT0H0M0S'
    deadLetteringOnMessageExpiration: true
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    enableBatchedOperations: true
  }
}]

output name string = namespace.name
output fullyQualifiedNamespace string = '${namespace.name}.servicebus.windows.net'
