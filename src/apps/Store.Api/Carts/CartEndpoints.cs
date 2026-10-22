using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Store.Api.Auth;

namespace Store.Api.Carts;

public class SetCartItemRequest
{
  [Range(1, CartService.MaxQuantityPerItem)]
  public int Quantity { get; set; }
}

public static class CartEndpoints
{
  public static IServiceCollection AddCart(this IServiceCollection services) => services.AddScoped<CartService>();

  public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup("/api/cart").WithTags("Cart").RequireAuthorization();

    group.MapGet("/", async (ClaimsPrincipal user, CartService cart, CancellationToken ct) =>
      Results.Ok(await cart.GetAsync(user.GetUserId(), ct)))
      .WithName("GetCart")
      .Produces<CartDto>();

    group.MapPut("/items/{sku}", async (string sku, SetCartItemRequest request, ClaimsPrincipal user, CartService cart, CancellationToken ct) =>
    {
      var userId = user.GetUserId();
      var (outcome, available) = await cart.SetItemAsync(userId, sku, request.Quantity, ct);

      return outcome switch
      {
        SetItemOutcome.ProductNotFound => Results.NotFound(),
        SetItemOutcome.InsufficientStock => Results.ValidationProblem(new Dictionary<string, string[]>
        {
          [nameof(request.Quantity)] = [available == 0 ? "This item is out of stock." : $"Only {available} available."],
        }),
        _ => Results.Ok(await cart.GetAsync(userId, ct)),
      };
    })
    .WithName("SetCartItem")
    .Produces<CartDto>()
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status404NotFound);

    group.MapDelete("/items/{sku}", async (string sku, ClaimsPrincipal user, CartService cart, CancellationToken ct) =>
    {
      var userId = user.GetUserId();
      await cart.RemoveItemAsync(userId, sku, ct);
      return Results.Ok(await cart.GetAsync(userId, ct));
    })
    .WithName("RemoveCartItem")
    .Produces<CartDto>();

    group.MapDelete("/", async (ClaimsPrincipal user, CartService cart, CancellationToken ct) =>
    {
      await cart.ClearAsync(user.GetUserId(), ct);
      return Results.NoContent();
    })
    .WithName("ClearCart")
    .Produces(StatusCodes.Status204NoContent);

    return app;
  }
}
