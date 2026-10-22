using Microsoft.EntityFrameworkCore;
using Store.Domain.Messaging;
using Store.Domain.Orders;
using Store.Functions.Fulfillment;
using Store.Functions.Shipping;

namespace Store.Functions.Tests;

public class FulfillmentTests
{
  private static PaymentCaptured Captured(Order o) => new(o.Id, o.OrderNumber, Guid.NewGuid(), o.Total, DateTimeOffset.UtcNow);

  [Fact]
  public async Task PaidOrder_ShipsWithUps_ConsumesStock_AndNotifies()
  {
    using var h = new Harness();
    var order = await h.PlaceOrderAsync(country: "US", status: OrderStatus.Paid);

    var result = await h.Fulfillment.ProcessAsync(Captured(order), default);

    Assert.Equal(FulfillmentOutcome.Shipped, result.Outcome);
    Assert.Equal(OrderStatus.Shipped, (await h.Db.Orders.AsNoTracking().SingleAsync()).Status);
    var shipment = await h.Db.Shipments.SingleAsync();
    Assert.Equal(Carrier.UPS, shipment.Carrier);
    Assert.Matches(@"^1Z[0-9A-Z]{6}03\d{8}$", shipment.TrackingNumber);
    Assert.Equal("UPS Ground", shipment.Service);
    Assert.Equal(h.Clock.Now.AddDays(5), shipment.EstimatedDelivery);
    // 2 units leave: on hand 10 -> 8, and the 2 reserved are released.
    Assert.Equal((8, 0), await h.StockOfAsync(order));
    Assert.Equal([order.OrderNumber], h.Notifier.Shipped);
  }

  [Fact]
  public async Task InternationalOrder_ShipsWithDhl()
  {
    using var h = new Harness();
    var order = await h.PlaceOrderAsync(country: "DE", status: OrderStatus.Paid);

    await h.Fulfillment.ProcessAsync(Captured(order), default);

    var shipment = await h.Db.Shipments.SingleAsync();
    Assert.Equal(Carrier.DHL, shipment.Carrier);
    Assert.Matches(@"^\d{10}$", shipment.TrackingNumber);
    Assert.Equal("DHL Express Worldwide", shipment.Service);
  }

  [Fact]
  public async Task RedeliveredMessage_DoesNotShipOrDeductStockTwice()
  {
    using var h = new Harness();
    var order = await h.PlaceOrderAsync(status: OrderStatus.Paid);

    await h.Fulfillment.ProcessAsync(Captured(order), default);
    var second = await h.Fulfillment.ProcessAsync(Captured(order), default);

    Assert.Equal(FulfillmentOutcome.AlreadyShipped, second.Outcome);
    Assert.Single(await h.Db.Shipments.ToListAsync());
    Assert.Equal((8, 0), await h.StockOfAsync(order));
    Assert.Single(h.Notifier.Shipped);
  }

  [Fact]
  public async Task OrderNotYetPaid_Throws_SoServiceBusRedelivers()
  {
    using var h = new Harness();
    var order = await h.PlaceOrderAsync(status: OrderStatus.Pending);

    await Assert.ThrowsAsync<OrderNotReadyException>(() => h.Fulfillment.ProcessAsync(Captured(order), default));

    Assert.Empty(await h.Db.Shipments.ToListAsync());
  }

  [Theory]
  [InlineData(OrderStatus.PaymentFailed)]
  [InlineData(OrderStatus.Cancelled)]
  [InlineData(OrderStatus.Refunded)]
  public async Task OrdersThatWillNeverShip_AreSkipped(OrderStatus status)
  {
    using var h = new Harness();
    var order = await h.PlaceOrderAsync(status: status);

    var result = await h.Fulfillment.ProcessAsync(Captured(order), default);

    Assert.Equal(FulfillmentOutcome.Skipped, result.Outcome);
    Assert.Empty(await h.Db.Shipments.ToListAsync());
    Assert.Equal((10, 2), await h.StockOfAsync(order)); // untouched
  }

  [Fact]
  public async Task CarrierOutage_Throws_AndLeavesTheOrderPaidForRetry()
  {
    using var h = new Harness();
    var order = await h.PlaceOrderAsync(email: "buyer+shipfail@example.com", status: OrderStatus.Paid);

    await Assert.ThrowsAsync<CarrierUnavailableException>(() => h.Fulfillment.ProcessAsync(Captured(order), default));

    Assert.Equal(OrderStatus.Paid, (await h.Db.Orders.AsNoTracking().SingleAsync()).Status);
    Assert.Empty(await h.Db.Shipments.ToListAsync());
    Assert.Equal((10, 2), await h.StockOfAsync(order));
  }

  [Fact]
  public async Task UnknownOrder_IsReportedAsPermanentlyUnprocessable()
  {
    using var h = new Harness();

    var result = await h.Fulfillment.ProcessAsync(new PaymentCaptured(Guid.NewGuid(), "PS-X", Guid.NewGuid(), 1m, DateTimeOffset.UtcNow), default);

    Assert.Equal(FulfillmentOutcome.OrderNotFound, result.Outcome);
  }
}
