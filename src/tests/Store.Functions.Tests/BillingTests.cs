using Microsoft.EntityFrameworkCore;
using Store.Domain.Messaging;
using Store.Domain.Orders;
using Store.Functions.Billing;
using Store.Functions.Payments;

namespace Store.Functions.Tests;

public class BillingTests
{
  private static OrderPlaced Placed(Order o) => new(o.Id, o.OrderNumber, o.CustomerEmail, o.Total, "USD", DateTimeOffset.UtcNow);

  [Fact]
  public async Task ApprovedPayment_MarksOrderPaid_RecordsPayment_AndEmitsPaymentCaptured()
  {
    using var h = new Harness();
    var order = await h.PlaceOrderAsync();

    var result = await h.Billing.ProcessAsync(Placed(order), default);

    Assert.Equal(BillingOutcome.Captured, result.Outcome);
    var saved = await h.Db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
    Assert.Equal(OrderStatus.Paid, saved.Status);
    var payment = await h.Db.Payments.SingleAsync();
    Assert.Equal(PaymentStatus.Captured, payment.Status);
    Assert.Equal(order.Total, payment.Amount);
    Assert.StartsWith("mock_", payment.ProviderReference);
    Assert.Equal(order.Id, result.Message!.OrderId);
    Assert.Equal(payment.Id, result.Message.PaymentId);
    Assert.Equal(order.Total, result.Message.Amount);
    // Paying doesn't touch stock: the units stay reserved until the order ships.
    Assert.Equal((10, 2), await h.StockOfAsync(order));
  }

  [Fact]
  public async Task DeclinedPayment_FailsTheOrder_ReleasesReservation_AndNotifiesTheCustomer()
  {
    using var h = new Harness();
    var order = await h.PlaceOrderAsync(email: "buyer+decline@example.com");

    var result = await h.Billing.ProcessAsync(Placed(order), default);

    Assert.Equal(BillingOutcome.Declined, result.Outcome);
    Assert.Null(result.Message); // nothing for fulfillment to do
    Assert.Equal(OrderStatus.PaymentFailed, (await h.Db.Orders.AsNoTracking().SingleAsync()).Status);
    var payment = await h.Db.Payments.SingleAsync();
    Assert.Equal(PaymentStatus.Declined, payment.Status);
    Assert.Equal("card_declined", payment.DeclineReason);
    Assert.Equal((10, 0), await h.StockOfAsync(order)); // the two reserved units are back on the shelf
    Assert.Equal([order.OrderNumber], h.Notifier.Declined);
  }

  [Fact]
  public async Task RedeliveredMessage_DoesNotChargeTwice_ButReEmitsPaymentCaptured()
  {
    using var h = new Harness();
    var order = await h.PlaceOrderAsync();

    var first = await h.Billing.ProcessAsync(Placed(order), default);
    var second = await h.Billing.ProcessAsync(Placed(order), default);

    Assert.Single(await h.Db.Payments.ToListAsync());
    Assert.Equal(BillingOutcome.Captured, second.Outcome);
    // Same message id on both sends, so Service Bus duplicate detection collapses them.
    Assert.Equal(first.Message!.PaymentId, second.Message!.PaymentId);
  }

  [Fact]
  public async Task RedeliveredDeclinedMessage_DoesNotReleaseStockOrNotifyTwice()
  {
    using var h = new Harness();
    var order = await h.PlaceOrderAsync(email: "buyer+decline@example.com");

    await h.Billing.ProcessAsync(Placed(order), default);
    var second = await h.Billing.ProcessAsync(Placed(order), default);

    Assert.Equal(BillingOutcome.Declined, second.Outcome);
    Assert.Equal((10, 0), await h.StockOfAsync(order));
    Assert.Single(h.Notifier.Declined);
  }

  [Fact]
  public async Task ProviderOutage_Throws_AndLeavesTheOrderUntouchedForRetry()
  {
    using var h = new Harness();
    var order = await h.PlaceOrderAsync(email: "buyer+error@example.com");

    await Assert.ThrowsAsync<PaymentProviderUnavailableException>(() => h.Billing.ProcessAsync(Placed(order), default));

    Assert.Equal(OrderStatus.Pending, (await h.Db.Orders.AsNoTracking().SingleAsync()).Status);
    Assert.Empty(await h.Db.Payments.ToListAsync());
    Assert.Equal((10, 2), await h.StockOfAsync(order));
  }

  [Fact]
  public async Task UnknownOrder_IsReportedAsPermanentlyUnprocessable()
  {
    using var h = new Harness();

    var result = await h.Billing.ProcessAsync(new OrderPlaced(Guid.NewGuid(), "PS-X", "a@b.c", 1m, "USD", DateTimeOffset.UtcNow), default);

    Assert.Equal(BillingOutcome.OrderNotFound, result.Outcome);
  }

  [Theory]
  [InlineData(OrderStatus.Shipped)]
  [InlineData(OrderStatus.Cancelled)]
  public async Task OrdersPastBilling_AreLeftAlone(OrderStatus status)
  {
    using var h = new Harness();
    var order = await h.PlaceOrderAsync(status: status);

    var result = await h.Billing.ProcessAsync(Placed(order), default);

    Assert.Equal(BillingOutcome.AlreadyCompleted, result.Outcome);
    Assert.Empty(await h.Db.Payments.ToListAsync());
    Assert.Equal(status, (await h.Db.Orders.AsNoTracking().SingleAsync()).Status);
  }

  [Fact]
  public async Task NotificationFailure_DoesNotFailBilling()
  {
    using var h = new Harness();
    h.Notifier.Throws = true;
    var order = await h.PlaceOrderAsync(email: "buyer+decline@example.com");

    var result = await h.Billing.ProcessAsync(Placed(order), default);

    Assert.Equal(BillingOutcome.Declined, result.Outcome);
  }
}
