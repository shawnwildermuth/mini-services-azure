using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Store.Api.Catalog;
using Store.Data;

namespace Store.Api.Tests;

public class CatalogApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private readonly HttpClient _client = factory.CreateClient();

  private Task<PagedResult<ProductSummaryDto>?> Products(string query = "") =>
    _client.GetFromJsonAsync<PagedResult<ProductSummaryDto>>("/api/products" + query);

  [Fact]
  public async Task Categories_ReturnsSeededCategoriesWithCounts()
  {
    var categories = await _client.GetFromJsonAsync<List<CategoryDto>>("/api/categories");

    Assert.Equal(8, categories!.Count);
    Assert.All(categories, c => Assert.True(c.ProductCount > 0));
  }

  [Fact]
  public async Task Products_DefaultsToFirstPageSortedByName()
  {
    var result = await Products();

    Assert.Equal(1, result!.Page);
    Assert.Equal(CatalogService.DefaultPageSize, result.Items.Count);
    Assert.True(result.TotalCount >= 48);
    Assert.Equal(result.Items.OrderBy(p => p.Name).Select(p => p.Sku), result.Items.Select(p => p.Sku));
  }

  [Fact]
  public async Task Products_PagesWithoutOverlap()
  {
    var first = await Products("?pageSize=10&page=1");
    var second = await Products("?pageSize=10&page=2");

    Assert.Empty(first!.Items.Select(p => p.Sku).Intersect(second!.Items.Select(p => p.Sku)));
  }

  [Fact]
  public async Task Products_FiltersByCategory()
  {
    var result = await Products("?category=gpus");

    Assert.NotEmpty(result!.Items);
    Assert.All(result.Items, p => Assert.Equal("gpus", p.CategorySlug));
  }

  [Fact]
  public async Task Products_FiltersByPriceRangeAndSortsDescending()
  {
    var result = await Products("?minPrice=100&maxPrice=300&sort=-price");

    Assert.All(result!.Items, p => Assert.InRange(p.Price, 100m, 300m));
    Assert.Equal(result.Items.OrderByDescending(p => p.Price).Select(p => p.Price), result.Items.Select(p => p.Price));
  }

  [Fact]
  public async Task Products_SearchMatchesNameOrBrand()
  {
    var result = await Products("?q=Corsair");

    Assert.NotEmpty(result!.Items);
    Assert.All(result.Items, p => Assert.Contains("Corsair", p.Brand + p.Name + p.Sku));
  }

  [Fact]
  public async Task Products_SpecFilters_AndAcrossNamesOrWithinName()
  {
    var am5 = await Products("?category=motherboards&spec=socket:AM5");
    var am5Atx = await Products("?category=motherboards&spec=socket:AM5&spec=formFactor:ATX");
    var either = await Products("?category=motherboards&spec=socket:AM5&spec=socket:LGA1700");

    Assert.NotEmpty(am5!.Items);
    Assert.InRange(am5Atx!.TotalCount, 1, am5.TotalCount - 1);
    Assert.True(either!.TotalCount > am5.TotalCount);
  }

  [Fact]
  public async Task Products_RejectsUnknownSort()
  {
    var response = await _client.GetAsync("/api/products?sort=popularity");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Products_ClampsPageSize()
  {
    var result = await Products("?pageSize=5000");

    Assert.Equal(CatalogService.MaxPageSize, result!.PageSize);
  }

  [Fact]
  public async Task Product_ReturnsDetailWithSpecs()
  {
    var product = await _client.GetFromJsonAsync<ProductDetailDto>("/api/products/CPU-AMD-9950X");

    Assert.Equal("Ryzen 9 9950X", product!.Name);
    Assert.Equal("AM5", product.Specs["socket"]);
  }

  [Fact]
  public async Task Product_UnknownSkuIsNotFound()
  {
    Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/products/NOPE")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/products/NOPE/availability")).StatusCode);
  }

  [Fact]
  public async Task Availability_ReflectsStock()
  {
    var inStock = await _client.GetFromJsonAsync<AvailabilityDto>("/api/products/CPU-AMD-9950X/availability");
    var outOfStock = await _client.GetFromJsonAsync<AvailabilityDto>("/api/products/CPU-INT-14600K/availability");

    Assert.True(inStock!.InStock);
    Assert.False(outOfStock!.InStock);
  }

  [Fact]
  public async Task Product_IsServedFromCache_UntilInvalidated()
  {
    const string sku = "CPU-AMD-9600X";
    var before = await _client.GetFromJsonAsync<ProductDetailDto>($"/api/products/{sku}");

    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
      db.Products.Single(p => p.Sku == sku).Price = 1m;
      await db.SaveChangesAsync();
    }

    var cached = await _client.GetFromJsonAsync<ProductDetailDto>($"/api/products/{sku}");
    Assert.Equal(before!.Price, cached!.Price);

    await factory.Services.GetRequiredService<CatalogCache>().InvalidateAsync();

    var fresh = await _client.GetFromJsonAsync<ProductDetailDto>($"/api/products/{sku}");
    Assert.Equal(1m, fresh!.Price);
  }

  [Fact]
  public async Task CacheCanBeSwitchedOff_SoEveryReadSeesTheDatabase()
  {
    using var noCache = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.Configure<Store.Api.Catalog.CatalogCacheOptions>(o => o.Enabled = false)));
    var client = noCache.CreateClient();
    const string sku = "MB-MSI-B650";
    var before = await client.GetFromJsonAsync<ProductDetailDto>($"/api/products/{sku}");

    using (var scope = noCache.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
      db.Products.Single(p => p.Sku == sku).Price = before!.Price + 1m;
      await db.SaveChangesAsync(); // no cache invalidation at all
    }

    var after = await client.GetFromJsonAsync<ProductDetailDto>($"/api/products/{sku}");
    Assert.Equal(before!.Price + 1m, after!.Price);
  }

  [Fact]
  public async Task Facets_ListBrandsSpecsAndPriceRange_ForACategory()
  {
    var facets = await _client.GetFromJsonAsync<FacetsDto>("/api/products/facets?category=motherboards");

    Assert.Contains("ASUS", facets!.Brands);
    Assert.DoesNotContain("Corsair", facets.Brands);
    Assert.Equal(new[] { "AM5", "LGA1700", "LGA1851" }, facets.Specs["socket"].Order());
    Assert.Equal(99m, facets.MinPrice);
    Assert.Equal(499m, facets.MaxPrice);
  }

  [Fact]
  public async Task Facets_WithoutCategory_CoverEverything()
  {
    var facets = await _client.GetFromJsonAsync<FacetsDto>("/api/products/facets");

    Assert.True(facets!.Specs.Count > 5);
    Assert.True(facets.MaxPrice >= 1999m);
  }

  [Fact]
  public async Task Health_LiveAndReady()
  {
    Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/live")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/ready")).StatusCode);
  }
}
