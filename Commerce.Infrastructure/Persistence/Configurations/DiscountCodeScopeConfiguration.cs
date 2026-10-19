namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class DiscountCodeScopeConfiguration : IEntityTypeConfiguration<DiscountCodeScope>
{
    public void Configure(EntityTypeBuilder<DiscountCodeScope> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ScopeRefType).HasConversion<string>();

        builder.HasOne(s => s.DiscountCode)
            .WithMany(d => d.Scopes)
            .HasForeignKey(s => s.DiscountCodeId);
    }
}
