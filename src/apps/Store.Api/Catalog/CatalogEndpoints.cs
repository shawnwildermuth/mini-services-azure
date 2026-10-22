namespace Store.Api.Catalog;

public static class CatalogEndpoints
{
  public static IServiceCollection AddCatalog(this IServiceCollection services, IConfiguration configuration)
  {
    services.Configure<CatalogCacheOptions>(configuration.GetSection("Catalog:Cache"));
    return services.AddSingleton<CatalogCache>().AddScoped<CatalogService>();
  }

  public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api").WithTags("Catalog");

    group.MapGet("/categories", async (CatalogService catalog, CancellationToken ct) =>
      Results.Ok(await catalog.GetCategoriesAsync(ct)))
      .WithName("GetCategories")
      .Produces<List<CategoryDto>>();

    group.MapGet("/products", async ([AsParameters] ProductQuery query, CatalogService catalog, CancellationToken ct) =>
    {
      if (query.Sort is not null && !CatalogService.SortOptions.Contains(query.Sort))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
          ["sort"] = [$"Must be one of: {string.Join(", ", CatalogService.SortOptions)}."],
        });

      return Results.Ok(await catalog.GetProductsAsync(query, ct));
    })
    .WithName("GetProducts")
    .Produces<PagedResult<ProductSummaryDto>>()
    .ProducesValidationProblem();

    group.MapGet("/products/facets", async (string? category, CatalogService catalog, CancellationToken ct) =>
      Results.Ok(await catalog.GetFacetsAsync(category, ct)))
      .WithName("GetProductFacets")
      .Produces<FacetsDto>();

    group.MapGet("/products/{sku}", async (string sku, CatalogService catalog, CancellationToken ct) =>
      await catalog.GetProductAsync(sku, ct) is { } product ? Results.Ok(product) : Results.NotFound())
      .WithName("GetProduct")
      .Produces<ProductDetailDto>()
      .ProducesProblem(StatusCodes.Status404NotFound);

    group.MapGet("/products/{sku}/availability", async (string sku, CatalogService catalog, CancellationToken ct) =>
      await catalog.GetAvailabilityAsync(sku, ct) is { } availability ? Results.Ok(availability) : Results.NotFound())
      .WithName("GetProductAvailability")
      .Produces<AvailabilityDto>()
      .ProducesProblem(StatusCodes.Status404NotFound);

    return app;
  }
}
