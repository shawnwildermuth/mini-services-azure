using Microsoft.EntityFrameworkCore;
using Store.Data;
using Store.Domain.Messaging;
using Store.Domain.Orders;
using Store.Functions.Notifications;
using Store.Functions.Payments;

namespace Store.Functions.Billing;

public enum BillingOutcome
{
  /// <summary>The order is paid; <see cref="BillingResult.Message"/> must reach fulfillment.</summary>
  Captured,
  /// <summary>The payment was declined and the stock reservation released.</summary>
  Declined,
  /// <summary>Nothing to do: the order has already moved past billing (shipped, cancelled).</summary>
  AlreadyCompleted,
  /// <summary>No such order. A permanent problem: retrying cannot help.</summary>
  OrderNotFound,
}

public record BillingResult(BillingOutcome Outcome, PaymentCaptured? Message = null);

/// <summary>
/// Charges orders announced by <see cref="OrderPlaced"/>. Safe to run any number of times for the same
/// order (Service Bus delivers at least once): the order's status and the one-payment-per-order unique
/// index guarantee a single charge, and a replay of an already-paid order simply re-emits
/// <see cref="PaymentCaptured"/> in case the first send was lost.
/// </summary>
public class BillingService(
  StoreDbContext db, IPaymentProvider provider, ICustomerNotifier notifier, TimeProvider clock, ILogger<BillingService> logger)
{
  public async Task<BillingResult> ProcessAsync(OrderPlaced placed, CancellationToken ct)
  {
    var (result, declined) = await Retry.OnConcurrencyAsync(db, () => ProcessOnceAsync(placed, ct));

    if (declined is not null)
    {
      // After the commit, and never fatal: a lost notice must not make us re-process the order.
      try { await notifier.PaymentDeclinedAsync(declined.Order, declined.Reason, ct); }
      catch (Exception ex) { logger.LogWarning(ex, "Could not send the payment-declined notice for {OrderNumber}", declined.Order.OrderNumber); }
    }
    return result;
  }

  private record Declined(Order Order, string? Reason);

  private async Task<(BillingResult, Declined?)> ProcessOnceAsync(OrderPlaced placed, CancellationToken ct)
  {
    var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == placed.OrderId, ct);
    if (order is null)
    {
      logger.LogError("OrderPlaced for unknown order {OrderId}", placed.OrderId);
      return (new BillingResult(BillingOutcome.OrderNotFound), null);
    }

    var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == order.Id, ct);
    Declined? newlyDeclined = null;

    if (order.Status == OrderStatus.Pending && payment is null)
    {
      var charge = await provider.ChargeAsync(
        new PaymentRequest(order.Id, order.OrderNumber, order.Total, "USD", order.CustomerEmail), ct);

      var now = clock.GetUtcNow();
      payment = new Payment
      {
        OrderId = order.Id,
        Amount = order.Total,
        Currency = "USD",
        Status = charge.Approved ? PaymentStatus.Captured : PaymentStatus.Declined,
        ProviderReference = charge.Reference,
        DeclineReason = charge.DeclineReason,
        CreatedAt = now,
      };
      db.Payments.Add(payment);
      order.UpdatedAt = now;

      if (charge.Approved)
      {
        order.Status = OrderStatus.Paid;
      }
      else
      {
        order.Status = OrderStatus.PaymentFailed;
        await ReleaseReservationAsync(order, ct);
        newlyDeclined = new Declined(order, charge.DeclineReason);
      }

      await db.SaveChangesAsync(ct); // payment, order status and stock release commit together
      logger.LogInformation("Order {OrderNumber} payment {Status}", order.OrderNumber, payment.Status);
    }

    return order.Status switch
    {
      OrderStatus.Paid when payment is { Status: PaymentStatus.Captured } =>
        (new BillingResult(BillingOutcome.Captured,
          new PaymentCaptured(order.Id, order.OrderNumber, payment.Id, payment.Amount, payment.CreatedAt)), null),
      OrderStatus.PaymentFailed => (new BillingResult(BillingOutcome.Declined), newlyDeclined),
      _ => (new BillingResult(BillingOutcome.AlreadyCompleted), null),
    };
  }

  /// <summary>A failed payment frees the units that checkout held for the order.</summary>
  private async Task ReleaseReservationAsync(Order order, CancellationToken ct)
  {
    var productIds = order.Items.Select(i => i.ProductId).ToList();
    var inventory = await db.Inventory.Where(i => productIds.Contains(i.ProductId)).ToDictionaryAsync(i => i.ProductId, ct);

    foreach (var item in order.Items)
      if (inventory.TryGetValue(item.ProductId, out var stock))
        stock.QuantityReserved = Math.Max(stock.QuantityReserved - item.Quantity, 0);
  }
}
