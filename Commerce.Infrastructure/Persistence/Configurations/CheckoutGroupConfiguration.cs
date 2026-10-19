namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class CheckoutGroupConfiguration : IEntityTypeConfiguration<CheckoutGroup>
{
    public void Configure(EntityTypeBuilder<CheckoutGroup> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<string>();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();

        builder.HasOne(g => g.Cart)
            .WithMany(c => c.CheckoutGroups)
            .HasForeignKey(g => g.CartId);

        builder.HasMany(g => g.Orders)
            .WithOne(o => o.CheckoutGroup)
            .HasForeignKey(o => o.CheckoutGroupId);

        builder.HasMany(g => g.DiscountCodeUsages)
            .WithOne(u => u.CheckoutGroup)
            .HasForeignKey(u => u.CheckoutGroupId);
    }
}
