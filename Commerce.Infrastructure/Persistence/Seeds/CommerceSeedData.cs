namespace Commerce.Infrastructure.Persistence.Seeds;

using Microsoft.EntityFrameworkCore;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;

public static class CommerceSeedData
{
    public static void SeedDiscountCodes(ModelBuilder modelBuilder)
    {
        var welcomeVoucherId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        modelBuilder.Entity<DiscountCode>().HasData(
            new DiscountCode
            {
                Id = welcomeVoucherId,
                Code = "WELCOME2026",
                Name = "Khuyến mại chào mừng thành viên mới",
                ScopeType = DiscountScopeType.Global,
                DiscountType = DiscountType.FixedAmount,
                DiscountValue = 50000m,
                MaxDiscountAmount = 50000m,
                MinOrderAmount = 200000m,
                MaxUsesTotal = 10000,
                MaxUsesPerUser = 1,
                UsedCount = 0,
                ReservedCount = 0,
                IsDisabled = false,
                Status = DiscountStatus.Active,
                StartsAt = DateTimeOffset.UtcNow,
                EndsAt = DateTimeOffset.UtcNow.AddYears(1),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                IsDeleted = false
            }
        );
    }
}
