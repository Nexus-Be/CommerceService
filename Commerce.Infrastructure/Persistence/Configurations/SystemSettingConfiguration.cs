namespace Commerce.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Commerce.Domain.Entities;

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SettingKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.SettingKey).IsUnique();
        builder.Property(x => x.SettingValue).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
