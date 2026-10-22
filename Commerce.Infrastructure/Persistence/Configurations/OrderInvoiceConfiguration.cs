namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class OrderInvoiceConfiguration : IEntityTypeConfiguration<OrderInvoice>
{
    public void Configure(EntityTypeBuilder<OrderInvoice> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.InvoiceNo).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.InvoiceNo).IsUnique();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.FileUrl).HasMaxLength(1000);
        builder.Property(x => x.Status).HasConversion<string>();

        builder.HasOne(inv => inv.Order)
            .WithOne(o => o.Invoice)
            .HasForeignKey<OrderInvoice>(inv => inv.OrderId);

        builder.HasOne(inv => inv.SubOrder)
            .WithOne(s => s.Invoice)
            .HasForeignKey<OrderInvoice>(inv => inv.SubOrderId);

        builder.HasOne(inv => inv.AuctionOrder)
            .WithOne(ao => ao.Invoice)
            .HasForeignKey<OrderInvoice>(inv => inv.AuctionOrderId);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
