using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Store.Data;
using Store.Domain.Messaging;
using Store.Domain.Orders;

namespace Store.Api.Orders;

public enum CheckoutOutcome { Created, Existing, EmptyCart, AddressNotFound, InsufficientStock }

public record StockIssue(string Sku, string Name, int Requested, int Available);

public record CheckoutResult(CheckoutOutcome Outcome, Order? Order = null, IReadOnlyList<StockIssue>? StockIssues = null);

public class CheckoutService(StoreDbContext db, TimeProvider clock)
{
  private const int MaxAttempts = 3;

  /// <summary>Prices the current cart for an address. Null if the address isn't the user's.</summary>
  public async Task<QuoteDto?> QuoteAsync(string userId, int addressId, CancellationToken ct)
  {
    var country = await db.Addresses.AsNoTracking()
      .Where(a => a.Id == addressId && a.UserId == userId)
      .Select(a => a.CountryCode)
      .FirstOrDefaultAsync(ct);
    if (country is null) return null;

    var subtotal = await db.CartItems.AsNoTracking()
      .Where(i => db.Carts.Any(c => c.Id == i.CartId && c.UserId == userId))
      .SumAsync(i => i.Product.Price * i.Quantity, ct);

    var carrier = ShippingPolicy.CarrierFor(country);
    var shipping = ShippingPolicy.RateFor(carrier);
    return new QuoteDto(subtotal, shipping, subtotal + shipping, carrier);
  }

  /// <summary>
  /// Turns the user's cart into an order. In ONE transaction: snapshot prices and address, reserve
  /// stock, empty the cart, and queue an OrderPlaced message in the outbox. Retrying with the same
  /// idempotency key returns the original order instead of creating another.
  /// </summary>
  public async Task<CheckoutResult> PlaceOrderAsync(
    string userId, string email, int addressId, string? idempotencyKey, CancellationToken ct)
  {
    for (var attempt = 1; ; attempt++)
    {
      try
      {
        return await TryPlaceOrderAsync(userId, email, addressId, idempotencyKey, ct);
      }
      catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
      {
        // Someone else changed the same stock rows between our read and write; re-read and re-check.
        db.ChangeTracker.Clear();
      }
      catch (DbUpdateException) when (idempotencyKey is not null)
      {
        // A concurrent request with the same key won the unique index; return its order.
        db.ChangeTracker.Clear();
        var existing = await FindExistingAsync(userId, idempotencyKey, ct);
        if (existing is not null) return new CheckoutResult(CheckoutOutcome.Existing, existing);
        throw;
      }
    }
  }

  private async Task<CheckoutResult> TryPlaceOrderAsync(
    string userId, string email, int addressId, string? idempotencyKey, CancellationToken ct)
  {
    if (idempotencyKey is not null && await FindExistingAsync(userId, idempotencyKey, ct) is { } existing)
      return new CheckoutResult(CheckoutOutcome.Existing, existing);

    var address = await db.Addresses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == userId, ct);
    if (address is null) return new CheckoutResult(CheckoutOutcome.AddressNotFound);

    var cart = await db.Carts
      .Include(c => c.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Inventory)
      .FirstOrDefaultAsync(c => c.UserId == userId, ct);
    if (cart is null || cart.Items.Count == 0) return new CheckoutResult(CheckoutOutcome.EmptyCart);

    var issues = cart.Items
      .Select(i => new StockIssue(i.Product.Sku, i.Product.Name, i.Quantity,
        i.Product.IsActive && i.Product.Inventory is { } inv ? Math.Max(inv.QuantityOnHand - inv.QuantityReserved, 0) : 0))
      .Where(s => s.Requested > s.Available)
      .ToList();
    if (issues.Count > 0) return new CheckoutResult(CheckoutOutcome.InsufficientStock, StockIssues: issues);

    var now = clock.GetUtcNow();
    var carrier = ShippingPolicy.CarrierFor(address.CountryCode);
    var items = cart.Items.Select(i => new OrderItem
    {
      ProductId = i.ProductId,
      Sku = i.Product.Sku,
      Name = i.Product.Name,
      UnitPrice = i.Product.Price,
      Quantity = i.Quantity,
    }).ToList();
    var subtotal = items.Sum(i => i.LineTotal);
    var shipping = ShippingPolicy.RateFor(carrier);

    var order = new Order
    {
      OrderNumber = NewOrderNumber(now),
      UserId = userId,
      CustomerEmail = email,
      CreatedAt = now,
      UpdatedAt = now,
      Subtotal = subtotal,
      ShippingCost = shipping,
      Total = subtotal + shipping,
      Carrier = carrier,
      ShipToName = address.FullName,
      ShipToLine1 = address.Line1,
      ShipToLine2 = address.Line2,
      ShipToCity = address.City,
      ShipToRegion = address.Region,
      ShipToPostalCode = address.PostalCode,
      ShipToCountryCode = address.CountryCode,
      ShipToPhone = address.Phone,
      IdempotencyKey = idempotencyKey,
      Items = items,
    };
    db.Orders.Add(order);

    foreach (var line in cart.Items)
      line.Product.Inventory!.QuantityReserved += line.Quantity;

    cart.Items.Clear();
    cart.UpdatedAt = now;

    db.OutboxMessages.Add(new OutboxMessage
    {
      Destination = Queues.OrderPlaced,
      Type = nameof(OrderPlaced),
      Payload = JsonSerializer.Serialize(
        new OrderPlaced(order.Id, order.OrderNumber, email, order.Total, "USD", now), JsonSerializerOptions.Web),
      CreatedAt = now,
      NextAttemptAt = now,
    });

    await db.SaveChangesAsync(ct); // order, reservation, cleared cart and outbox row commit together
    return new CheckoutResult(CheckoutOutcome.Created, order);
  }

  private Task<Order?> FindExistingAsync(string userId, string key, CancellationToken ct) =>
    db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.UserId == userId && o.IdempotencyKey == key, ct);

  private static string NewOrderNumber(DateTimeOffset now) =>
    $"PS-{now:yyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3))}";
}
