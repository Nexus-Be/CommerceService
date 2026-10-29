namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.ReasonCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ReasonNote).HasMaxLength(1000);
        builder.Property(x => x.FailureCode).HasMaxLength(100);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();

        builder.HasOne(r => r.SubOrder)
            .WithMany(s => s.Refunds)
            .HasForeignKey(r => r.SubOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.AuctionOrder)
            .WithMany(ao => ao.Refunds)
            .HasForeignKey(r => r.AuctionOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Order)
            .WithMany(o => o.Refunds)
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Cancellation)
            .WithMany(c => c.Refunds)
            .HasForeignKey(r => r.CancellationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
