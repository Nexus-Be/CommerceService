namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OrderNo).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.OrderNo).IsUnique();
        builder.Property(x => x.Status).HasConversion<string>();
        builder.Property(x => x.ShippingAddress).HasMaxLength(2000);
        builder.Property(x => x.ContactPhone).HasMaxLength(50);
        builder.Property(x => x.ItemsAmount).HasPrecision(18, 2);
        builder.Property(x => x.ProductDiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.ShopDiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.PlatformMarkupAmount).HasPrecision(18, 2);
        builder.Property(x => x.SystemDiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.ShippingFeeAmount).HasPrecision(18, 2);
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.SellerReceiveAmount).HasPrecision(18, 2);
        builder.Property(x => x.PlatformFeeAmount).HasPrecision(18, 2);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();

        builder.HasOne(o => o.CheckoutGroup)
            .WithMany(g => g.Orders)
            .HasForeignKey(o => o.CheckoutGroupId);

        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId);

        builder.HasMany(o => o.StatusHistories)
            .WithOne(h => h.Order)
            .HasForeignKey(h => h.OrderId);

        builder.HasOne(o => o.Invoice)
            .WithOne(inv => inv.Order)
            .HasForeignKey<OrderInvoice>(inv => inv.OrderId);

        builder.HasOne(o => o.Cancellation)
            .WithOne(c => c.Order)
            .HasForeignKey<OrderCancellation>(c => c.OrderId);

        builder.HasMany(o => o.DiscountCodeUsages)
            .WithOne(u => u.Order)
            .HasForeignKey(u => u.OrderId);
    }
}
