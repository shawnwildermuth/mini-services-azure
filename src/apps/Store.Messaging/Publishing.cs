using Azure.Identity;
using Azure.Messaging.ServiceBus;

namespace Store.Messaging;

/// <summary>Sends a message to a queue. Implementations must be safe to call repeatedly with the same id.</summary>
public interface IMessagePublisher
{
  Task PublishAsync(string queue, Guid messageId, string type, string json, CancellationToken ct);
}

public class ServiceBusPublisher(ServiceBusClient client) : IMessagePublisher, IAsyncDisposable
{
  public async Task PublishAsync(string queue, Guid messageId, string type, string json, CancellationToken ct)
  {
    await using var sender = client.CreateSender(queue);
    await sender.SendMessageAsync(new ServiceBusMessage(json)
    {
      // The queues have duplicate detection on MessageId, so a re-send after a crash or replay is dropped.
      MessageId = messageId.ToString(),
      Subject = type,
      ContentType = "application/json",
    }, ct);
  }

  public ValueTask DisposeAsync() => client.DisposeAsync();
}

public static class ServiceBusClients
{
  /// <summary>
  /// A connection string (local emulator, or any SAS connection) wins; otherwise the namespace is reached with
  /// the app's identity (managed identity in Azure, the developer's login locally).
  /// </summary>
  public static ServiceBusClient Create(string? connectionString, string? fullyQualifiedNamespace) =>
    !string.IsNullOrWhiteSpace(connectionString)
      ? new ServiceBusClient(connectionString)
      : !string.IsNullOrWhiteSpace(fullyQualifiedNamespace)
        ? new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential())
        : throw new InvalidOperationException("Service Bus is not configured.");
}
