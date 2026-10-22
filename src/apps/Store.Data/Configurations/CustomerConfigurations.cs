using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Store.Domain.Customers;
using Store.Domain.Shopping;

namespace Store.Data.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
  public void Configure(EntityTypeBuilder<Cart> builder)
  {
    builder.Property(c => c.UserId).HasMaxLength(450);
    builder.HasIndex(c => c.UserId).IsUnique();
    builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    builder.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
  }
}

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
  public void Configure(EntityTypeBuilder<CartItem> builder)
  {
    builder.HasKey(i => new { i.CartId, i.ProductId });
    builder.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
  }
}

public class AddressConfiguration : IEntityTypeConfiguration<Address>
{
  public void Configure(EntityTypeBuilder<Address> builder)
  {
    builder.Property(a => a.UserId).HasMaxLength(450);
    builder.Property(a => a.FullName).HasMaxLength(128);
    builder.Property(a => a.Line1).HasMaxLength(200);
    builder.Property(a => a.Line2).HasMaxLength(200);
    builder.Property(a => a.City).HasMaxLength(100);
    builder.Property(a => a.Region).HasMaxLength(100);
    builder.Property(a => a.PostalCode).HasMaxLength(20);
    builder.Property(a => a.CountryCode).HasMaxLength(2).IsFixedLength();
    builder.Property(a => a.Phone).HasMaxLength(32);
    builder.HasIndex(a => a.UserId);
    builder.HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
  }
}
