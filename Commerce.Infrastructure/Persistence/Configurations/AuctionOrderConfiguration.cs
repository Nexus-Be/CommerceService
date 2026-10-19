namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class AuctionOrderConfiguration : IEntityTypeConfiguration<AuctionOrder>
{
    public void Configure(EntityTypeBuilder<AuctionOrder> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OrderNo).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.OrderNo).IsUnique();
        builder.Property(x => x.ProductNameSnapshot).HasMaxLength(500).IsRequired();
        builder.Property(x => x.WinningAmount).HasPrecision(18, 2);
        builder.Property(x => x.ShippingAddress).HasMaxLength(2000);
        builder.Property(x => x.ContactPhone).HasMaxLength(50);
        builder.Property(x => x.ShippingFeeAmount).HasPrecision(18, 2);
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.SellerReceiveAmount).HasPrecision(18, 2);
        builder.Property(x => x.PlatformFeeAmount).HasPrecision(18, 2);
        builder.Property(x => x.Status).HasConversion<string>();
        builder.Property(x => x.RowVersion).IsConcurrencyToken();

        builder.HasMany(ao => ao.StatusHistories)
            .WithOne(h => h.AuctionOrder)
            .HasForeignKey(h => h.AuctionOrderId);

        builder.HasOne(ao => ao.Invoice)
            .WithOne(inv => inv.AuctionOrder)
            .HasForeignKey<OrderInvoice>(inv => inv.AuctionOrderId);

        builder.HasOne(ao => ao.Cancellation)
            .WithOne(c => c.AuctionOrder)
            .HasForeignKey<OrderCancellation>(c => c.AuctionOrderId);
    }
}
