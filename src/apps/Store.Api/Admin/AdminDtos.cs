using System.ComponentModel.DataAnnotations;
using Store.Domain.Orders;

namespace Store.Api.Admin;

// ----- catalog -----

public class SpecDto
{
  [Required, StringLength(64)] public string Name { get; set; } = "";
  [Required, StringLength(128)] public string Value { get; set; } = "";
}

public class ProductRequest
{
  [Required, StringLength(256)] public string Name { get; set; } = "";
  [Required, StringLength(128)] public string Brand { get; set; } = "";
  [StringLength(2000)] public string Description { get; set; } = "";
  [Range(typeof(decimal), "0.01", "100000")] public decimal Price { get; set; }
  [Range(1, int.MaxValue)] public int CategoryId { get; set; }
  public bool IsActive { get; set; } = true;
  [MaxLength(30)] public List<SpecDto> Specs { get; set; } = [];
}

public class CreateProductRequest : ProductRequest
{
  [Required, RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9._-]{1,62}$", ErrorMessage = "Use letters, digits, '.', '_' or '-' (2-63 characters).")]
  public string Sku { get; set; } = "";
  [Range(0, 100000)] public int InitialStock { get; set; }
}

public record AdminProductSummaryDto(int Id, string Sku, string Name, string Brand, decimal Price, string CategoryName, bool IsActive, int Available);

public record AdminProductDto(
  int Id, string Sku, string Name, string Brand, string Description, decimal Price, int CategoryId, bool IsActive,
  IReadOnlyList<SpecDto> Specs, int QuantityOnHand, int QuantityReserved);

public class CategoryRequest
{
  [Required, StringLength(128)] public string Name { get; set; } = "";
  [Required, StringLength(64), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Use lowercase letters, digits and single hyphens.")]
  public string Slug { get; set; } = "";
}

public record AdminCategoryDto(int Id, string Slug, string Name, int ProductCount);

// ----- inventory -----

public class SetStockRequest
{
  [Range(0, 1_000_000)] public int QuantityOnHand { get; set; }
}

public record InventoryItemDto(int ProductId, string Sku, string Name, int QuantityOnHand, int QuantityReserved, int Available);

// ----- orders -----

public record AdminOrderSummaryDto(Guid Id, string OrderNumber, OrderStatus Status, DateTimeOffset CreatedAt, string CustomerEmail, decimal Total, int ItemCount);

public record AdminPaymentDto(PaymentStatus Status, decimal Amount, string? ProviderReference, string? DeclineReason, DateTimeOffset CreatedAt);

public record AdminOrderDetailDto(
  Store.Api.Orders.OrderDetailDto Order, string CustomerEmail, AdminPaymentDto? Payment);

// ----- dashboard / operations -----

public record LowStockDto(int ProductId, string Sku, string Name, int Available);

public record DashboardDto(
  IReadOnlyDictionary<string, int> OrdersByStatus, decimal Revenue, int ActiveProducts,
  IReadOnlyList<LowStockDto> LowStock, int StuckOrders);

public record DeadLetterDto(
  string Queue, long SequenceNumber, string MessageId, string? Subject, string? Reason, string? Description,
  int DeliveryCount, DateTimeOffset EnqueuedAt, string Body, Guid? OrderId);
