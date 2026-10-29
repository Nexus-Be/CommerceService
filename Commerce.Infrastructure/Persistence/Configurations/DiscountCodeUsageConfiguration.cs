namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class DiscountCodeUsageConfiguration : IEntityTypeConfiguration<DiscountCodeUsage>
{
    public void Configure(EntityTypeBuilder<DiscountCodeUsage> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DiscountAmount).HasPrecision(18, 2);

        builder.HasOne(u => u.DiscountCode)
            .WithMany(d => d.Usages)
            .HasForeignKey(u => u.DiscountCodeId);

        builder.HasOne(u => u.Order)
            .WithMany(o => o.DiscountCodeUsages)
            .HasForeignKey(u => u.OrderId);

        builder.HasOne(u => u.SubOrder)
            .WithMany(s => s.DiscountCodeUsages)
            .HasForeignKey(u => u.SubOrderId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
