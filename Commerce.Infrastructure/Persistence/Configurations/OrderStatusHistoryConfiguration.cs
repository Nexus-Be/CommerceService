namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FromStatus).HasConversion<string>();
        builder.Property(x => x.ToStatus).HasConversion<string>();
        builder.Property(x => x.ActorRole).HasConversion<string>();
        builder.Property(x => x.Reason).HasMaxLength(1000);

        builder.HasOne(h => h.Order)
            .WithMany(o => o.StatusHistories)
            .HasForeignKey(h => h.OrderId);

        builder.HasOne(h => h.AuctionOrder)
            .WithMany(ao => ao.StatusHistories)
            .HasForeignKey(h => h.AuctionOrderId);
    }
}
