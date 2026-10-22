using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Store.Domain.Messaging;
using Store.Domain.Orders;

namespace Store.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
  public void Configure(EntityTypeBuilder<Order> builder)
  {
    builder.Property(o => o.OrderNumber).HasMaxLength(32);
    builder.Property(o => o.UserId).HasMaxLength(450);
    builder.Property(o => o.CustomerEmail).HasMaxLength(256);
    builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
    builder.Property(o => o.Carrier).HasConversion<string>().HasMaxLength(8);
    builder.Property(o => o.Subtotal).HasPrecision(18, 2);
    builder.Property(o => o.ShippingCost).HasPrecision(18, 2);
    builder.Property(o => o.Total).HasPrecision(18, 2);
    builder.Property(o => o.IdempotencyKey).HasMaxLength(64);
    builder.Property(o => o.RowVersion).IsRowVersion();

    builder.Property(o => o.ShipToName).HasMaxLength(128);
    builder.Property(o => o.ShipToLine1).HasMaxLength(200);
    builder.Property(o => o.ShipToLine2).HasMaxLength(200);
    builder.Property(o => o.ShipToCity).HasMaxLength(100);
    builder.Property(o => o.ShipToRegion).HasMaxLength(100);
    builder.Property(o => o.ShipToPostalCode).HasMaxLength(20);
    builder.Property(o => o.ShipToCountryCode).HasMaxLength(2).IsFixedLength();
    builder.Property(o => o.ShipToPhone).HasMaxLength(32);

    builder.HasIndex(o => o.OrderNumber).IsUnique();
    builder.HasIndex(o => new { o.UserId, o.CreatedAt });
    builder.HasIndex(o => o.Status);
    builder.HasIndex(o => new { o.UserId, o.IdempotencyKey }).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");

    builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
  }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
  public void Configure(EntityTypeBuilder<OrderItem> builder)
  {
    builder.Property(i => i.Sku).HasMaxLength(64);
    builder.Property(i => i.Name).HasMaxLength(256);
    builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
    builder.Ignore(i => i.LineTotal);
    builder.HasIndex(i => i.OrderId);
    // Products are never hard-deleted while orders reference them.
    builder.HasOne<Store.Domain.Catalog.Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
  }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
  public void Configure(EntityTypeBuilder<Payment> builder)
  {
    builder.Property(p => p.Amount).HasPrecision(18, 2);
    builder.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();
    builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
    builder.Property(p => p.ProviderReference).HasMaxLength(128);
    builder.Property(p => p.DeclineReason).HasMaxLength(256);
    builder.HasIndex(p => p.OrderId).IsUnique();
    builder.HasOne<Order>().WithMany().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Restrict);
  }
}

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
  public void Configure(EntityTypeBuilder<Shipment> builder)
  {
    builder.Property(s => s.Carrier).HasConversion<string>().HasMaxLength(8);
    builder.Property(s => s.TrackingNumber).HasMaxLength(64);
    builder.Property(s => s.Service).HasMaxLength(64);
    builder.HasIndex(s => s.OrderId).IsUnique();
    builder.HasOne<Order>().WithMany().HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Restrict);
  }
}

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
  public void Configure(EntityTypeBuilder<OutboxMessage> builder)
  {
    builder.Property(m => m.Destination).HasMaxLength(128);
    builder.Property(m => m.Type).HasMaxLength(128);
    builder.Property(m => m.LastError).HasMaxLength(2000);
    // The dispatcher scans only unprocessed rows.
    builder.HasIndex(m => m.NextAttemptAt).HasFilter("[ProcessedAt] IS NULL");
  }
}
