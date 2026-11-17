namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class NotificationOutboxItemConfiguration : IEntityTypeConfiguration<NotificationOutboxItem>
{
    public void Configure(EntityTypeBuilder<NotificationOutboxItem> builder)
    {
        builder.ToTable("NotificationOutbox");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasMaxLength(60).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("text");
        builder.Property(x => x.LastError).HasMaxLength(500);

        builder.HasIndex(x => x.EventId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextRetryAt });
        builder.HasIndex(x => x.SubOrderId);
    }
}
