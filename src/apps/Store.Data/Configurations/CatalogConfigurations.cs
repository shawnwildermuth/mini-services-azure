using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Store.Domain.Catalog;

namespace Store.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
  public void Configure(EntityTypeBuilder<Category> builder)
  {
    builder.Property(c => c.Slug).HasMaxLength(64);
    builder.Property(c => c.Name).HasMaxLength(128);
    builder.HasIndex(c => c.Slug).IsUnique();
  }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
  public void Configure(EntityTypeBuilder<Product> builder)
  {
    builder.Property(p => p.Sku).HasMaxLength(64);
    builder.Property(p => p.Name).HasMaxLength(256);
    builder.Property(p => p.Brand).HasMaxLength(128);
    builder.Property(p => p.Description).HasMaxLength(2000);
    builder.Property(p => p.Price).HasPrecision(18, 2);
    builder.Property(p => p.ImageUrl).HasMaxLength(512);
    builder.HasIndex(p => p.Sku).IsUnique();
    builder.HasIndex(p => new { p.CategoryId, p.IsActive });
    builder.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId);
    builder.HasMany(p => p.Specs).WithOne().HasForeignKey(s => s.ProductId);
    builder.HasOne(p => p.Inventory).WithOne().HasForeignKey<Inventory>(i => i.ProductId);
  }
}

public class ProductSpecConfiguration : IEntityTypeConfiguration<ProductSpec>
{
  public void Configure(EntityTypeBuilder<ProductSpec> builder)
  {
    builder.HasKey(s => new { s.ProductId, s.Name });
    builder.Property(s => s.Name).HasMaxLength(64);
    builder.Property(s => s.Value).HasMaxLength(128);
    builder.HasIndex(s => new { s.Name, s.Value });
  }
}

public class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
  public void Configure(EntityTypeBuilder<Inventory> builder)
  {
    builder.HasKey(i => i.ProductId);
    builder.Ignore(i => i.Available);
    builder.Property(i => i.RowVersion).IsRowVersion();
  }
}
