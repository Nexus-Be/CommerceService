namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();

        builder.HasOne(o => o.Cart)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CartId);

        builder.HasMany(o => o.SubOrders)
            .WithOne(s => s.Order)
            .HasForeignKey(s => s.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.DiscountCodeUsages)
            .WithOne(u => u.Order)
            .HasForeignKey(u => u.OrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.Invoice)
            .WithOne(inv => inv.Order)
            .HasForeignKey<OrderInvoice>(inv => inv.OrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(o => o.Refunds)
            .WithOne(r => r.Order)
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
