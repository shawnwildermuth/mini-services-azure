using Store.Domain.Catalog;

namespace Store.Domain.Shopping;

/// <summary>A signed-in customer's cart. <see cref="UserId"/> is the Identity user id.</summary>
public class Cart
{
  public int Id { get; set; }
  public required string UserId { get; set; }
  public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
  public List<CartItem> Items { get; set; } = [];
}

public class CartItem
{
  public int CartId { get; set; }
  public int ProductId { get; set; }
  public Product Product { get; set; } = null!;
  public int Quantity { get; set; }
}
