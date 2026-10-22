using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Store.Api.Catalog;
using Store.Data;
using Store.Domain.Catalog;

namespace Store.Api.Admin;

public static class AdminCatalogEndpoints
{
  public const int LowStockThreshold = 5;

  private static IResult Invalid(string field, string message) =>
    Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

  private static IResult Conflict(string message) =>
    Results.Problem(statusCode: StatusCodes.Status409Conflict, title: message);

  public static void MapAdminCatalogEndpoints(this RouteGroupBuilder admin)
  {
    MapProducts(admin);
    MapCategories(admin);
    MapInventory(admin);
  }

  // ---------------------------------------------------------------- products

  private static void MapProducts(RouteGroupBuilder admin)
  {
    var group = admin.MapGroup("/products").WithTags("Admin: Products");

    group.MapGet("/", async (string? q, string? category, string? status, int? page, int? pageSize, StoreDbContext db, CancellationToken ct) =>
    {
      var p = Math.Max(page ?? 1, 1);
      var size = Math.Clamp(pageSize ?? 20, 1, 100);

      var query = db.Products.AsNoTracking().AsQueryable();
      if (!string.IsNullOrWhiteSpace(q))
        query = query.Where(x => x.Name.Contains(q) || x.Sku.Contains(q) || x.Brand.Contains(q));
      if (!string.IsNullOrWhiteSpace(category))
        query = query.Where(x => x.Category.Slug == category);
      query = status switch
      {
        "active" => query.Where(x => x.IsActive),
        "inactive" => query.Where(x => !x.IsActive),
        _ => query,
      };

      var total = await query.CountAsync(ct);
      var items = await query.OrderBy(x => x.Name).Skip((p - 1) * size).Take(size)
        .Select(x => new AdminProductSummaryDto(
          x.Id, x.Sku, x.Name, x.Brand, x.Price, x.Category.Name, x.IsActive,
          x.Inventory == null ? 0 : Math.Max(x.Inventory.QuantityOnHand - x.Inventory.QuantityReserved, 0)))
        .ToListAsync(ct);

      return Results.Ok(new PagedResult<AdminProductSummaryDto>(items, p, size, total));
    })
    .WithName("AdminListProducts")
    .Produces<PagedResult<AdminProductSummaryDto>>();

    group.MapGet("/{id:int}", async (int id, StoreDbContext db, CancellationToken ct) =>
      await LoadProductAsync(db, id, ct) is { } product ? Results.Ok(product) : Results.NotFound())
      .WithName("AdminGetProduct")
      .Produces<AdminProductDto>()
      .ProducesProblem(StatusCodes.Status404NotFound);

    group.MapPost("/", async (CreateProductRequest request, ClaimsPrincipal user, ILoggerFactory logs, StoreDbContext db, CatalogCache cache, CancellationToken ct) =>
    {
      if (DuplicateSpec(request.Specs) is { } dup) return Invalid("specs", $"Specification '{dup}' is listed more than once.");
      if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, ct)) return Invalid("categoryId", "Choose an existing category.");
      var sku = request.Sku.Trim();
      if (await db.Products.AnyAsync(p => p.Sku == sku, ct)) return Invalid("sku", "That SKU is already in use.");

      var product = new Product
      {
        Sku = sku,
        Name = request.Name.Trim(),
        Brand = request.Brand.Trim(),
        Description = request.Description.Trim(),
        Price = request.Price,
        CategoryId = request.CategoryId,
        IsActive = request.IsActive,
        Specs = request.Specs.Select(s => new ProductSpec { Name = s.Name.Trim(), Value = s.Value.Trim() }).ToList(),
        Inventory = new Inventory { QuantityOnHand = request.InitialStock },
      };
      db.Products.Add(product);

      try
      {
        await db.SaveChangesAsync(ct);
      }
      catch (DbUpdateException)
      {
        return Invalid("sku", "That SKU is already in use."); // lost a race on the unique index
      }

      await cache.InvalidateAsync(ct);
      logs.AdminAction(user, "created product", product.Sku);
      return Results.Created($"/api/admin/products/{product.Id}", await LoadProductAsync(db, product.Id, ct));
    })
    .WithName("AdminCreateProduct")
    .Produces<AdminProductDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem();

    group.MapPut("/{id:int}", async (int id, ProductRequest request, ClaimsPrincipal user, ILoggerFactory logs, StoreDbContext db, CatalogCache cache, CancellationToken ct) =>
    {
      if (DuplicateSpec(request.Specs) is { } dup) return Invalid("specs", $"Specification '{dup}' is listed more than once.");
      var product = await db.Products.Include(p => p.Specs).FirstOrDefaultAsync(p => p.Id == id, ct);
      if (product is null) return Results.NotFound();
      if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId, ct)) return Invalid("categoryId", "Choose an existing category.");

      product.Name = request.Name.Trim();
      product.Brand = request.Brand.Trim();
      product.Description = request.Description.Trim();
      product.Price = request.Price;
      product.CategoryId = request.CategoryId;
      product.IsActive = request.IsActive;

      // Diff the specs rather than replacing them: (product, name) is the key, and delete+insert of the same key can't share a save.
      var wanted = request.Specs.ToDictionary(s => s.Name.Trim(), s => s.Value.Trim(), StringComparer.OrdinalIgnoreCase);
      foreach (var existing in product.Specs.ToList())
      {
        if (wanted.TryGetValue(existing.Name, out var value)) { existing.Value = value; wanted.Remove(existing.Name); }
        else db.ProductSpecs.Remove(existing);
      }
      foreach (var (name, value) in wanted)
        product.Specs.Add(new ProductSpec { Name = name, Value = value });

      await db.SaveChangesAsync(ct);
      await cache.InvalidateAsync(ct);
      logs.AdminAction(user, "updated product", $"{product.Sku} (price {product.Price}, active {product.IsActive})");
      return Results.Ok(await LoadProductAsync(db, id, ct));
    })
    .WithName("AdminUpdateProduct")
    .Produces<AdminProductDto>()
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status404NotFound);
  }

  private static string? DuplicateSpec(IEnumerable<SpecDto> specs) =>
    specs.GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1)?.Key;

  private static Task<AdminProductDto?> LoadProductAsync(StoreDbContext db, int id, CancellationToken ct) =>
    db.Products.AsNoTracking().Where(p => p.Id == id)
      .Select(p => new AdminProductDto(
        p.Id, p.Sku, p.Name, p.Brand, p.Description, p.Price, p.CategoryId, p.IsActive,
        p.Specs.OrderBy(s => s.Name).Select(s => new SpecDto { Name = s.Name, Value = s.Value }).ToList(),
        p.Inventory == null ? 0 : p.Inventory.QuantityOnHand,
        p.Inventory == null ? 0 : p.Inventory.QuantityReserved))
      .FirstOrDefaultAsync(ct);

  // -------------------------------------------------------------- categories

  private static void MapCategories(RouteGroupBuilder admin)
  {
    var group = admin.MapGroup("/categories").WithTags("Admin: Categories");

    group.MapGet("/", async (StoreDbContext db, CancellationToken ct) =>
      Results.Ok(await db.Categories.AsNoTracking().OrderBy(c => c.Name)
        .Select(c => new AdminCategoryDto(c.Id, c.Slug, c.Name, c.Products.Count))
        .ToListAsync(ct)))
      .WithName("AdminListCategories")
      .Produces<List<AdminCategoryDto>>();

    group.MapPost("/", async (CategoryRequest request, ClaimsPrincipal user, ILoggerFactory logs, StoreDbContext db, CatalogCache cache, CancellationToken ct) =>
    {
      if (await db.Categories.AnyAsync(c => c.Slug == request.Slug, ct)) return Invalid("slug", "That slug is already in use.");

      var category = new Category { Slug = request.Slug, Name = request.Name.Trim() };
      db.Categories.Add(category);
      await db.SaveChangesAsync(ct);
      await cache.InvalidateAsync(ct);
      logs.AdminAction(user, "created category", category.Slug);
      return Results.Created($"/api/admin/categories/{category.Id}", new AdminCategoryDto(category.Id, category.Slug, category.Name, 0));
    })
    .WithName("AdminCreateCategory")
    .Produces<AdminCategoryDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem();

    group.MapPut("/{id:int}", async (int id, CategoryRequest request, ClaimsPrincipal user, ILoggerFactory logs, StoreDbContext db, CatalogCache cache, CancellationToken ct) =>
    {
      var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
      if (category is null) return Results.NotFound();
      if (await db.Categories.AnyAsync(c => c.Slug == request.Slug && c.Id != id, ct)) return Invalid("slug", "That slug is already in use.");

      category.Name = request.Name.Trim();
      category.Slug = request.Slug;
      await db.SaveChangesAsync(ct);
      await cache.InvalidateAsync(ct);
      logs.AdminAction(user, "updated category", category.Slug);
      return Results.Ok(new AdminCategoryDto(category.Id, category.Slug, category.Name, await db.Products.CountAsync(p => p.CategoryId == id, ct)));
    })
    .WithName("AdminUpdateCategory")
    .Produces<AdminCategoryDto>()
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status404NotFound);

    group.MapDelete("/{id:int}", async (int id, ClaimsPrincipal user, ILoggerFactory logs, StoreDbContext db, CatalogCache cache, CancellationToken ct) =>
    {
      var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
      if (category is null) return Results.NotFound();
      if (await db.Products.AnyAsync(p => p.CategoryId == id, ct))
        return Conflict("This category still has products. Move or deactivate them first.");

      db.Categories.Remove(category);
      await db.SaveChangesAsync(ct);
      await cache.InvalidateAsync(ct);
      logs.AdminAction(user, "deleted category", category.Slug);
      return Results.NoContent();
    })
    .WithName("AdminDeleteCategory")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);
  }

  // --------------------------------------------------------------- inventory

  private static void MapInventory(RouteGroupBuilder admin)
  {
    var group = admin.MapGroup("/inventory").WithTags("Admin: Inventory");

    group.MapGet("/", async (string? q, bool? lowStock, int? page, int? pageSize, StoreDbContext db, CancellationToken ct) =>
    {
      var p = Math.Max(page ?? 1, 1);
      var size = Math.Clamp(pageSize ?? 25, 1, 100);

      var query = db.Inventory.AsNoTracking().AsQueryable();
      if (!string.IsNullOrWhiteSpace(q))
        query = query.Where(i => db.Products.Any(x => x.Id == i.ProductId && (x.Name.Contains(q) || x.Sku.Contains(q))));
      if (lowStock == true)
        query = query.Where(i => i.QuantityOnHand - i.QuantityReserved <= LowStockThreshold);

      var total = await query.CountAsync(ct);
      var items = await query
        .Join(db.Products, i => i.ProductId, x => x.Id, (i, x) => new { i, x })
        .OrderBy(r => r.i.QuantityOnHand - r.i.QuantityReserved).ThenBy(r => r.x.Name)
        .Skip((p - 1) * size).Take(size)
        .Select(r => new InventoryItemDto(
          r.x.Id, r.x.Sku, r.x.Name, r.i.QuantityOnHand, r.i.QuantityReserved,
          Math.Max(r.i.QuantityOnHand - r.i.QuantityReserved, 0)))
        .ToListAsync(ct);

      return Results.Ok(new PagedResult<InventoryItemDto>(items, p, size, total));
    })
    .WithName("AdminListInventory")
    .Produces<PagedResult<InventoryItemDto>>();

    group.MapPut("/{productId:int}", async (int productId, SetStockRequest request, ClaimsPrincipal user, ILoggerFactory logs, StoreDbContext db, CancellationToken ct) =>
    {
      var stock = await db.Inventory.FirstOrDefaultAsync(i => i.ProductId == productId, ct);
      if (stock is null) return Results.NotFound();
      if (request.QuantityOnHand < stock.QuantityReserved)
        return Invalid(nameof(request.QuantityOnHand), $"Cannot go below the {stock.QuantityReserved} units reserved for open orders.");

      stock.QuantityOnHand = request.QuantityOnHand;
      try
      {
        await db.SaveChangesAsync(ct);
      }
      catch (DbUpdateConcurrencyException)
      {
        return Conflict("Stock changed while you were editing it. Reload and try again.");
      }

      var name = await db.Products.Where(p => p.Id == productId).Select(p => new { p.Sku, p.Name }).FirstAsync(ct);
      logs.AdminAction(user, "set stock on hand", $"{name.Sku} = {stock.QuantityOnHand}");
      return Results.Ok(new InventoryItemDto(
        productId, name.Sku, name.Name, stock.QuantityOnHand, stock.QuantityReserved, Math.Max(stock.QuantityOnHand - stock.QuantityReserved, 0)));
    })
    .WithName("AdminSetStock")
    .Produces<InventoryItemDto>()
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);
  }
}
