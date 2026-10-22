using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Store.Api.Addresses;
using Store.Api.Catalog;
using Store.Api.Messaging;
using Store.Api.Orders;
using Store.Data;
using Store.Domain.Messaging;
using Store.Domain.Orders;

namespace Store.Api.Tests;

public class OrderApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
  {
    Converters = { new JsonStringEnumConverter() },
  };

  private static async Task<int> AddAddress(HttpClient client, string country = "US")
  {
    var response = await client.PostAsJsonAsync("/api/addresses", new
    {
      fullName = "Ada Lovelace", line1 = "1 Main St", city = "Atlanta", region = "GA", postalCode = "30301", countryCode = country,
    });
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<AddressDto>())!.Id;
  }

  private static async Task AddToCart(HttpClient client, string sku, int quantity) =>
    (await client.PutAsJsonAsync($"/api/cart/items/{sku}", new { quantity })).EnsureSuccessStatusCode();

  private static Task<HttpResponseMessage> Checkout(HttpClient client, int addressId, string? key = null)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(new { addressId }) };
    if (key is not null) request.Headers.Add("Idempotency-Key", key);
    return client.SendAsync(request);
  }

  private static async Task<OrderDetailDto> ReadOrder(HttpResponseMessage response) =>
    (await response.Content.ReadFromJsonAsync<OrderDetailDto>(Json))!;

  private async Task<T> InDb<T>(Func<StoreDbContext, Task<T>> action)
  {
    using var scope = factory.Services.CreateScope();
    return await action(scope.ServiceProvider.GetRequiredService<StoreDbContext>());
  }

  private Task<int> Reserved(string sku) =>
    InDb(db => db.Inventory.Where(i => db.Products.Any(p => p.Id == i.ProductId && p.Sku == sku)).Select(i => i.QuantityReserved).SingleAsync());

  [Fact]
  public async Task Checkout_CreatesOrder_ReservesStock_ClearsCart_AndQueuesMessage()
  {
    var user = await factory.CreateUserAsync();
    var address = await AddAddress(user.Http);
    await AddToCart(user.Http, "CPU-AMD-9600X", 2); // 2 x 279.00
    await AddToCart(user.Http, "MB-GIG-B650M", 1);  // 169.00
    var cpuReservedBefore = await Reserved("CPU-AMD-9600X");

    var response = await Checkout(user.Http, address);

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var order = await ReadOrder(response);
    Assert.Equal(OrderStatus.Pending, order.Status);
    Assert.Equal(727m, order.Subtotal);
    Assert.Equal(Carrier.UPS, order.Carrier);
    Assert.Equal(12m, order.ShippingCost);
    Assert.Equal(739m, order.Total);
    Assert.Equal(2, order.Items.Count);
    Assert.Matches(@"^PS-\d{6}-[0-9A-F]{6}$", order.OrderNumber);
    Assert.Equal("Atlanta", order.ShipTo.City);

    Assert.Empty((await user.Http.GetFromJsonAsync<Store.Api.Carts.CartDto>("/api/cart"))!.Items);
    Assert.Equal(cpuReservedBefore + 2, await Reserved("CPU-AMD-9600X"));

    var outbox = await InDb(db => db.OutboxMessages.Where(m => m.Payload.Contains(order.Id.ToString())).ToListAsync());
    var message = Assert.Single(outbox);
    Assert.Equal(Queues.OrderPlaced, message.Destination);
    Assert.Null(message.ProcessedAt);
    var payload = JsonSerializer.Deserialize<OrderPlaced>(message.Payload, JsonSerializerOptions.Web)!;
    Assert.Equal(order.OrderNumber, payload.OrderNumber);
    Assert.Equal(739m, payload.Total);
    Assert.Equal(user.Email, payload.CustomerEmail);
  }

  [Fact]
  public async Task Checkout_ShipsInternationalOrdersWithDhl()
  {
    var user = await factory.CreateUserAsync();
    var address = await AddAddress(user.Http, "DE");
    await AddToCart(user.Http, "MB-GIG-H610", 1); // 99.00

    var order = await ReadOrder(await Checkout(user.Http, address));

    Assert.Equal(Carrier.DHL, order.Carrier);
    Assert.Equal(29m, order.ShippingCost);
    Assert.Equal(128m, order.Total);
  }

  [Fact]
  public async Task Quote_MatchesWhatCheckoutCharges()
  {
    var user = await factory.CreateUserAsync();
    var address = await AddAddress(user.Http, "CA");
    await AddToCart(user.Http, "CPU-AMD-9600X", 1);

    var quote = await user.Http.GetFromJsonAsync<QuoteDto>($"/api/checkout/quote?addressId={address}", Json);
    var order = await ReadOrder(await Checkout(user.Http, address));

    Assert.Equal(order.Total, quote!.Total);
    Assert.Equal(Carrier.UPS, quote.Carrier);
    Assert.Equal(HttpStatusCode.NotFound, (await user.Http.GetAsync("/api/checkout/quote?addressId=999999")).StatusCode);
  }

  [Fact]
  public async Task Checkout_RejectsEmptyCartAndForeignAddress()
  {
    var user = await factory.CreateUserAsync();
    var other = await factory.CreateUserAsync();
    var mine = await AddAddress(user.Http);
    var theirs = await AddAddress(other.Http);

    Assert.Equal(HttpStatusCode.BadRequest, (await Checkout(user.Http, mine)).StatusCode); // empty cart

    await AddToCart(user.Http, "CPU-AMD-9600X", 1);
    Assert.Equal(HttpStatusCode.BadRequest, (await Checkout(user.Http, theirs)).StatusCode);
    Assert.Single((await user.Http.GetFromJsonAsync<Store.Api.Carts.CartDto>("/api/cart"))!.Items); // untouched
  }

  [Fact]
  public async Task Checkout_WhenStockWasTakenInTheMeantime_Conflicts_AndChangesNothing()
  {
    // PSU-MSI-1200 has 5 in stock. Both customers fit it in their cart, but only one can buy it.
    var first = await factory.CreateUserAsync();
    var second = await factory.CreateUserAsync();
    var firstAddress = await AddAddress(first.Http);
    var secondAddress = await AddAddress(second.Http);
    await AddToCart(first.Http, "PSU-MSI-1200", 5);
    await AddToCart(second.Http, "PSU-MSI-1200", 5);

    Assert.Equal(HttpStatusCode.Created, (await Checkout(first.Http, firstAddress)).StatusCode);
    var reservedAfterFirst = await Reserved("PSU-MSI-1200");

    var response = await Checkout(second.Http, secondAddress);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.Contains("out of stock", await response.Content.ReadAsStringAsync());
    Assert.Equal(reservedAfterFirst, await Reserved("PSU-MSI-1200"));
    Assert.Single((await second.Http.GetFromJsonAsync<Store.Api.Carts.CartDto>("/api/cart"))!.Items); // cart kept
    Assert.Empty((await second.Http.GetFromJsonAsync<PagedResult<OrderSummaryDto>>("/api/orders", Json))!.Items);
  }

  [Fact]
  public async Task Checkout_WithSameIdempotencyKey_ReturnsTheOriginalOrder()
  {
    var user = await factory.CreateUserAsync();
    var address = await AddAddress(user.Http);
    await AddToCart(user.Http, "MB-GIG-H610", 1);
    var key = Guid.NewGuid().ToString();

    var first = await Checkout(user.Http, address, key);
    var reservedAfterFirst = await Reserved("MB-GIG-H610");
    var second = await Checkout(user.Http, address, key);

    Assert.Equal(HttpStatusCode.Created, first.StatusCode);
    Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    Assert.Equal((await ReadOrder(first)).Id, (await ReadOrder(second)).Id);
    Assert.Equal(reservedAfterFirst, await Reserved("MB-GIG-H610")); // not reserved twice
    var orders = await user.Http.GetFromJsonAsync<PagedResult<OrderSummaryDto>>("/api/orders", Json);
    Assert.Single(orders!.Items);
  }

  [Fact]
  public async Task Orders_AreListedNewestFirst_AndOnlyVisibleToTheirOwner()
  {
    var owner = await factory.CreateUserAsync();
    var stranger = await factory.CreateUserAsync();
    var address = await AddAddress(owner.Http);

    await AddToCart(owner.Http, "MB-GIG-H610", 1);
    var older = await ReadOrder(await Checkout(owner.Http, address));
    factory.Clock.Advance(TimeSpan.FromMinutes(1));
    await AddToCart(owner.Http, "MB-GIG-H610", 2);
    var newer = await ReadOrder(await Checkout(owner.Http, address));

    var list = await owner.Http.GetFromJsonAsync<PagedResult<OrderSummaryDto>>("/api/orders", Json);
    Assert.Equal([newer.Id, older.Id], list!.Items.Select(o => o.Id));
    Assert.Equal(2, list.Items[0].ItemCount);

    Assert.Equal(older.Id, (await owner.Http.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{older.Id}", Json))!.Id);
    Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/orders/{older.Id}")).StatusCode);
    Assert.Empty((await stranger.Http.GetFromJsonAsync<PagedResult<OrderSummaryDto>>("/api/orders", Json))!.Items);
  }

  [Fact]
  public async Task Order_KeepsItsPriceAndAddressSnapshot()
  {
    var user = await factory.CreateUserAsync();
    var address = await AddAddress(user.Http);
    await AddToCart(user.Http, "MB-GIG-H610", 1);
    var order = await ReadOrder(await Checkout(user.Http, address));

    await InDb(async db =>
    {
      db.Products.Single(p => p.Sku == "MB-GIG-H610").Price = 1m;
      db.Addresses.Single(a => a.Id == address).City = "Elsewhere";
      return await db.SaveChangesAsync();
    });

    var fetched = await user.Http.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{order.Id}", Json);
    Assert.Equal(99m, fetched!.Items.Single().UnitPrice);
    Assert.Equal("Atlanta", fetched.ShipTo.City);
  }

  [Fact]
  public async Task OrderDetail_IncludesTheShipmentOnceFulfillmentHasBookedIt()
  {
    var user = await factory.CreateUserAsync();
    var address = await AddAddress(user.Http);
    await AddToCart(user.Http, "MB-GIG-H610", 1);
    var order = await ReadOrder(await Checkout(user.Http, address));
    Assert.Null((await user.Http.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{order.Id}", Json))!.Shipment);

    await InDb(async db =>
    {
      db.Shipments.Add(new Shipment { OrderId = order.Id, Carrier = Carrier.UPS, TrackingNumber = "1Z999AA10123456784", Service = "UPS Ground" });
      db.Orders.Single(o => o.Id == order.Id).Status = OrderStatus.Shipped;
      return await db.SaveChangesAsync();
    });

    var shipped = await user.Http.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{order.Id}", Json);
    Assert.Equal(OrderStatus.Shipped, shipped!.Status);
    Assert.Equal("1Z999AA10123456784", shipped.Shipment!.TrackingNumber);
    Assert.Equal(Carrier.UPS, shipped.Shipment.Carrier);
  }

  [Fact]
  public async Task Orders_RequireAuthentication()
  {
    var anonymous = factory.CreateClient();

    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/orders")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/orders", new { addressId = 1 })).StatusCode);
  }

  // ----- outbox dispatch -----

  private async Task<int> Dispatch()
  {
    using var scope = factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
  }

  private async Task<Order> PlaceSimpleOrder()
  {
    var user = await factory.CreateUserAsync();
    var address = await AddAddress(user.Http);
    await AddToCart(user.Http, "MB-GIG-H610", 1);
    var dto = await ReadOrder(await Checkout(user.Http, address));
    return await InDb(db => db.Orders.SingleAsync(o => o.Id == dto.Id));
  }

  [Fact]
  public async Task Dispatcher_PublishesOrderPlaced_OnceWithTheOutboxIdAsMessageId()
  {
    var order = await PlaceSimpleOrder();
    var outboxId = await InDb(db => db.OutboxMessages.Where(m => m.Payload.Contains(order.Id.ToString())).Select(m => m.Id).SingleAsync());

    await Dispatch();
    await Dispatch(); // already processed: must not publish again

    var published = Assert.Single(factory.Publisher.Published, m => m.Json.Contains(order.Id.ToString()));
    Assert.Equal(Queues.OrderPlaced, published.Queue);
    Assert.Equal(outboxId, published.MessageId);
    Assert.Equal(nameof(OrderPlaced), published.Type);
    var row = await InDb(db => db.OutboxMessages.SingleAsync(m => m.Id == outboxId));
    Assert.NotNull(row.ProcessedAt);
  }

  [Fact]
  public async Task Dispatcher_RetriesWithBackoff_WhenPublishingFails()
  {
    var order = await PlaceSimpleOrder();
    factory.Publisher.Failure = new InvalidOperationException("bus down");

    await Dispatch();
    var failed = await InDb(db => db.OutboxMessages.SingleAsync(m => m.Payload.Contains(order.Id.ToString())));
    Assert.Null(failed.ProcessedAt);
    Assert.Equal(1, failed.Attempts);
    Assert.Equal("bus down", failed.LastError);
    Assert.True(failed.NextAttemptAt > factory.Clock.GetUtcNow());

    // The bus recovers, but the backoff window hasn't elapsed yet.
    factory.Publisher.Failure = null;
    await Dispatch();
    Assert.DoesNotContain(factory.Publisher.Published, m => m.Json.Contains(order.Id.ToString()));

    factory.Clock.Advance(TimeSpan.FromMinutes(1));
    await Dispatch();
    Assert.Contains(factory.Publisher.Published, m => m.Json.Contains(order.Id.ToString()));
    var delivered = await InDb(db => db.OutboxMessages.SingleAsync(m => m.Id == failed.Id));
    Assert.NotNull(delivered.ProcessedAt);
    Assert.Null(delivered.LastError);
  }
}
