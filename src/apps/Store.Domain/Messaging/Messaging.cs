namespace Store.Domain.Messaging;

/// <summary>Service Bus queue names shared by the API (publisher) and the functions (consumers).</summary>
public static class Queues
{
  public const string OrderPlaced = "order-placed";
  public const string PaymentCaptured = "payment-captured";
}

/// <summary>Published when checkout succeeds. Consumers load order details from the database.</summary>
public record OrderPlaced(
  Guid OrderId, string OrderNumber, string CustomerEmail, decimal Total, string Currency, DateTimeOffset PlacedAt);

/// <summary>Published by billing once an order has been paid; triggers fulfillment.</summary>
public record PaymentCaptured(Guid OrderId, string OrderNumber, Guid PaymentId, decimal Amount, DateTimeOffset CapturedAt);

/// <summary>
/// A message written in the same transaction as the change that caused it, then published to
/// Service Bus by the dispatcher (transactional outbox). Delivery is at-least-once.
/// </summary>
public class OutboxMessage
{
  /// <summary>Also used as the Service Bus MessageId, so duplicate detection drops re-publishes.</summary>
  public Guid Id { get; set; } = Guid.NewGuid();
  public required string Destination { get; set; }
  public required string Type { get; set; }
  public required string Payload { get; set; }
  public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
  public DateTimeOffset? ProcessedAt { get; set; }
  public int Attempts { get; set; }
  public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
  public string? LastError { get; set; }
}
