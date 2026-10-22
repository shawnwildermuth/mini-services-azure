using System.ComponentModel.DataAnnotations;
using Store.Domain.Orders;

namespace Store.Api.Orders;

public class PlaceOrderRequest
{
  [Range(1, int.MaxValue)]
  public int AddressId { get; set; }
}

public record QuoteDto(decimal Subtotal, decimal ShippingCost, decimal Total, Carrier Carrier);

public record OrderSummaryDto(Guid Id, string OrderNumber, OrderStatus Status, DateTimeOffset CreatedAt, decimal Total, int ItemCount);

public record OrderItemDto(string Sku, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);

public record ShipToDto(
  string Name, string Line1, string? Line2, string City, string? Region, string PostalCode, string CountryCode, string? Phone);

public record ShipmentDto(Carrier Carrier, string TrackingNumber, string Service, DateTimeOffset ShippedAt, DateTimeOffset EstimatedDelivery);

public record OrderDetailDto(
  Guid Id, string OrderNumber, OrderStatus Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
  decimal Subtotal, decimal ShippingCost, decimal Total, Carrier Carrier,
  ShipToDto ShipTo, IReadOnlyList<OrderItemDto> Items, ShipmentDto? Shipment);

public static class OrderMapping
{
  public static OrderDetailDto ToDetail(this Order o, Shipment? shipment = null) => new(
    o.Id, o.OrderNumber, o.Status, o.CreatedAt, o.UpdatedAt, o.Subtotal, o.ShippingCost, o.Total, o.Carrier,
    new ShipToDto(o.ShipToName, o.ShipToLine1, o.ShipToLine2, o.ShipToCity, o.ShipToRegion, o.ShipToPostalCode, o.ShipToCountryCode, o.ShipToPhone),
    o.Items.OrderBy(i => i.Name).Select(i => new OrderItemDto(i.Sku, i.Name, i.UnitPrice, i.Quantity, i.LineTotal)).ToList(),
    shipment is null ? null : new ShipmentDto(shipment.Carrier, shipment.TrackingNumber, shipment.Service, shipment.ShippedAt, shipment.EstimatedDelivery));
}
