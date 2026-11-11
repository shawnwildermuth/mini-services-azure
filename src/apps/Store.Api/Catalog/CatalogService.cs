using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Store.Data;

namespace Store.Api.Catalog;

public class CatalogService(StoreDbContext db, CatalogCache cache)
{
  public const int DefaultPageSize = 24;
  public const int MaxPageSize = 100;
  public static readonly string[] SortOptions = ["name", "-name", "price", "-price"];

  public Task<List<CategoryDto>?> GetCategoriesAsync(CancellationToken ct) =>
    cache.GetOrCreateAsync("categories", async token => await db.Categories
      .OrderBy(c => c.Name)
      .Select(c => new CategoryDto(c.Slug, c.Name, c.Products.Count(p => p.IsActive)))
      .ToListAsync(token), ct);

  public Task<PagedResult<ProductSummaryDto>?> GetProductsAsync(ProductQuery query, CancellationToken ct)
  {
    var page = Math.Max(query.Page ?? 1, 1);
    var pageSize = Math.Clamp(query.PageSize ?? DefaultPageSize, 1, MaxPageSize);
    var sort = query.Sort ?? "name";
    var specs = ParseSpecs(query.Spec);

    var key = CacheKey(query, specs, sort, page, pageSize);
    return cache.GetOrCreateAsync(key, token => QueryProductsAsync(query, specs, sort, page, pageSize, token), ct);
  }

  public Task<FacetsDto?> GetFacetsAsync(string? category, CancellationToken ct) =>
    cache.GetOrCreateAsync($"facets:{category}", async token =>
    {
      var products = db.Products.AsNoTracking().Where(p => p.IsActive);
      if (!string.IsNullOrWhiteSpace(category))
        products = products.Where(p => p.Category.Slug == category);

      var brands = await products.Select(p => p.Brand).Distinct().OrderBy(b => b).ToListAsync(token);
      var specRows = await db.ProductSpecs.AsNoTracking()
        .Where(s => products.Any(p => p.Id == s.ProductId))
        .Select(s => new { s.Name, s.Value })
        .Distinct()
        .ToListAsync(token);
      var range = await products.GroupBy(_ => 1)
        .Select(g => new { Min = g.Min(p => p.Price), Max = g.Max(p => p.Price) })
        .FirstOrDefaultAsync(token);

      var specs = specRows
        .GroupBy(s => s.Name)
        .OrderBy(g => g.Key)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(s => s.Value).Order().ToList());

      return new FacetsDto(brands, specs, range?.Min ?? 0, range?.Max ?? 0);
    }, ct);

  public Task<ProductDetailDto?> GetProductAsync(string sku, CancellationToken ct) =>
    cache.GetOrCreateAsync($"product:{sku.ToUpperInvariant()}", async token =>
    {
      var p = await db.Products.AsNoTracking()
        .Where(p => p.Sku == sku && p.IsActive)
        .Select(p => new
        {
          p.Sku, p.Name, p.Brand, p.Description, p.Price, p.ImageUrl,
          CategorySlug = p.Category.Slug, CategoryName = p.Category.Name,
          Specs = p.Specs.OrderBy(s => s.Name).Select(s => new { s.Name, s.Value }).ToList(),
        })
        .FirstOrDefaultAsync(token);

      return p is null
        ? null
        : new ProductDetailDto(p.Sku, p.Name, p.Brand, p.Description, p.Price, p.CategorySlug, p.CategoryName,
            p.Specs.ToDictionary(s => s.Name, s => s.Value), p.ImageUrl);
    }, ct);

  /// <summary>Stock levels change constantly, so this is intentionally never cached.</summary>
  public Task<AvailabilityDto?> GetAvailabilityAsync(string sku, CancellationToken ct) =>
    db.Products.AsNoTracking()
      .Where(p => p.Sku == sku && p.IsActive)
      .Select(p => new AvailabilityDto(
        p.Sku,
        p.Inventory != null && p.Inventory.QuantityOnHand - p.Inventory.QuantityReserved > 0,
        p.Inventory == null ? 0 : Math.Max(p.Inventory.QuantityOnHand - p.Inventory.QuantityReserved, 0)))
      .FirstOrDefaultAsync(ct);

  private async Task<PagedResult<ProductSummaryDto>?> QueryProductsAsync(
    ProductQuery query, ILookup<string, string> specs, string sort, int page, int pageSize, CancellationToken ct)
  {
    var products = db.Products.AsNoTracking().Where(p => p.IsActive);

    if (!string.IsNullOrWhiteSpace(query.Category))
      products = products.Where(p => p.Category.Slug == query.Category);
    if (!string.IsNullOrWhiteSpace(query.Brand))
      products = products.Where(p => p.Brand == query.Brand);
    if (!string.IsNullOrWhiteSpace(query.Q))
      products = products.Where(p => p.Name.Contains(query.Q) || p.Brand.Contains(query.Q) || p.Sku.Contains(query.Q));
    if (query.MinPrice is { } min)
      products = products.Where(p => p.Price >= min);
    if (query.MaxPrice is { } max)
      products = products.Where(p => p.Price <= max);

    // Different spec names are ANDed; multiple values for the same name are ORed.
    foreach (var group in specs)
    {
      var name = group.Key;
      var values = group.ToList();
      products = products.Where(p => p.Specs.Any(s => s.Name == name && values.Contains(s.Value)));
    }

    var total = await products.CountAsync(ct);

    products = sort switch
    {
      "-name" => products.OrderByDescending(p => p.Name),
      "price" => products.OrderBy(p => p.Price).ThenBy(p => p.Name),
      "-price" => products.OrderByDescending(p => p.Price).ThenBy(p => p.Name),
      _ => products.OrderBy(p => p.Name),
    };

    var items = await products
      .Skip((page - 1) * pageSize)
      .Take(pageSize)
      .Select(p => new ProductSummaryDto(p.Sku, p.Name, p.Brand, p.Price, p.Category.Slug, p.ImageUrl))
      .ToListAsync(ct);

    return new PagedResult<ProductSummaryDto>(items, page, pageSize, total);
  }

  /// <summary>Parses "name:value" pairs; malformed entries are ignored.</summary>
  public static ILookup<string, string> ParseSpecs(string[]? specs) =>
    (specs ?? [])
      .Select(s => s.Split(':', 2))
      .Where(p => p.Length == 2 && p[0].Length > 0 && p[1].Length > 0)
      .ToLookup(p => p[0], p => p[1]);

  private static string CacheKey(ProductQuery q, ILookup<string, string> specs, string sort, int page, int pageSize)
  {
    var specKey = string.Join(",", specs.OrderBy(g => g.Key).SelectMany(g => g.Order().Select(v => $"{g.Key}:{v}")));
    var raw = string.Join("|", q.Category, q.Q, q.Brand, q.MinPrice, q.MaxPrice, specKey, sort, page, pageSize);
    return "products:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
  }
}
