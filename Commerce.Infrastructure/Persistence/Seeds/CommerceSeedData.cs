namespace Commerce.Infrastructure.Persistence.Seeds;

using Microsoft.EntityFrameworkCore;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;

public static class CommerceSeedData
{
    public static void SeedDiscountCodes(ModelBuilder modelBuilder)
    {
        var welcomeVoucherId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var seedDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var endDate = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        modelBuilder.Entity<DiscountCode>().HasData(
            new DiscountCode
            {
                Id = welcomeVoucherId,
                Code = "WELCOME2026",
                Name = "Khuyến mại chào mừng thành viên mới",
                ScopeType = DiscountScopeType.System,
                DiscountType = DiscountType.Fixed,
                DiscountValue = 50000m,
                MaxDiscountAmount = 50000m,
                MinOrderAmount = 200000m,
                MaxUsesTotal = 10000,
                MaxUsesPerUser = 1,
                UsedCount = 0,
                ReservedCount = 0,
                IsVisible = true,
                Status = DiscountStatus.Active,
                StartsAt = seedDate,
                EndsAt = endDate,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );
    }

    public static void SeedSystemSettings(ModelBuilder modelBuilder)
    {
        var seedDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        modelBuilder.Entity<SystemSetting>().HasData(
            new SystemSetting
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111101"),
                SettingKey = "CART_EXPIRATION_DAYS",
                SettingValue = "7",
                ValueType = SettingValueType.Integer,
                Category = SettingCategory.Timing,
                Description = "Số ngày không hoạt động trước khi giỏ hàng thành ABANDONED",
                IsEditable = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new SystemSetting
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111102"),
                SettingKey = "CHECKOUT_TIMEOUT_MINUTES",
                SettingValue = "15",
                ValueType = SettingValueType.Integer,
                Category = SettingCategory.Timing,
                Description = "Hạn thanh toán sau khi tạo đơn tổng (phút)",
                IsEditable = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new SystemSetting
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111103"),
                SettingKey = "SELLER_RESPOND_HOURS",
                SettingValue = "24",
                ValueType = SettingValueType.Integer,
                Category = SettingCategory.Timing,
                Description = "Hạn người bán phản hồi đơn đã thanh toán (giờ)",
                IsEditable = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new SystemSetting
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111104"),
                SettingKey = "ORDER_AUTO_COMPLETE_DAYS",
                SettingValue = "7",
                ValueType = SettingValueType.Integer,
                Category = SettingCategory.Timing,
                Description = "Số ngày tự hoàn tất đơn đã giao thành công",
                IsEditable = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new SystemSetting
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111105"),
                SettingKey = "PAYMENT_RETRY_LIMIT",
                SettingValue = "3",
                ValueType = SettingValueType.Integer,
                Category = SettingCategory.System,
                Description = "Số lần thử thanh toán tối đa cho mỗi đơn tổng",
                IsEditable = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new SystemSetting
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111106"),
                SettingKey = "MAX_QTY_PER_ITEM",
                SettingValue = "99",
                ValueType = SettingValueType.Integer,
                Category = SettingCategory.System,
                Description = "Số lượng tối đa cho mỗi sản phẩm trong giỏ hàng",
                IsEditable = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new SystemSetting
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111107"),
                SettingKey = "MAX_CART_ITEMS",
                SettingValue = "50",
                ValueType = SettingValueType.Integer,
                Category = SettingCategory.System,
                Description = "Số dòng sản phẩm tối đa trong giỏ hàng",
                IsEditable = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );
    }
}
