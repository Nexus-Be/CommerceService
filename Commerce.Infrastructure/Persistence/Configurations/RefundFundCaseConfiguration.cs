namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class RefundFundCaseConfiguration : IEntityTypeConfiguration<RefundFundCase>
{
    public void Configure(EntityTypeBuilder<RefundFundCase> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.SellerRecovered).HasPrecision(18, 2);
        builder.Property(x => x.SellerShortfall).HasPrecision(18, 2);
        builder.Property(x => x.RequestReason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.NeedsFinanceReason).HasMaxLength(100);
        builder.Property(x => x.ResolutionNote).HasMaxLength(500);
        // One case per sub-order, whatever report or admin asked for it.
        builder.HasIndex(x => x.SubOrderId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
