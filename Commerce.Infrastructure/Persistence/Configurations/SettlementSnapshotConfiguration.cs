namespace Commerce.Infrastructure.Persistence.Configurations;

using Commerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class SettlementSnapshotConfiguration : IEntityTypeConfiguration<SettlementSnapshot>
{
    public void Configure(EntityTypeBuilder<SettlementSnapshot> builder)
    {
        builder.ToTable("SettlementSnapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BusinessType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.LastErrorCode).HasMaxLength(100);
        builder.Property(x => x.LastError).HasMaxLength(500);
        foreach (var name in new[]
                 {
                     nameof(SettlementSnapshot.SellerReceiveAmount), nameof(SettlementSnapshot.PlatformFeeAmount),
                     nameof(SettlementSnapshot.ShippingAmount), nameof(SettlementSnapshot.SubsidyAmount),
                     nameof(SettlementSnapshot.OrderEscrowAmount), nameof(SettlementSnapshot.DepositEscrowAmount)
                 })
        {
            builder.Property(name).HasPrecision(18, 2);
        }

        // One payout payload per business type and sub-order: two processes capturing the same order race on this index and share the winner.
        builder.HasIndex(x => new { x.BusinessType, x.SubOrderId }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextRetryAt });

        // The captured payload is evidence: only the retry state may change after the first save.
        foreach (var name in new[]
                 {
                     nameof(SettlementSnapshot.BusinessType), nameof(SettlementSnapshot.OrderId), nameof(SettlementSnapshot.SubOrderId),
                     nameof(SettlementSnapshot.SellerId), nameof(SettlementSnapshot.SellerReceiveAmount), nameof(SettlementSnapshot.PlatformFeeAmount),
                     nameof(SettlementSnapshot.ShippingAmount), nameof(SettlementSnapshot.SubsidyAmount), nameof(SettlementSnapshot.OrderEscrowAmount),
                     nameof(SettlementSnapshot.DepositEscrowAmount), nameof(SettlementSnapshot.PayloadVersion)
                 })
        {
            builder.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
