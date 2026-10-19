namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class OrderCancellationConfiguration : IEntityTypeConfiguration<OrderCancellation>
{
    public void Configure(EntityTypeBuilder<OrderCancellation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActorRole).HasConversion<string>();
        builder.Property(x => x.ReasonCode).HasConversion<string>();
        builder.Property(x => x.ReasonNote).HasMaxLength(1000);

        builder.HasOne(c => c.Order)
            .WithOne(o => o.Cancellation)
            .HasForeignKey<OrderCancellation>(c => c.OrderId);

        builder.HasOne(c => c.AuctionOrder)
            .WithOne(ao => ao.Cancellation)
            .HasForeignKey<OrderCancellation>(c => c.AuctionOrderId);
    }
}
