namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class SubOrderItemConfiguration : IEntityTypeConfiguration<SubOrderItem>
{
    public void Configure(EntityTypeBuilder<SubOrderItem> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProductNameSnapshot).HasMaxLength(500).IsRequired();
        builder.Property(x => x.SkuCodeSnapshot).HasMaxLength(100);
        builder.Property(x => x.SkuAttributesSnapshot).HasColumnType("jsonb");
        builder.Property(x => x.ImageUrlSnapshot).HasMaxLength(1000);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.UnitPriceAfterDiscount).HasPrecision(18, 2);
        builder.Property(x => x.LineTotal).HasPrecision(18, 2);

        builder.HasOne(i => i.SubOrder)
            .WithMany(s => s.Items)
            .HasForeignKey(i => i.SubOrderId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
