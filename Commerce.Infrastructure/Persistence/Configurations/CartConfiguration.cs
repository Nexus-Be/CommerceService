namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SubtotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();

        builder.HasMany(c => c.Items)
            .WithOne(i => i.Cart)
            .HasForeignKey(i => i.CartId);

        builder.HasMany(c => c.Orders)
            .WithOne(o => o.Cart)
            .HasForeignKey(o => o.CartId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
