namespace Store.Domain.Catalog;

public class Category
{
  public int Id { get; set; }
  public required string Slug { get; set; }
  public required string Name { get; set; }
  public List<Product> Products { get; set; } = [];
}

public class Product
{
  public int Id { get; set; }
  public required string Sku { get; set; }
  public required string Name { get; set; }
  public required string Brand { get; set; }
  public string Description { get; set; } = "";
  public decimal Price { get; set; }
  public bool IsActive { get; set; } = true;
  public int CategoryId { get; set; }
  public Category Category { get; set; } = null!;
  public List<ProductSpec> Specs { get; set; } = [];
  public Inventory? Inventory { get; set; }
}

/// <summary>A searchable attribute of a product, e.g. socket = AM5.</summary>
public class ProductSpec
{
  public int ProductId { get; set; }
  public required string Name { get; set; }
  public required string Value { get; set; }
}

public class Inventory
{
  public int ProductId { get; set; }
  public int QuantityOnHand { get; set; }
  /// <summary>Held for pending orders. Checkout adds to it; payment failure releases it; shipping consumes it.</summary>
  public int QuantityReserved { get; set; }
  public int Available => QuantityOnHand - QuantityReserved;
  /// <summary>Concurrency token: two checkouts racing for the last unit can't both succeed.</summary>
  public byte[] RowVersion { get; set; } = [];
}
