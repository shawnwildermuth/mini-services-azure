namespace Store.Domain.Orders;

public enum OrderStatus
{
  /// <summary>Accepted and stock reserved; waiting for payment.</summary>
  Pending,
  /// <summary>Payment captured; waiting to ship.</summary>
  Paid,
  PaymentFailed,
  Shipped,
  Cancelled,
  /// <summary>Shipped, then the payment was returned to the customer.</summary>
  Refunded,
}

public enum Carrier
{
  UPS,
  DHL,
}

public class Order
{
  public Guid Id { get; set; } = Guid.NewGuid();
  /// <summary>Customer-facing reference, e.g. PS-261022-A1B2C3.</summary>
  public required string OrderNumber { get; set; }
  public required string UserId { get; set; }
  public required string CustomerEmail { get; set; }
  public OrderStatus Status { get; set; } = OrderStatus.Pending;
  public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
  public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

  public decimal Subtotal { get; set; }
  public decimal ShippingCost { get; set; }
  public decimal Total { get; set; }
  public Carrier Carrier { get; set; }

  // The address as it was at checkout; later edits to the address book don't change placed orders.
  public required string ShipToName { get; set; }
  public required string ShipToLine1 { get; set; }
  public string? ShipToLine2 { get; set; }
  public required string ShipToCity { get; set; }
  public string? ShipToRegion { get; set; }
  public required string ShipToPostalCode { get; set; }
  public required string ShipToCountryCode { get; set; }
  public string? ShipToPhone { get; set; }

  /// <summary>Client-supplied key that makes a retried checkout return the same order.</summary>
  public string? IdempotencyKey { get; set; }

  public List<OrderItem> Items { get; set; } = [];

  /// <summary>Concurrency token: billing, fulfillment and admin actions can't silently overwrite each other's status changes.</summary>
  public byte[] RowVersion { get; set; } = [];
}

public class OrderItem
{
  public int Id { get; set; }
  public Guid OrderId { get; set; }
  public int ProductId { get; set; }
  // Snapshots, so the order is unaffected by later catalog edits.
  public required string Sku { get; set; }
  public required string Name { get; set; }
  public decimal UnitPrice { get; set; }
  public int Quantity { get; set; }
  public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>Simulated shipping rules shared by the API (quotes) and fulfillment.</summary>
public static class ShippingPolicy
{
  private static readonly HashSet<string> NorthAmerica = ["US", "CA", "MX"];

  /// <summary>UPS within North America, DHL for everywhere else.</summary>
  public static Carrier CarrierFor(string countryCode) =>
    NorthAmerica.Contains(countryCode.Trim().ToUpperInvariant()) ? Carrier.UPS : Carrier.DHL;

  /// <summary>Flat simulated rates until real carrier integrations exist.</summary>
  public static decimal RateFor(Carrier carrier) => carrier == Carrier.UPS ? 12.00m : 29.00m;
}
