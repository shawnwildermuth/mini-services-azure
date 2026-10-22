using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Store.Data;
using Store.Domain.Catalog;
using Store.Domain.Customers;
using Store.Domain.Orders;
using Store.Functions.Notifications;
using Store.Functions.Payments;
using Store.Functions.Shipping;
using Store.Functions.Billing;
using Store.Functions.Fulfillment;

namespace Store.Functions.Tests;

public class RecordingNotifier : ICustomerNotifier
{
  public List<string> Declined { get; } = [];
  public List<string> Shipped { get; } = [];
  public bool Throws { get; set; }

  public Task PaymentDeclinedAsync(Order order, string? reason, CancellationToken ct)
  {
    if (Throws) throw new InvalidOperationException("mail server down");
    Declined.Add(order.OrderNumber);
    return Task.CompletedTask;
  }

  public Task OrderShippedAsync(Order order, Shipment shipment, CancellationToken ct)
  {
    if (Throws) throw new InvalidOperationException("mail server down");
    Shipped.Add(order.OrderNumber);
    return Task.CompletedTask;
  }
}

/// <summary>A fresh in-memory database plus the services under test, wired like production.</summary>
public class Harness : IDisposable
{
  public StoreDbContext Db { get; } = new(
    new DbContextOptionsBuilder<StoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

  public RecordingNotifier Notifier { get; } = new();
  public TestClock Clock { get; } = new();

  public BillingService Billing => new(Db, new MockPaymentProvider(), Notifier, Clock, NullLogger<BillingService>.Instance);

  public FulfillmentService Fulfillment => new(
    Db, [new SimulatedUps(), new SimulatedDhl()], Notifier, Clock, NullLogger<FulfillmentService>.Instance);

  /// <summary>Seeds a product with stock 10 and an order of <paramref name="quantity"/> units, with stock reserved as checkout would.</summary>
  public async Task<Order> PlaceOrderAsync(string email = "buyer@example.com", string country = "US", int quantity = 2, OrderStatus status = OrderStatus.Pending)
  {
    var product = new Product
    {
      Sku = $"SKU-{Guid.NewGuid():N}",
      Name = "Widget",
      Brand = "Acme",
      Price = 50m,
      Category = new Category { Slug = $"c-{Guid.NewGuid():N}", Name = "C" },
      Inventory = new Inventory { QuantityOnHand = 10, QuantityReserved = quantity },
    };

    var carrier = ShippingPolicy.CarrierFor(country);
    var order = new Order
    {
      OrderNumber = $"PS-{Guid.NewGuid():N}"[..14],
      UserId = "user-1",
      CustomerEmail = email,
      Status = status,
      Subtotal = 50m * quantity,
      ShippingCost = ShippingPolicy.RateFor(carrier),
      Total = 50m * quantity + ShippingPolicy.RateFor(carrier),
      Carrier = carrier,
      ShipToName = "Ada",
      ShipToLine1 = "1 Main",
      ShipToCity = "Town",
      ShipToPostalCode = "12345",
      ShipToCountryCode = country,
      Items = [new OrderItem { Sku = product.Sku, Name = product.Name, UnitPrice = 50m, Quantity = quantity }],
    };
    Db.Products.Add(product);
    await Db.SaveChangesAsync();
    order.Items[0].ProductId = product.Id;
    Db.Orders.Add(order);
    await Db.SaveChangesAsync();
    return order;
  }

  public async Task<(int OnHand, int Reserved)> StockOfAsync(Order order)
  {
    Db.ChangeTracker.Clear();
    var productId = order.Items[0].ProductId;
    var stock = await Db.Inventory.AsNoTracking().SingleAsync(i => i.ProductId == productId);
    return (stock.QuantityOnHand, stock.QuantityReserved);
  }

  public void Dispose() => Db.Dispose();
}

public class TestClock : TimeProvider
{
  public DateTimeOffset Now { get; set; } = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
  public override DateTimeOffset GetUtcNow() => Now;
}
