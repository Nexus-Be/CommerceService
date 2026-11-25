namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class DeliveryFailureAttemptConfiguration : IEntityTypeConfiguration<DeliveryFailureAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryFailureAttempt> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TrackingCode).HasMaxLength(100);
        builder.Property(x => x.ReasonCode).HasMaxLength(100);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.EventType).HasMaxLength(50);
        builder.Property(x => x.DedupeKey).HasMaxLength(250);
        builder.HasIndex(x => x.SubOrderId);
        // A callback the carrier repeats is the same attempt.
        builder.HasIndex(x => new { x.SubOrderId, x.DedupeKey }).IsUnique().HasFilter("\"DedupeKey\" IS NOT NULL");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
