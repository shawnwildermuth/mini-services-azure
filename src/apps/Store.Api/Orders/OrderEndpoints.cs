using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Store.Api.Auth;
using Store.Api.Catalog;
using Store.Data;

namespace Store.Api.Orders;

public static class OrderEndpoints
{
  public static IServiceCollection AddOrders(this IServiceCollection services) =>
    services.AddSingleton(TimeProvider.System).AddScoped<CheckoutService>();

  public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api").WithTags("Orders").RequireAuthorization();

    group.MapGet("/checkout/quote", async (int addressId, ClaimsPrincipal user, CheckoutService checkout, CancellationToken ct) =>
      await checkout.QuoteAsync(user.GetUserId(), addressId, ct) is { } quote ? Results.Ok(quote) : Results.NotFound())
      .WithName("GetCheckoutQuote")
      .Produces<QuoteDto>()
      .ProducesProblem(StatusCodes.Status404NotFound);

    group.MapPost("/orders", async (
      PlaceOrderRequest request,
      [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
      ClaimsPrincipal user, CheckoutService checkout, CancellationToken ct) =>
    {
      if (idempotencyKey is { Length: > 64 })
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["Idempotency-Key"] = ["Must be 64 characters or fewer."] });

      var result = await checkout.PlaceOrderAsync(
        user.GetUserId(), user.GetEmail(), request.AddressId, string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey, ct);

      return result.Outcome switch
      {
        CheckoutOutcome.Created => Results.Created($"/api/orders/{result.Order!.Id}", result.Order.ToDetail()),
        CheckoutOutcome.Existing => Results.Ok(result.Order!.ToDetail()),
        CheckoutOutcome.EmptyCart => Results.ValidationProblem(new Dictionary<string, string[]> { ["cart"] = ["Your cart is empty."] }),
        CheckoutOutcome.AddressNotFound => Results.ValidationProblem(new Dictionary<string, string[]> { ["addressId"] = ["Choose a shipping address."] }),
        _ => Results.Json(
          new HttpValidationProblemDetails(result.StockIssues!.ToDictionary(
            s => s.Sku,
            s => new[] { s.Available == 0 ? $"{s.Name} is out of stock." : $"{s.Name}: only {s.Available} available." }))
          {
            Status = StatusCodes.Status409Conflict,
            Title = "Some items are no longer available in the quantity you chose.",
          },
          statusCode: StatusCodes.Status409Conflict),
      };
    })
    .WithName("PlaceOrder")
    .Produces<OrderDetailDto>(StatusCodes.Status201Created)
    .Produces<OrderDetailDto>()
    .ProducesValidationProblem()
    .ProducesValidationProblem(StatusCodes.Status409Conflict);

    group.MapGet("/orders", async (int? page, int? pageSize, ClaimsPrincipal user, StoreDbContext db, CancellationToken ct) =>
    {
      var userId = user.GetUserId();
      var p = Math.Max(page ?? 1, 1);
      var size = Math.Clamp(pageSize ?? 10, 1, 50);

      var query = db.Orders.AsNoTracking().Where(o => o.UserId == userId);
      var total = await query.CountAsync(ct);
      var items = await query
        .OrderByDescending(o => o.CreatedAt)
        .Skip((p - 1) * size).Take(size)
        .Select(o => new OrderSummaryDto(o.Id, o.OrderNumber, o.Status, o.CreatedAt, o.Total, o.Items.Sum(i => i.Quantity)))
        .ToListAsync(ct);

      return Results.Ok(new PagedResult<OrderSummaryDto>(items, p, size, total));
    })
    .WithName("GetOrders")
    .Produces<PagedResult<OrderSummaryDto>>();

    group.MapGet("/orders/{id:guid}", async (Guid id, ClaimsPrincipal user, StoreDbContext db, CancellationToken ct) =>
    {
      var userId = user.GetUserId();
      var order = await db.Orders.AsNoTracking().Include(o => o.Items)
        .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId, ct);
      if (order is null) return Results.NotFound();

      var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.OrderId == id, ct);
      return Results.Ok(order.ToDetail(shipment));
    })
    .WithName("GetOrder")
    .Produces<OrderDetailDto>()
    .ProducesProblem(StatusCodes.Status404NotFound);

    return app;
  }
}
