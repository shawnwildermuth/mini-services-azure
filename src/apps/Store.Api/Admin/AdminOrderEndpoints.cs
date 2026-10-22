using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Store.Api.Catalog;
using Store.Api.Orders;
using Store.Data;
using Store.Domain.Orders;
using Store.Messaging;

namespace Store.Api.Admin;

public static class AdminOrderEndpoints
{
  /// <summary>Orders still Pending/Paid after this long probably have a stuck message or an outage behind them.</summary>
  public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(10);

  private static IResult Conflict(string message) =>
    Results.Problem(statusCode: StatusCodes.Status409Conflict, title: message);

  public static void MapAdminOrderEndpoints(this RouteGroupBuilder admin)
  {
    var group = admin.MapGroup("/orders").WithTags("Admin: Orders");

    group.MapGet("/", async (string? status, string? q, int? page, int? pageSize, StoreDbContext db, CancellationToken ct) =>
    {
      var p = Math.Max(page ?? 1, 1);
      var size = Math.Clamp(pageSize ?? 20, 1, 100);

      var query = db.Orders.AsNoTracking().AsQueryable();
      if (Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed))
        query = query.Where(o => o.Status == parsed);
      if (!string.IsNullOrWhiteSpace(q))
        query = query.Where(o => o.OrderNumber.Contains(q) || o.CustomerEmail.Contains(q));

      var total = await query.CountAsync(ct);
      var items = await query.OrderByDescending(o => o.CreatedAt).Skip((p - 1) * size).Take(size)
        .Select(o => new AdminOrderSummaryDto(o.Id, o.OrderNumber, o.Status, o.CreatedAt, o.CustomerEmail, o.Total, o.Items.Sum(i => i.Quantity)))
        .ToListAsync(ct);

      return Results.Ok(new PagedResult<AdminOrderSummaryDto>(items, p, size, total));
    })
    .WithName("AdminListOrders")
    .Produces<PagedResult<AdminOrderSummaryDto>>();

    group.MapGet("/{id:guid}", async (Guid id, StoreDbContext db, CancellationToken ct) =>
      await LoadAsync(db, id, ct) is { } order ? Results.Ok(order) : Results.NotFound())
      .WithName("AdminGetOrder")
      .Produces<AdminOrderDetailDto>()
      .ProducesProblem(StatusCodes.Status404NotFound);

    // Cancel an order that has not shipped. A captured payment is refunded and the reserved stock released.
    group.MapPost("/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, StoreDbContext db, TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
    {
      var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);
      if (order is null) return Results.NotFound();
      if (order.Status is not (OrderStatus.Pending or OrderStatus.Paid))
        return Conflict($"A {order.Status.ToString().ToLowerInvariant()} order cannot be cancelled.");

      var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == id, ct);
      if (payment is { Status: PaymentStatus.Captured }) payment.Status = PaymentStatus.Refunded;

      var productIds = order.Items.Select(i => i.ProductId).ToList();
      var stock = await db.Inventory.Where(i => productIds.Contains(i.ProductId)).ToDictionaryAsync(i => i.ProductId, ct);
      foreach (var item in order.Items)
        if (stock.TryGetValue(item.ProductId, out var s))
          s.QuantityReserved = Math.Max(s.QuantityReserved - item.Quantity, 0);

      order.Status = OrderStatus.Cancelled;
      order.UpdatedAt = clock.GetUtcNow();

