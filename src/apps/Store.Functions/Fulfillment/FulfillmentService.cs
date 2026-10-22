using Microsoft.EntityFrameworkCore;
using Store.Data;
using Store.Domain.Messaging;
using Store.Domain.Orders;
using Store.Functions.Notifications;
using Store.Functions.Shipping;

namespace Store.Functions.Fulfillment;

public enum FulfillmentOutcome
{
  Shipped,
  /// <summary>Redelivery of a message for an order that already shipped.</summary>
  AlreadyShipped,
  /// <summary>The order will never ship (payment failed or cancelled).</summary>
  Skipped,
  /// <summary>No such order. A permanent problem: retrying cannot help.</summary>
  OrderNotFound,
}

/// <summary>The order is not in a state that can be shipped yet. Transient: redelivery may find it ready.</summary>
public class OrderNotReadyException(string message) : Exception(message);

public record FulfillmentResult(FulfillmentOutcome Outcome, Shipment? Shipment = null);

/// <summary>
/// Ships paid orders announced by <see cref="PaymentCaptured"/>. Idempotent: an order ships once (status
/// guard + one-shipment-per-order unique index), and the simulated carriers return the same tracking
/// number for the same order, so a retry after a crash can't create a second shipment.
/// </summary>
public class FulfillmentService(
  StoreDbContext db, IEnumerable<IShippingCarrier> carriers, ICustomerNotifier notifier, TimeProvider clock, ILogger<FulfillmentService> logger)
{
  public async Task<FulfillmentResult> ProcessAsync(PaymentCaptured captured, CancellationToken ct)
  {
    var (result, order) = await Retry.OnConcurrencyAsync(db, () => ProcessOnceAsync(captured, ct));

    if (result.Outcome == FulfillmentOutcome.Shipped && order is not null)
    {
      try { await notifier.OrderShippedAsync(order, result.Shipment!, ct); }
      catch (Exception ex) { logger.LogWarning(ex, "Could not send the shipped notice for {OrderNumber}", order.OrderNumber); }
    }
    return result;
  }

  private async Task<(FulfillmentResult, Order?)> ProcessOnceAsync(PaymentCaptured captured, CancellationToken ct)
  {
    var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == captured.OrderId, ct);
    if (order is null)
    {
      logger.LogError("PaymentCaptured for unknown order {OrderId}", captured.OrderId);
      return (new FulfillmentResult(FulfillmentOutcome.OrderNotFound), null);
    }

    switch (order.Status)
    {
      case OrderStatus.Shipped:
        return (new FulfillmentResult(FulfillmentOutcome.AlreadyShipped), null);
      case OrderStatus.PaymentFailed or OrderStatus.Cancelled or OrderStatus.Refunded:
        logger.LogWarning("Not shipping order {OrderNumber}: it is {Status}", order.OrderNumber, order.Status);
        return (new FulfillmentResult(FulfillmentOutcome.Skipped), null);
      case OrderStatus.Pending:
        throw new OrderNotReadyException($"Order {order.OrderNumber} is not marked as paid yet.");
    }

    var carrier = carriers.Single(c => c.Carrier == order.Carrier);
    var label = await carrier.CreateShipmentAsync(
      new ShipmentRequest(order.Id, order.OrderNumber, order.CustomerEmail, order.ShipToCountryCode), ct);

    var now = clock.GetUtcNow();
    var shipment = new Shipment
    {
      OrderId = order.Id,
      Carrier = order.Carrier,
      TrackingNumber = label.TrackingNumber,
      Service = label.Service,
      ShippedAt = now,
      EstimatedDelivery = now + label.TransitTime,
    };
    db.Shipments.Add(shipment);

    order.Status = OrderStatus.Shipped;
    order.UpdatedAt = now;

    // The units leave the warehouse: they are no longer on hand, and no longer merely reserved.
    var productIds = order.Items.Select(i => i.ProductId).ToList();
    var inventory = await db.Inventory.Where(i => productIds.Contains(i.ProductId)).ToDictionaryAsync(i => i.ProductId, ct);
    foreach (var item in order.Items)
    {
      if (!inventory.TryGetValue(item.ProductId, out var stock)) continue;
      stock.QuantityOnHand = Math.Max(stock.QuantityOnHand - item.Quantity, 0);
      stock.QuantityReserved = Math.Max(stock.QuantityReserved - item.Quantity, 0);
    }

    await db.SaveChangesAsync(ct); // shipment, order status and stock commit together
    logger.LogInformation("Order {OrderNumber} shipped via {Carrier} ({Tracking})", order.OrderNumber, shipment.Carrier, shipment.TrackingNumber);
    return (new FulfillmentResult(FulfillmentOutcome.Shipped, shipment), order);
  }
}
