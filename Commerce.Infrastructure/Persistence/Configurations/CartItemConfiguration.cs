namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProductNameSnapshot).HasMaxLength(500).IsRequired();
        builder.Property(x => x.UnitPriceSnapshot).HasPrecision(18, 2);

        builder.HasIndex(x => new { x.CartId, x.SkuId }).IsUnique();
        builder.HasIndex(x => x.ProductId);

        builder.HasOne(i => i.Cart)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.CartId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