      if (!await TrySaveAsync(db, ct)) return Conflict("The order changed while you were working on it. Reload and try again.");
      logs.AdminAction(user, "cancelled order", order.OrderNumber);
      return Results.Ok(await LoadAsync(db, id, ct));
    })
    .WithName("AdminCancelOrder")
    .Produces<AdminOrderDetailDto>()
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);

    // Return the money for an order that already shipped. (Putting the goods back on the shelf is a separate returns process.)
    group.MapPost("/{id:guid}/refund", async (Guid id, ClaimsPrincipal user, StoreDbContext db, TimeProvider clock, ILoggerFactory logs, CancellationToken ct) =>
    {
      var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
      if (order is null) return Results.NotFound();
      if (order.Status != OrderStatus.Shipped)
        return Conflict("Only shipped orders can be refunded. Cancel orders that have not shipped yet.");

      var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == id, ct);
      if (payment is { Status: PaymentStatus.Captured }) payment.Status = PaymentStatus.Refunded;

      order.Status = OrderStatus.Refunded;
      order.UpdatedAt = clock.GetUtcNow();

      if (!await TrySaveAsync(db, ct)) return Conflict("The order changed while you were working on it. Reload and try again.");
      logs.AdminAction(user, "refunded order", order.OrderNumber);
      return Results.Ok(await LoadAsync(db, id, ct));
    })
    .WithName("AdminRefundOrder")
    .Produces<AdminOrderDetailDto>()
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);
  }

  private static async Task<bool> TrySaveAsync(StoreDbContext db, CancellationToken ct)
  {
    try
    {
      await db.SaveChangesAsync(ct);
      return true;
    }
    catch (DbUpdateConcurrencyException)
    {
      return false;
    }
  }

  private static async Task<AdminOrderDetailDto?> LoadAsync(StoreDbContext db, Guid id, CancellationToken ct)
  {
    var order = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);
    if (order is null) return null;

    var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.OrderId == id, ct);
    var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == id, ct);
    return new AdminOrderDetailDto(
      order.ToDetail(shipment),
      order.CustomerEmail,
      payment is null ? null : new AdminPaymentDto(payment.Status, payment.Amount, payment.ProviderReference, payment.DeclineReason, payment.CreatedAt));
  }

  // -------------------------------------------------------------- dashboard

  public static void MapAdminDashboardEndpoint(this RouteGroupBuilder admin) =>
    admin.MapGet("/dashboard", async (StoreDbContext db, TimeProvider clock, CancellationToken ct) =>
    {
      var counts = await db.Orders.AsNoTracking().GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
      var byStatus = Enum.GetValues<OrderStatus>().ToDictionary(s => s.ToString(), s => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0);

      var revenue = await db.Orders.AsNoTracking()
        .Where(o => o.Status == OrderStatus.Paid || o.Status == OrderStatus.Shipped)
        .SumAsync(o => o.Total, ct);

      var lowStock = await db.Inventory.AsNoTracking()
        .Join(db.Products.Where(p => p.IsActive), i => i.ProductId, p => p.Id, (i, p) => new { i, p })
        .Where(r => r.i.QuantityOnHand - r.i.QuantityReserved <= AdminCatalogEndpoints.LowStockThreshold)
        .OrderBy(r => r.i.QuantityOnHand - r.i.QuantityReserved).ThenBy(r => r.p.Name)
        .Take(5)
        .Select(r => new LowStockDto(r.p.Id, r.p.Sku, r.p.Name, Math.Max(r.i.QuantityOnHand - r.i.QuantityReserved, 0)))
        .ToListAsync(ct);

      var cutoff = clock.GetUtcNow() - StuckAfter;
      var stuck = await db.Orders.AsNoTracking()
        .CountAsync(o => (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Paid) && o.UpdatedAt < cutoff, ct);

      return Results.Ok(new DashboardDto(byStatus, revenue, await db.Products.CountAsync(p => p.IsActive, ct), lowStock, stuck));
    })
    .WithTags("Admin: Dashboard")
    .WithName("AdminDashboard")
    .Produces<DashboardDto>();

  // ------------------------------------------------------------- operations

  private static readonly string[] Queues = [Store.Domain.Messaging.Queues.OrderPlaced, Store.Domain.Messaging.Queues.PaymentCaptured];

  public static void MapAdminOperationsEndpoints(this RouteGroupBuilder admin)
  {
    var group = admin.MapGroup("/operations/dead-letters").WithTags("Admin: Operations");

    group.MapGet("/", async (IDeadLetterAdmin deadLetters, CancellationToken ct) =>
    {
      try
      {
        var all = new List<DeadLetterDto>();
        foreach (var queue in Queues)
          all.AddRange((await deadLetters.PeekAsync(queue, 50, ct)).Select(ToDto));
        return Results.Ok(all.OrderByDescending(m => m.EnqueuedAt).ToList());
      }
      catch (ServiceBusNotConfiguredException)
      {
        return NotConfigured();
      }
    })
    .WithName("AdminListDeadLetters")
    .Produces<List<DeadLetterDto>>()
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    group.MapPost("/{queue}/{sequenceNumber:long}/replay", (string queue, long sequenceNumber, IDeadLetterAdmin deadLetters, CancellationToken ct) =>
      Settle(queue, deadLetters.ReplayAsync, sequenceNumber, ct))
      .WithName("AdminReplayDeadLetter")
      .Produces(StatusCodes.Status204NoContent)
      .ProducesProblem(StatusCodes.Status404NotFound)
      .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    group.MapDelete("/{queue}/{sequenceNumber:long}", (string queue, long sequenceNumber, IDeadLetterAdmin deadLetters, CancellationToken ct) =>
      Settle(queue, deadLetters.DiscardAsync, sequenceNumber, ct))
      .WithName("AdminDiscardDeadLetter")
      .Produces(StatusCodes.Status204NoContent)
      .ProducesProblem(StatusCodes.Status404NotFound)
      .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
  }

  private static async Task<IResult> Settle(string queue, Func<string, long, CancellationToken, Task<bool>> action, long sequenceNumber, CancellationToken ct)
  {
    if (!Queues.Contains(queue)) return Results.NotFound();
    try
    {
      return await action(queue, sequenceNumber, ct) ? Results.NoContent() : Results.NotFound();
    }
    catch (ServiceBusNotConfiguredException)
    {
      return NotConfigured();
    }
    catch (DeadLetterBusyException)
    {
      return Conflict("Another operation is working on this message. Try again in a moment.");
    }
  }

  private static IResult NotConfigured() =>
    Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Bus is not configured for this environment.");

  private static DeadLetterDto ToDto(DeadLetteredMessage m) => new(
    m.Queue, m.SequenceNumber, m.MessageId, m.Subject, m.Reason, m.Description, m.DeliveryCount, m.EnqueuedAt, m.Body, OrderIdOf(m.Body));

  /// <summary>Best effort: both message types carry the order id so the console can link to the order.</summary>
  private static Guid? OrderIdOf(string body)
  {
    try
    {
      using var doc = JsonDocument.Parse(body);
      return doc.RootElement.TryGetProperty("orderId", out var id) && id.TryGetGuid(out var guid) ? guid : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }
}
