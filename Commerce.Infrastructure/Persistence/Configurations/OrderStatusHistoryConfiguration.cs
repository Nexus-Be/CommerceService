namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(1000);

        builder.HasOne(h => h.SubOrder)
            .WithMany(s => s.StatusHistories)
            .HasForeignKey(h => h.SubOrderId);

        builder.HasOne(h => h.AuctionOrder)
            .WithMany(ao => ao.StatusHistories)
            .HasForeignKey(h => h.AuctionOrderId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
