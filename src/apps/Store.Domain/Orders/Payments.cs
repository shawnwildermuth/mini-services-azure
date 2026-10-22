namespace Store.Domain.Orders;

public enum PaymentStatus
{
  Captured,
  Declined,
  /// <summary>Was captured, then returned to the customer (order cancelled or refunded).</summary>
  Refunded,
}

/// <summary>The outcome of charging an order. At most one per order (unique index), which makes billing idempotent.</summary>
public class Payment
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public Guid OrderId { get; set; }
  public decimal Amount { get; set; }
  public required string Currency { get; set; }
  public PaymentStatus Status { get; set; }
  /// <summary>The payment provider's id for the charge.</summary>
  public string? ProviderReference { get; set; }
  public string? DeclineReason { get; set; }
  public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>How an order is being delivered. At most one per order (unique index).</summary>
public class Shipment
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public Guid OrderId { get; set; }
  public Carrier Carrier { get; set; }
  public required string TrackingNumber { get; set; }
  public required string Service { get; set; }
  public DateTimeOffset ShippedAt { get; set; } = DateTimeOffset.UtcNow;
  public DateTimeOffset EstimatedDelivery { get; set; }
}
