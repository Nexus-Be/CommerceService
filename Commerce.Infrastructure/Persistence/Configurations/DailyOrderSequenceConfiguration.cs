namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class DailyOrderSequenceConfiguration : IEntityTypeConfiguration<DailyOrderSequence>
{
    public void Configure(EntityTypeBuilder<DailyOrderSequence> builder)
    {
        builder.HasKey(x => new { x.SeqDate, x.SeqType });
        builder.Property(x => x.LastValue).IsRequired();
    }
}
