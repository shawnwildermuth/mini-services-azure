using System.Security.Cryptography;
using System.Text;
using Store.Domain.Orders;

namespace Store.Functions.Shipping;

public record ShipmentRequest(Guid OrderId, string OrderNumber, string CustomerEmail, string CountryCode);

public record ShipmentLabel(string TrackingNumber, string Service, TimeSpan TransitTime);

/// <summary>The carrier is down or timed out. Transient: the message is retried, then dead-lettered.</summary>
public class CarrierUnavailableException(string message) : Exception(message);

/// <summary>
/// Books a shipment. Implementations must treat <see cref="ShipmentRequest.OrderId"/> as an
/// idempotency key: asking twice for the same order must return the same label.
/// </summary>
public interface IShippingCarrier
{
  Carrier Carrier { get; }
  Task<ShipmentLabel> CreateShipmentAsync(ShipmentRequest request, CancellationToken ct);
}

public abstract class SimulatedCarrier : IShippingCarrier
{
  public abstract Carrier Carrier { get; }

  public Task<ShipmentLabel> CreateShipmentAsync(ShipmentRequest request, CancellationToken ct)
  {
    // `name+shipfail@...` simulates a carrier outage.
    if (request.CustomerEmail.Contains("+shipfail", StringComparison.OrdinalIgnoreCase))
      throw new CarrierUnavailableException($"The (simulated) {Carrier} API is unavailable.");

    // Derived from the order id so a retried booking yields the same tracking number.
    return Task.FromResult(Book(request, SHA256.HashData(Encoding.UTF8.GetBytes(request.OrderId.ToString()))));
  }

  protected abstract ShipmentLabel Book(ShipmentRequest request, byte[] seed);
}

/// <summary>UPS for North America. Tracking numbers look like 1Z + 16 characters.</summary>
public class SimulatedUps : SimulatedCarrier
{
  private const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

  public override Carrier Carrier => Carrier.UPS;

  protected override ShipmentLabel Book(ShipmentRequest request, byte[] seed)
  {
    var shipper = new string(seed.Take(6).Select(b => Alphabet[b % Alphabet.Length]).ToArray());
    var serial = string.Concat(seed.Skip(6).Take(8).Select(b => (b % 10).ToString()));
    var mexico = request.CountryCode.Equals("MX", StringComparison.OrdinalIgnoreCase);

    return new ShipmentLabel(
      $"1Z{shipper}{(mexico ? "02" : "03")}{serial}",
      mexico ? "UPS Standard" : "UPS Ground",
      TimeSpan.FromDays(mexico ? 7 : 5));
  }
}

/// <summary>DHL for everywhere else. Waybill numbers are 10 digits.</summary>
public class SimulatedDhl : SimulatedCarrier
{
  public override Carrier Carrier => Carrier.DHL;

  protected override ShipmentLabel Book(ShipmentRequest request, byte[] seed) =>
    new(
      string.Concat(seed.Take(10).Select(b => (b % 10).ToString())),
      "DHL Express Worldwide",
      TimeSpan.FromDays(6));
}
