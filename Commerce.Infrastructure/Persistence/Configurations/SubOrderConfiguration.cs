namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class SubOrderConfiguration : IEntityTypeConfiguration<SubOrder>
{
    public void Configure(EntityTypeBuilder<SubOrder> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SubOrderNo).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.SubOrderNo).IsUnique();
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

        builder.HasOne(s => s.Order)
            .WithMany(o => o.SubOrders)
            .HasForeignKey(s => s.OrderId);

        builder.HasMany(s => s.Items)
            .WithOne(i => i.SubOrder)
            .HasForeignKey(i => i.SubOrderId);

        builder.HasMany(s => s.StatusHistories)
            .WithOne(h => h.SubOrder)
            .HasForeignKey(h => h.SubOrderId);

        builder.HasOne(s => s.Invoice)
            .WithOne(inv => inv.SubOrder)
            .HasForeignKey<OrderInvoice>(inv => inv.SubOrderId);

        builder.HasOne(s => s.Cancellation)
            .WithOne(c => c.SubOrder)
            .HasForeignKey<OrderCancellation>(c => c.SubOrderId);

        builder.HasMany(s => s.DiscountCodeUsages)
            .WithOne(u => u.SubOrder)
            .HasForeignKey(u => u.SubOrderId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
