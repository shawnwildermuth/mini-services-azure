using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Store.Domain.Catalog;
using Store.Domain.Customers;
using Store.Domain.Messaging;
using Store.Domain.Orders;
using Store.Domain.Shopping;

namespace Store.Data;

public class StoreDbContext(DbContextOptions<StoreDbContext> options) : IdentityDbContext<AppUser>(options), IDataProtectionKeyContext
{
  /// <summary>
  /// The keys that sign customers' bearer/refresh tokens. Kept in the database so every API instance (and every
  /// restart) shares them; otherwise customers would be signed out whenever a container is replaced.
  /// </summary>
  public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

  public DbSet<Category> Categories => Set<Category>();
  public DbSet<Product> Products => Set<Product>();
  public DbSet<ProductSpec> ProductSpecs => Set<ProductSpec>();
  public DbSet<Inventory> Inventory => Set<Inventory>();
  public DbSet<Cart> Carts => Set<Cart>();
  public DbSet<CartItem> CartItems => Set<CartItem>();
  public DbSet<Address> Addresses => Set<Address>();
  public DbSet<Order> Orders => Set<Order>();
  public DbSet<OrderItem> OrderItems => Set<OrderItem>();
  public DbSet<Payment> Payments => Set<Payment>();
  public DbSet<Shipment> Shipments => Set<Shipment>();
  public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreDbContext).Assembly);
  }
}
