using Microsoft.AspNetCore.Mvc;

namespace Store.Api.Catalog;

public record CategoryDto(string Slug, string Name, int ProductCount);

public record ProductSummaryDto(string Sku, string Name, string Brand, decimal Price, string CategorySlug, string? ImageUrl);

public record ProductDetailDto(
  string Sku, string Name, string Brand, string Description, decimal Price,
  string CategorySlug, string CategoryName, IReadOnlyDictionary<string, string> Specs, string? ImageUrl);

/// <summary>Filter options for the product list: brands, spec values by spec name, and the price range.</summary>
public record FacetsDto(
  IReadOnlyList<string> Brands, IReadOnlyDictionary<string, IReadOnlyList<string>> Specs, decimal MinPrice, decimal MaxPrice);

public record AvailabilityDto(string Sku, bool InStock, int Available);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Query-string parameters for the product list. <c>Spec</c> values look like <c>socket:AM5</c>.</summary>
public record ProductQuery(
  [FromQuery(Name = "category")] string? Category,
  [FromQuery(Name = "q")] string? Q,
  [FromQuery(Name = "brand")] string? Brand,
  [FromQuery(Name = "minPrice")] decimal? MinPrice,
  [FromQuery(Name = "maxPrice")] decimal? MaxPrice,
  [FromQuery(Name = "spec")] string[]? Spec,
  [FromQuery(Name = "sort")] string? Sort,
  [FromQuery(Name = "page")] int? Page,
  [FromQuery(Name = "pageSize")] int? PageSize);
