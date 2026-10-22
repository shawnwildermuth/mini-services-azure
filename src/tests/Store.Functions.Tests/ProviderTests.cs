using Store.Domain.Orders;
using Store.Functions.Payments;
using Store.Functions.Shipping;

namespace Store.Functions.Tests;

public class ProviderTests
{
  private static PaymentRequest Charge(string email, Guid? orderId = null) => new(orderId ?? Guid.NewGuid(), "PS-1", 10m, "USD", email);
  private static ShipmentRequest Ship(string email, string country = "US", Guid? orderId = null) => new(orderId ?? Guid.NewGuid(), "PS-1", email, country);

  [Fact]
  public async Task MockPayments_ApproveByDefault_DeclineOnPlusDecline_AndFailOnPlusError()
  {
    var provider = new MockPaymentProvider();

    Assert.True((await provider.ChargeAsync(Charge("a@example.com"), default)).Approved);
    var declined = await provider.ChargeAsync(Charge("a+DECLINE@example.com"), default);
    Assert.False(declined.Approved);
    Assert.Equal("card_declined", declined.DeclineReason);
    await Assert.ThrowsAsync<PaymentProviderUnavailableException>(() => provider.ChargeAsync(Charge("a+error@example.com"), default));
  }

  [Fact]
  public async Task MockPayments_GiveTheSameReferenceForTheSameOrder()
  {
    var provider = new MockPaymentProvider();
    var order = Guid.NewGuid();

    var first = await provider.ChargeAsync(Charge("a@example.com", order), default);
    var again = await provider.ChargeAsync(Charge("a@example.com", order), default);
    var other = await provider.ChargeAsync(Charge("a@example.com"), default);

    Assert.Equal(first.Reference, again.Reference);
    Assert.NotEqual(first.Reference, other.Reference);
  }

  [Theory]
  [InlineData("US", "UPS Ground", 5)]
  [InlineData("CA", "UPS Ground", 5)]
  [InlineData("MX", "UPS Standard", 7)]
  public async Task Ups_BooksNorthAmericanShipments(string country, string service, int days)
  {
    var label = await new SimulatedUps().CreateShipmentAsync(Ship("a@example.com", country), default);

    Assert.Matches(@"^1Z[0-9A-Z]{6}0[23]\d{8}$", label.TrackingNumber);
    Assert.Equal(18, label.TrackingNumber.Length);
    Assert.Equal(service, label.Service);
    Assert.Equal(TimeSpan.FromDays(days), label.TransitTime);
  }

  [Fact]
  public async Task Carriers_ReturnTheSameLabelForTheSameOrder()
  {
    var order = Guid.NewGuid();
    foreach (IShippingCarrier carrier in new SimulatedCarrier[] { new SimulatedUps(), new SimulatedDhl() })
    {
      var first = await carrier.CreateShipmentAsync(Ship("a@example.com", orderId: order), default);
      var again = await carrier.CreateShipmentAsync(Ship("a@example.com", orderId: order), default);
      var other = await carrier.CreateShipmentAsync(Ship("a@example.com"), default);

      Assert.Equal(first.TrackingNumber, again.TrackingNumber);
      Assert.NotEqual(first.TrackingNumber, other.TrackingNumber);
    }
  }

  [Fact]
  public async Task Carriers_FailOnPlusShipfail()
  {
    await Assert.ThrowsAsync<CarrierUnavailableException>(() => new SimulatedUps().CreateShipmentAsync(Ship("a+shipfail@example.com"), default));
    await Assert.ThrowsAsync<CarrierUnavailableException>(() => new SimulatedDhl().CreateShipmentAsync(Ship("a+shipfail@example.com", "DE"), default));
  }

  [Fact]
  public void CarrierSelection_FollowsTheShippingPolicy()
  {
    Assert.Equal(Carrier.UPS, ShippingPolicy.CarrierFor("us"));
    Assert.Equal(Carrier.DHL, ShippingPolicy.CarrierFor("GB"));
  }
}
