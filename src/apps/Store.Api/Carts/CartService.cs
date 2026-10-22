using Microsoft.EntityFrameworkCore;
using Store.Data;
using Store.Domain.Shopping;

namespace Store.Api.Carts;

public record CartItemDto(string Sku, string Name, string Brand, decimal UnitPrice, int Quantity, decimal LineTotal, int Available);

/// <summary>Prices are current catalog prices; they are snapshotted onto the order at checkout.</summary>
public record CartDto(IReadOnlyList<CartItemDto> Items, int ItemCount, decimal Subtotal);

public enum SetItemOutcome { Ok, ProductNotFound, InsufficientStock }

public class CartService(StoreDbContext db)
{
  public const int MaxQuantityPerItem = 20;

  public async Task<CartDto> GetAsync(string userId, CancellationToken ct)
  {
    var items = await db.CartItems.AsNoTracking()
      .Where(i => db.Carts.Any(c => c.Id == i.CartId && c.UserId == userId))
      .OrderBy(i => i.Product.Name)
      .Select(i => new
      {
        i.Product.Sku, i.Product.Name, i.Product.Brand, i.Product.Price, i.Quantity,
        Available = i.Product.Inventory == null ? 0 : Math.Max(i.Product.Inventory.QuantityOnHand - i.Product.Inventory.QuantityReserved, 0),
      })
      .ToListAsync(ct);

    var dtos = items.Select(i => new CartItemDto(i.Sku, i.Name, i.Brand, i.Price, i.Quantity, i.Price * i.Quantity, i.Available)).ToList();
    return new CartDto(dtos, dtos.Sum(i => i.Quantity), dtos.Sum(i => i.LineTotal));
  }

  public async Task<(SetItemOutcome Outcome, int Available)> SetItemAsync(string userId, string sku, int quantity, CancellationToken ct)
  {
    var product = await db.Products
      .Where(p => p.Sku == sku && p.IsActive)
      .Select(p => new
      {
        p.Id,
        Available = p.Inventory == null ? 0 : Math.Max(p.Inventory.QuantityOnHand - p.Inventory.QuantityReserved, 0),
      })
      .FirstOrDefaultAsync(ct);

    if (product is null) return (SetItemOutcome.ProductNotFound, 0);
    if (quantity > product.Available) return (SetItemOutcome.InsufficientStock, product.Available);

    var cart = await GetOrCreateCartAsync(userId, ct);
    var item = cart.Items.FirstOrDefault(i => i.ProductId == product.Id);
    if (item is null) cart.Items.Add(new CartItem { ProductId = product.Id, Quantity = quantity });
    else item.Quantity = quantity;

    cart.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return (SetItemOutcome.Ok, product.Available);
  }

  public async Task RemoveItemAsync(string userId, string sku, CancellationToken ct)
  {
    var cart = await db.Carts.Include(c => c.Items).ThenInclude(i => i.Product)
      .FirstOrDefaultAsync(c => c.UserId == userId, ct);
    var item = cart?.Items.FirstOrDefault(i => i.Product.Sku == sku);
    if (cart is null || item is null) return;

    cart.Items.Remove(item);
    cart.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
  }

  public async Task ClearAsync(string userId, CancellationToken ct)
  {
    var cart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId, ct);
    if (cart is null) return;

    cart.Items.Clear();
    cart.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
  }

  private async Task<Cart> GetOrCreateCartAsync(string userId, CancellationToken ct)
  {
    var cart = await db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId, ct);
    if (cart is not null) return cart;

    cart = new Cart { UserId = userId };
    db.Carts.Add(cart);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      // A concurrent request created this user's cart first (unique index on UserId); use theirs.
      db.Entry(cart).State = EntityState.Detached;
      cart = await db.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == userId, ct);
    }
    return cart;
  }
}
