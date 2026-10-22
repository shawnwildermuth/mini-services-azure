using System.Net;
using System.Net.Http.Json;
using Store.Api.Carts;

namespace Store.Api.Tests;

public class CartApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private static Task<HttpResponseMessage> Put(HttpClient client, string sku, int quantity) =>
    client.PutAsJsonAsync($"/api/cart/items/{sku}", new { quantity });

  [Fact]
  public async Task NewUser_HasEmptyCart()
  {
    var user = await factory.CreateUserAsync();

    var cart = await user.Http.GetFromJsonAsync<CartDto>("/api/cart");

    Assert.Empty(cart!.Items);
    Assert.Equal(0, cart.Subtotal);
  }

  [Fact]
  public async Task SetItem_AddsThenUpdatesQuantity_AndComputesTotals()
  {
    var user = await factory.CreateUserAsync();

    await Put(user.Http, "CPU-AMD-9600X", 1);           // 279.00
    var cart = await (await Put(user.Http, "MB-GIG-B650M", 2)).Content.ReadFromJsonAsync<CartDto>(); // 2 x 169.00
    Assert.Equal(2, cart!.Items.Count);
    Assert.Equal(3, cart.ItemCount);
    Assert.Equal(279m + 338m, cart.Subtotal);

    cart = await (await Put(user.Http, "CPU-AMD-9600X", 3)).Content.ReadFromJsonAsync<CartDto>();
    Assert.Equal(2, cart!.Items.Count);
    Assert.Equal(3, cart.Items.Single(i => i.Sku == "CPU-AMD-9600X").Quantity);
    Assert.Equal(279m * 3 + 338m, cart.Subtotal);
  }

  [Fact]
  public async Task SetItem_RejectsInvalidQuantities()
  {
    var user = await factory.CreateUserAsync();

    Assert.Equal(HttpStatusCode.BadRequest, (await Put(user.Http, "CPU-AMD-9600X", 0)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Put(user.Http, "CPU-AMD-9600X", CartService.MaxQuantityPerItem + 1)).StatusCode);
  }

  [Fact]
  public async Task SetItem_RejectsQuantityAboveStockAndOutOfStockItems()
  {
    var user = await factory.CreateUserAsync();

    // GPU-NV-5090 has 3 in stock; CPU-INT-14600K has none.
    Assert.Equal(HttpStatusCode.BadRequest, (await Put(user.Http, "GPU-NV-5090", 4)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await Put(user.Http, "GPU-NV-5090", 3)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Put(user.Http, "CPU-INT-14600K", 1)).StatusCode);
  }

  [Fact]
  public async Task SetItem_UnknownSkuIsNotFound()
  {
    var user = await factory.CreateUserAsync();

    Assert.Equal(HttpStatusCode.NotFound, (await Put(user.Http, "NOPE", 1)).StatusCode);
  }

  [Fact]
  public async Task RemoveItem_AndClear()
  {
    var user = await factory.CreateUserAsync();
    await Put(user.Http, "CPU-AMD-9600X", 1);
    await Put(user.Http, "MB-GIG-B650M", 1);

    var cart = await (await user.Http.DeleteAsync("/api/cart/items/CPU-AMD-9600X")).Content.ReadFromJsonAsync<CartDto>();
    Assert.Equal(["MB-GIG-B650M"], cart!.Items.Select(i => i.Sku));

    // Removing something that isn't in the cart is a harmless no-op.
    Assert.Equal(HttpStatusCode.OK, (await user.Http.DeleteAsync("/api/cart/items/CPU-AMD-9600X")).StatusCode);

    Assert.Equal(HttpStatusCode.NoContent, (await user.Http.DeleteAsync("/api/cart")).StatusCode);
    Assert.Empty((await user.Http.GetFromJsonAsync<CartDto>("/api/cart"))!.Items);
  }

  [Fact]
  public async Task Carts_AreIsolatedPerUser()
  {
    var alice = await factory.CreateUserAsync();
    var bob = await factory.CreateUserAsync();

    await Put(alice.Http, "CPU-AMD-9600X", 1);

    Assert.Empty((await bob.Http.GetFromJsonAsync<CartDto>("/api/cart"))!.Items);
  }
}
