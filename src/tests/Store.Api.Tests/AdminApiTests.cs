using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Store.Api.Addresses;
using Store.Api.Admin;
using Store.Api.Catalog;
using Store.Data;
using Store.Domain.Messaging;
using Store.Domain.Orders;
using Store.Messaging;

namespace Store.Api.Tests;

public class AdminApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

  private HttpClient Admin() => factory.CreateAdminClient();

  private async Task<T> InDb<T>(Func<StoreDbContext, Task<T>> action)
  {
    using var scope = factory.Services.CreateScope();
    return await action(scope.ServiceProvider.GetRequiredService<StoreDbContext>());
  }

  private static int _unique;
  private static string Unique(string prefix) => $"{prefix}{Interlocked.Increment(ref _unique)}";

  private static async Task<T> Read<T>(HttpResponseMessage response)
  {
    Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    return (await response.Content.ReadFromJsonAsync<T>(Json))!;
  }

  private async Task<AdminCategoryDto> NewCategory(HttpClient admin)
  {
    var slug = Unique("cat-");
    return await Read<AdminCategoryDto>(await admin.PostAsJsonAsync("/api/admin/categories", new { name = slug.ToUpperInvariant(), slug }));
  }

  private async Task<AdminProductDto> NewProduct(HttpClient admin, int categoryId, decimal price = 100m, int stock = 10, bool active = true, string? sku = null)
  {
    var response = await admin.PostAsJsonAsync("/api/admin/products", new
    {
      sku = sku ?? Unique("SKU-"), name = "Test part", brand = "Acme", description = "A test part", price, categoryId, isActive = active,
      initialStock = stock, specs = new[] { new { name = "socket", value = "AM5" }, new { name = "color", value = "Black" } },
    });
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    return (await response.Content.ReadFromJsonAsync<AdminProductDto>(Json))!;
  }

  // ------------------------------------------------------------ authentication

  [Fact]
  public async Task AdminEndpoints_RejectAnonymousAndCustomerTokens()
  {
    var anonymous = factory.CreateClient();
    var customer = await factory.CreateUserAsync();

    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/dashboard")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await customer.Http.GetAsync("/api/admin/dashboard")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await customer.Http.GetAsync("/api/admin/orders")).StatusCode);
  }

  [Fact]
  public async Task AdminEndpoints_RequireTheAdminRole()
  {
    var noRole = factory.CreateAdminClient("Reader");
    var admin = factory.CreateAdminClient();

    Assert.Equal(HttpStatusCode.Forbidden, (await noRole.GetAsync("/api/admin/dashboard")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/dashboard")).StatusCode);
  }

  [Fact]
  public async Task AdminTokens_DoNotWorkOnCustomerEndpoints()
  {
    Assert.Equal(HttpStatusCode.Unauthorized, (await Admin().GetAsync("/api/cart")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await Admin().GetAsync("/api/orders")).StatusCode);
  }

  [Fact]
  public async Task DevTokenEndpoint_IssuesATokenTheAdminApiAccepts_WithRolesAsAnArray()
  {
    var client = factory.CreateClient();
    var token = await Read<DevTokenResponse>(await client.PostAsync("/api/dev/admin-token", null));
    client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);

    var payload = token.AccessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
    payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
    using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
    Assert.Equal(JsonValueKind.Array, claims.RootElement.GetProperty("roles").ValueKind); // the shape Entra uses

    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/dashboard")).StatusCode);
  }

  [Fact]
  public async Task DevTokens_AreInertOutsideDevelopment_EvenIfTheFlagIsSet()
  {
    using var production = factory.WithWebHostBuilder(builder =>
    {
      builder.UseEnvironment("Production");
      builder.UseSetting("Admin:DevTokens:Enabled", "true"); // misconfiguration must not open the door
    });
    var client = production.CreateClient();

    // The endpoint isn't mapped...
    Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/dev/admin-token", null)).StatusCode);
    Assert.Null(production.Services.GetService<DevTokenIssuer>());

    // ...and a token signed with the (development) key from another instance is worthless.
    var forged = factory.Services.GetRequiredService<DevTokenIssuer>().Issue("Mallory", "Admin");
    client.DefaultRequestHeaders.Authorization = new("Bearer", forged);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/dashboard")).StatusCode);
  }

  // ------------------------------------------------------------------ products

  [Fact]
  public async Task CreateProduct_StoresItAndShowsItInTheStorefront()
  {
    var admin = Admin();
    var category = await NewCategory(admin);

    var product = await NewProduct(admin, category.Id, price: 249.99m, stock: 7);

    Assert.Equal(7, product.QuantityOnHand);
    Assert.Equal(["color", "socket"], product.Specs.Select(s => s.Name));
    var publicView = await factory.CreateClient().GetFromJsonAsync<ProductDetailDto>($"/api/products/{product.Sku}");
    Assert.Equal(249.99m, publicView!.Price);
    Assert.Equal(7, (await factory.CreateClient().GetFromJsonAsync<AvailabilityDto>($"/api/products/{product.Sku}/availability"))!.Available);
  }

  [Fact]
  public async Task CreateProduct_RejectsBadInput()
  {
    var admin = Admin();
    var category = await NewCategory(admin);
    var existing = await NewProduct(admin, category.Id);

    HttpResponseMessage Post(object body) => admin.PostAsJsonAsync("/api/admin/products", body).Result;
    object Body(string sku = "OK-SKU", decimal price = 10m, int categoryId = 0, object[]? specs = null) => new
    {
      sku, name = "n", brand = "b", price, categoryId = categoryId == 0 ? category.Id : categoryId, specs = specs ?? [],
    };

    Assert.Equal(HttpStatusCode.BadRequest, Post(Body(sku: existing.Sku)).StatusCode);      // duplicate SKU
    Assert.Equal(HttpStatusCode.BadRequest, Post(Body(price: 0m)).StatusCode);              // price
    Assert.Equal(HttpStatusCode.BadRequest, Post(Body(categoryId: 999999)).StatusCode);     // unknown category
    Assert.Equal(HttpStatusCode.BadRequest, Post(Body(sku: "bad sku!")).StatusCode);        // SKU format
    Assert.Equal(HttpStatusCode.BadRequest, Post(Body(specs: [new { name = "a", value = "1" }, new { name = "A", value = "2" }])).StatusCode); // duplicate spec
  }

  [Fact]
  public async Task UpdateProduct_ChangesFieldsAndDiffsSpecs_AndTheStorefrontSeesItImmediately()
  {
    var admin = Admin();
    var category = await NewCategory(admin);
    var product = await NewProduct(admin, category.Id, price: 100m);
    var publicClient = factory.CreateClient();
    Assert.Equal(100m, (await publicClient.GetFromJsonAsync<ProductDetailDto>($"/api/products/{product.Sku}"))!.Price); // warms the cache

    var response = await admin.PutAsJsonAsync($"/api/admin/products/{product.Id}", new
    {
      name = "Renamed", brand = "Acme", description = "New text", price = 123.45m, categoryId = category.Id, isActive = true,
      specs = new[] { new { name = "socket", value = "LGA1700" }, new { name = "wattage", value = "750W" } }, // change, drop color, add
    });
    var updated = await Read<AdminProductDto>(response);

    Assert.Equal("Renamed", updated.Name);
    Assert.Equal(["socket:LGA1700", "wattage:750W"], updated.Specs.Select(s => $"{s.Name}:{s.Value}"));
    var fresh = await publicClient.GetFromJsonAsync<ProductDetailDto>($"/api/products/{product.Sku}");
    Assert.Equal(123.45m, fresh!.Price); // the cache was invalidated by the write
    Assert.Equal("LGA1700", fresh.Specs["socket"]);
  }

  [Fact]
  public async Task DeactivatingAProduct_HidesItFromTheStorefront_ButNotFromAdmin()
  {
    var admin = Admin();
    var category = await NewCategory(admin);
    var product = await NewProduct(admin, category.Id);
    var publicClient = factory.CreateClient();
    Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync($"/api/products/{product.Sku}")).StatusCode);

    await Read<AdminProductDto>(await admin.PutAsJsonAsync($"/api/admin/products/{product.Id}", new
    {
      name = product.Name, brand = product.Brand, description = product.Description, price = product.Price,
      categoryId = category.Id, isActive = false, specs = Array.Empty<object>(),
    }));

    Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync($"/api/products/{product.Sku}")).StatusCode);
    var inactive = await Read<PagedResult<AdminProductSummaryDto>>(await admin.GetAsync($"/api/admin/products?status=inactive&category={category.Slug}"));
    Assert.Equal(product.Sku, Assert.Single(inactive.Items).Sku);
  }

  [Fact]
  public async Task ListProducts_FiltersBySearchAndCategory()
  {
    var admin = Admin();
    var category = await NewCategory(admin);
    var a = await NewProduct(admin, category.Id);
    await NewProduct(admin, category.Id);

    var inCategory = await Read<PagedResult<AdminProductSummaryDto>>(await admin.GetAsync($"/api/admin/products?category={category.Slug}"));
    var bySku = await Read<PagedResult<AdminProductSummaryDto>>(await admin.GetAsync($"/api/admin/products?q={a.Sku}"));

    Assert.Equal(2, inCategory.TotalCount);
    Assert.Equal(a.Id, Assert.Single(bySku.Items).Id);
  }

  [Fact]
  public async Task UnknownProduct_IsNotFound()
  {
    Assert.Equal(HttpStatusCode.NotFound, (await Admin().GetAsync("/api/admin/products/999999")).StatusCode);
  }

  [Fact]
  public async Task AdminChanges_AreAuditLogged_WithWhoAndWhat_ButNoCustomerData()
  {
    var admin = Admin();
    var category = await NewCategory(admin);
    var product = await NewProduct(admin, category.Id);
    var customer = await factory.CreateUserAsync();

    await admin.PutAsJsonAsync($"/api/admin/products/{product.Id}", new
    {
      name = "Renamed", brand = "Acme", description = "", price = 55m, categoryId = category.Id, isActive = true, specs = Array.Empty<object>(),
    });
    await admin.PutAsJsonAsync($"/api/admin/inventory/{product.Id}", new { quantityOnHand = 3 });

    var audit = factory.Logs.Entries.Where(e => e.Category == "Admin.Audit").Select(e => e.Message).ToList();
    Assert.Contains(audit, m => m.Contains("Test Admin") && m.Contains("created product") && m.Contains(product.Sku));
    Assert.Contains(audit, m => m.Contains("updated product") && m.Contains(product.Sku) && m.Contains("55"));
    Assert.Contains(audit, m => m.Contains("set stock on hand") && m.Contains($"{product.Sku} = 3"));
    Assert.Contains(audit, m => m.Contains("created category") && m.Contains(category.Slug));

    // Nothing the application logs may contain a customer's email address or a token.
    var everything = factory.Logs.Entries.Select(e => e.Message).ToList();
    Assert.DoesNotContain(everything, m => m.Contains(customer.Email, StringComparison.OrdinalIgnoreCase));
    Assert.DoesNotContain(everything, m => m.Contains(customer.Tokens.AccessToken));
    Assert.DoesNotContain(everything, m => m.Contains(customer.Tokens.RefreshToken));
  }

  // ---------------------------------------------------------------- categories

  [Fact]
  public async Task Categories_CanBeCreatedRenamedAndDeleted_AndTheStorefrontFollows()
  {
    var admin = Admin();
    var category = await NewCategory(admin);
    var publicClient = factory.CreateClient();
    Assert.Contains((await publicClient.GetFromJsonAsync<List<CategoryDto>>("/api/categories"))!, c => c.Slug == category.Slug);

    var renamed = await Read<AdminCategoryDto>(await admin.PutAsJsonAsync($"/api/admin/categories/{category.Id}", new { name = "Renamed", slug = category.Slug }));
    Assert.Equal("Renamed", renamed.Name);
    Assert.Equal("Renamed", (await publicClient.GetFromJsonAsync<List<CategoryDto>>("/api/categories"))!.Single(c => c.Slug == category.Slug).Name);

    Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/categories/{category.Id}")).StatusCode);
    Assert.DoesNotContain((await publicClient.GetFromJsonAsync<List<CategoryDto>>("/api/categories"))!, c => c.Slug == category.Slug);
  }

  [Fact]
  public async Task Categories_RejectDuplicateSlugsBadSlugsAndDeletionWhileInUse()
  {
    var admin = Admin();
    var category = await NewCategory(admin);
    await NewProduct(admin, category.Id);

    Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/categories", new { name = "x", slug = category.Slug })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/categories", new { name = "x", slug = "Not A Slug" })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/categories/{category.Id}")).StatusCode);
  }

  // ----------------------------------------------------------------- inventory

  [Fact]
  public async Task SetStock_UpdatesOnHand_ButNeverBelowWhatIsReserved()
  {
    var admin = Admin();
    var category = await NewCategory(admin);
    var product = await NewProduct(admin, category.Id, stock: 10);
    await InDb(async db =>
    {
      db.Inventory.Single(i => i.ProductId == product.Id).QuantityReserved = 4;
      return await db.SaveChangesAsync();
    });

    var ok = await Read<InventoryItemDto>(await admin.PutAsJsonAsync($"/api/admin/inventory/{product.Id}", new { quantityOnHand = 25 }));
    Assert.Equal(21, ok.Available);

    Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/inventory/{product.Id}", new { quantityOnHand = 3 })).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/admin/inventory/999999", new { quantityOnHand = 3 })).StatusCode);
  }

  [Fact]
  public async Task Inventory_ListCanBeFilteredToLowStock()
  {
    var admin = Admin();
    var category = await NewCategory(admin);
    var low = await NewProduct(admin, category.Id, stock: 2);
    var plenty = await NewProduct(admin, category.Id, stock: 500);

    var items = await Read<PagedResult<InventoryItemDto>>(await admin.GetAsync("/api/admin/inventory?lowStock=true&pageSize=100"));

    Assert.Contains(items.Items, i => i.ProductId == low.Id);
    Assert.DoesNotContain(items.Items, i => i.ProductId == plenty.Id);
  }

  // -------------------------------------------------------------------- orders

  private async Task<(AuthenticatedClient Customer, Guid OrderId, string Sku)> PlaceOrder(HttpClient admin, int quantity = 2)
  {
    var category = await NewCategory(admin);
    var product = await NewProduct(admin, category.Id, price: 40m, stock: 20);
    var customer = await factory.CreateUserAsync();

    var address = (await (await customer.Http.PostAsJsonAsync("/api/addresses", new
    {
      fullName = "Ada", line1 = "1 Main", city = "Atlanta", postalCode = "30301", countryCode = "US",
    })).Content.ReadFromJsonAsync<AddressDto>())!;
    (await customer.Http.PutAsJsonAsync($"/api/cart/items/{product.Sku}", new { quantity })).EnsureSuccessStatusCode();
    var order = await Read<Store.Api.Orders.OrderDetailDto>(await customer.Http.PostAsJsonAsync("/api/orders", new { addressId = address.Id }));
    return (customer, order.Id, product.Sku);
  }

  private Task<(int OnHand, int Reserved)> StockOf(string sku) =>
    InDb(async db =>
    {
      var stock = await db.Inventory.AsNoTracking().SingleAsync(i => db.Products.Any(p => p.Id == i.ProductId && p.Sku == sku));
      return (stock.QuantityOnHand, stock.QuantityReserved);
    });

  [Fact]
  public async Task Orders_AreListedAndDetailed_WithPaymentAndShipment()
  {
    var admin = Admin();
    var (customer, orderId, _) = await PlaceOrder(admin);

    var list = await Read<PagedResult<AdminOrderSummaryDto>>(await admin.GetAsync($"/api/admin/orders?q={customer.Email}"));
    var summary = Assert.Single(list.Items);
    Assert.Equal(orderId, summary.Id);
    Assert.Equal(OrderStatus.Pending, summary.Status);

    await InDb(async db =>
    {
      db.Payments.Add(new Payment { OrderId = orderId, Amount = 92m, Currency = "USD", Status = PaymentStatus.Captured, ProviderReference = "mock_1" });
      return await db.SaveChangesAsync();
    });
    var detail = await Read<AdminOrderDetailDto>(await admin.GetAsync($"/api/admin/orders/{orderId}"));
    Assert.Equal(customer.Email, detail.CustomerEmail);
    Assert.Equal("mock_1", detail.Payment!.ProviderReference);
    Assert.Equal(2, detail.Order.Items.Single().Quantity);

    Assert.Empty((await Read<PagedResult<AdminOrderSummaryDto>>(await admin.GetAsync($"/api/admin/orders?q={customer.Email}&status=Shipped"))).Items);
  }

  [Fact]
  public async Task CancelOrder_ReleasesStock_AndRefundsACapturedPayment()
  {
    var admin = Admin();
    var (_, orderId, sku) = await PlaceOrder(admin, quantity: 3);
    Assert.Equal((20, 3), await StockOf(sku));
    await InDb(async db =>
    {
      db.Payments.Add(new Payment { OrderId = orderId, Amount = 132m, Currency = "USD", Status = PaymentStatus.Captured });
      db.Orders.Single(o => o.Id == orderId).Status = OrderStatus.Paid;
      return await db.SaveChangesAsync();
    });

    var cancelled = await Read<AdminOrderDetailDto>(await admin.PostAsync($"/api/admin/orders/{orderId}/cancel", null));

    Assert.Equal(OrderStatus.Cancelled, cancelled.Order.Status);
    Assert.Equal(PaymentStatus.Refunded, cancelled.Payment!.Status);
    Assert.Equal((20, 0), await StockOf(sku));
  }

  [Fact]
  public async Task CancelOrder_IsRefusedOnceShippedOrAlreadyCancelled()
  {
    var admin = Admin();
    var (_, orderId, _) = await PlaceOrder(admin);
    await Read<AdminOrderDetailDto>(await admin.PostAsync($"/api/admin/orders/{orderId}/cancel", null));

    Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/orders/{orderId}/cancel", null)).StatusCode);

    var (_, shippedId, _) = await PlaceOrder(admin);
    await InDb(async db => { db.Orders.Single(o => o.Id == shippedId).Status = OrderStatus.Shipped; return await db.SaveChangesAsync(); });
    Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/orders/{shippedId}/cancel", null)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/orders/{Guid.NewGuid()}/cancel", null)).StatusCode);
  }

  [Fact]
  public async Task RefundOrder_OnlyAppliesToShippedOrders()
  {
    var admin = Admin();
    var (_, orderId, sku) = await PlaceOrder(admin);
    Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/orders/{orderId}/refund", null)).StatusCode);

    await InDb(async db =>
    {
      db.Payments.Add(new Payment { OrderId = orderId, Amount = 92m, Currency = "USD", Status = PaymentStatus.Captured });
      db.Orders.Single(o => o.Id == orderId).Status = OrderStatus.Shipped;
      return await db.SaveChangesAsync();
    });
    var refunded = await Read<AdminOrderDetailDto>(await admin.PostAsync($"/api/admin/orders/{orderId}/refund", null));

    Assert.Equal(OrderStatus.Refunded, refunded.Order.Status);
    Assert.Equal(PaymentStatus.Refunded, refunded.Payment!.Status);
    Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/orders/{orderId}/refund", null)).StatusCode);
    _ = sku;
  }

  [Fact]
  public async Task DeadLetter_ThatAnotherOperatorIsHandling_IsReportedBusy_NotGone()
  {
    var admin = Admin();
    factory.DeadLetters.Add(new DeadLetteredMessage(Queues.OrderPlaced, 777, "m-busy", "OrderPlaced", "MaxDeliveryCountExceeded", null, 5, DateTimeOffset.UtcNow, "{}"));
    factory.DeadLetters.BusySequence = 777;

    var replay = await admin.PostAsync($"/api/admin/operations/dead-letters/{Queues.OrderPlaced}/777/replay", null);
    var discard = await admin.DeleteAsync($"/api/admin/operations/dead-letters/{Queues.OrderPlaced}/777");

    Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);   // not 404 "already gone"
    Assert.Equal(HttpStatusCode.Conflict, discard.StatusCode);
    Assert.Contains("Try again", await discard.Content.ReadAsStringAsync());

    factory.DeadLetters.BusySequence = null;
    Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/operations/dead-letters/{Queues.OrderPlaced}/777")).StatusCode);
  }

  // ----------------------------------------------------------------- dashboard

  [Fact]
  public async Task Dashboard_SummarisesOrdersRevenueStockAndStuckOrders()
  {
    var admin = Admin();
    var before = await Read<DashboardDto>(await admin.GetAsync("/api/admin/dashboard"));

    var (_, orderId, _) = await PlaceOrder(admin);
    await InDb(async db => { db.Orders.Single(o => o.Id == orderId).Status = OrderStatus.Shipped; return await db.SaveChangesAsync(); });
    var category = await NewCategory(admin);
    var scarce = await NewProduct(admin, category.Id, stock: 1);

    var after = await Read<DashboardDto>(await admin.GetAsync("/api/admin/dashboard"));

    Assert.Equal(before.OrdersByStatus["Shipped"] + 1, after.OrdersByStatus["Shipped"]);
    Assert.Equal(before.Revenue + 92m, after.Revenue); // 2 x $40 + $12 UPS shipping
    Assert.Contains(after.LowStock, l => l.ProductId == scarce.Id);
  }

  [Fact]
  public async Task Dashboard_FlagsOrdersThatHaveSatInProgressTooLong()
  {
    var admin = Admin();
    var (_, orderId, _) = await PlaceOrder(admin);
    var before = await Read<DashboardDto>(await admin.GetAsync("/api/admin/dashboard"));

    factory.Clock.Advance(AdminOrderEndpoints.StuckAfter + TimeSpan.FromMinutes(1));
    var after = await Read<DashboardDto>(await admin.GetAsync("/api/admin/dashboard"));

    Assert.True(after.StuckOrders > before.StuckOrders);

    // Once it is dealt with, it stops being flagged.
    await Read<AdminOrderDetailDto>(await admin.PostAsync($"/api/admin/orders/{orderId}/cancel", null));
    var resolved = await Read<DashboardDto>(await admin.GetAsync("/api/admin/dashboard"));
    Assert.Equal(after.StuckOrders - 1, resolved.StuckOrders);
  }

  // ---------------------------------------------------------------- operations

  [Fact]
  public async Task DeadLetters_CanBeListedReplayedAndDiscarded()
  {
    var admin = Admin();
    var orderId = Guid.NewGuid();
    var body = JsonSerializer.Serialize(new { orderId, orderNumber = "PS-1" });
    factory.DeadLetters.Add(new DeadLetteredMessage(Queues.OrderPlaced, 101, "m-1", "OrderPlaced", "MaxDeliveryCountExceeded", null, 5, DateTimeOffset.UtcNow, body));
    factory.DeadLetters.Add(new DeadLetteredMessage(Queues.PaymentCaptured, 202, "m-2", null, "InvalidPayload", null, 0, DateTimeOffset.UtcNow.AddMinutes(-1), "not json"));

    var list = await Read<List<DeadLetterDto>>(await admin.GetAsync("/api/admin/operations/dead-letters"));
    Assert.Equal(["m-1", "m-2"], list.Select(m => m.MessageId));
    Assert.Equal(orderId, list[0].OrderId);
    Assert.Null(list[1].OrderId);

    Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/operations/dead-letters/{Queues.OrderPlaced}/101/replay", null)).StatusCode);
    Assert.Equal(101, Assert.Single(factory.DeadLetters.Replayed).SequenceNumber);
    Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/operations/dead-letters/{Queues.PaymentCaptured}/202")).StatusCode);

    Assert.Empty(await Read<List<DeadLetterDto>>(await admin.GetAsync("/api/admin/operations/dead-letters")));
    Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/admin/operations/dead-letters/{Queues.PaymentCaptured}/202")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/admin/operations/dead-letters/not-a-queue/1")).StatusCode);
  }
}
