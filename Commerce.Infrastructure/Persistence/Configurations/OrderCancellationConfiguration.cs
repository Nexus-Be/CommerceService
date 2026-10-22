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

        builder.HasOne(c => c.SubOrder)
            .WithOne(s => s.Cancellation)
            .HasForeignKey<OrderCancellation>(c => c.SubOrderId);

        builder.HasOne(c => c.AuctionOrder)
            .WithOne(ao => ao.Cancellation)
            .HasForeignKey<OrderCancellation>(c => c.AuctionOrderId);

        builder.HasMany(c => c.Refunds)
            .WithOne(r => r.Cancellation)
            .HasForeignKey(r => r.CancellationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
