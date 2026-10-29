namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class DiscountCodeConfiguration : IEntityTypeConfiguration<DiscountCode>
{
    public void Configure(EntityTypeBuilder<DiscountCode> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.DiscountValue).HasPrecision(18, 2);
        builder.Property(x => x.MaxDiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.MinOrderAmount).HasPrecision(18, 2);
        builder.Property(x => x.IsVisible).HasDefaultValue(true);

        builder.HasMany(d => d.Scopes)
            .WithOne(s => s.DiscountCode)
            .HasForeignKey(s => s.DiscountCodeId);

        builder.HasMany(d => d.Usages)
            .WithOne(u => u.DiscountCode)
            .HasForeignKey(u => u.DiscountCodeId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
