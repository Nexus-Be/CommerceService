namespace Commerce.Infrastructure.Persistence.Seeds;

using Microsoft.EntityFrameworkCore;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;

public static class CommerceSeedData
{
    public static void SeedAll(ModelBuilder modelBuilder)
    {
        SeedDiscountCodes(modelBuilder);
        SeedSystemSettings(modelBuilder);
        SeedCarts(modelBuilder);
        SeedOrders(modelBuilder);
    }

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

    public static void SeedCarts(ModelBuilder modelBuilder)
    {
        var seedDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var buyerUserId = Guid.Parse("dddddddd-4444-4444-4444-444444444444");
        var sellerUserId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");

        var cartId = Guid.Parse("c0000000-0000-0000-0000-000000000001");

        // 1. Buyer's Active Cart
        modelBuilder.Entity<Cart>().HasData(
            new Cart
            {
                Id = cartId,
                BuyerId = buyerUserId,
                Status = CartStatus.Active,
                ItemCount = 2,
                SubtotalAmount = 15900000m, // 15,000,000 + (450,000 * 2)
                LastActivityAt = seedDate,
                RowVersion = 1,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );

        // 2. Cart Items linking directly to real Products in Catalog
        modelBuilder.Entity<CartItem>().HasData(
            new CartItem
            {
                Id = Guid.Parse("c1000000-0000-0000-0000-000000000001"),
                CartId = cartId,
                ProductId = Guid.Parse("11000000-0000-0000-0000-000000000001"), // Smartphone Nexus X Pro 256GB
                SkuId = Guid.Parse("11100000-0000-0000-0000-000000000001"),     // NX-PHONE-256
                SellerId = sellerUserId,
                Quantity = 1,
                UnitPriceSnapshot = 15000000m,
                ProductNameSnapshot = "Smartphone Nexus X Pro 256GB Midnight Black",
                AddedAt = seedDate,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new CartItem
            {
                Id = Guid.Parse("c1000000-0000-0000-0000-000000000002"),
                CartId = cartId,
                ProductId = Guid.Parse("22000000-0000-0000-0000-000000000002"), // Áo Hoodie Nexus Unisex Cotton
                SkuId = Guid.Parse("22100000-0000-0000-0000-000000000002"),     // NX-HOODIE-L
                SellerId = sellerUserId,
                Quantity = 2,
                UnitPriceSnapshot = 450000m,
                ProductNameSnapshot = "Áo Hoodie Nexus Unisex Cotton - Đen / Size L",
                AddedAt = seedDate,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );
    }

    public static void SeedOrders(ModelBuilder modelBuilder)
    {
        var seedDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var buyerUserId = Guid.Parse("dddddddd-4444-4444-4444-444444444444");
        var sellerUserId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");

        var orderId = Guid.Parse("01000000-0000-0000-0000-000000000001");
        var subOrderId = Guid.Parse("02000000-0000-0000-0000-000000000001");

        // 1. Order (Paid / Completed)
        modelBuilder.Entity<Order>().HasData(
            new Order
            {
                Id = orderId,
                BuyerId = buyerUserId,
                Status = OrderStatus.Paid,
                TotalAmount = 850000m,
                PaymentAttemptCount = 1,
                PaymentDeadlineAt = seedDate.AddDays(1),
                RowVersion = 1,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );

        // 2. SubOrder
        modelBuilder.Entity<SubOrder>().HasData(
            new SubOrder
            {
                Id = subOrderId,
                SubOrderNo = "SUB-20260101-0001",
                OrderId = orderId,
                BuyerId = buyerUserId,
                SellerId = sellerUserId,
                Status = SubOrderStatus.Completed,
                PaymentMethod = PaymentMethod.Wallet,
                ShippingAddress = "{\"fullName\":\"John Buyer\",\"phone\":\"0900000004\",\"address\":\"123 Nguyen Trai, Thanh Xuan, Ha Noi\"}",
                ContactPhone = "0900000004",
                ItemsAmount = 850000m,
                ProductDiscountAmount = 0m,
                ShopDiscountAmount = 0m,
                PlatformMarkupAmount = 0m,
                SystemDiscountAmount = 0m,
                ShippingFeeAmount = 0m,
                TotalAmount = 850000m,
                SellerReceiveAmount = 807500m,
                PlatformFeeAmount = 42500m,
                PaymentDeadlineAt = seedDate.AddDays(1),
                PaidAt = seedDate,
                SellerAcceptedAt = seedDate,
                ShippedAt = seedDate,
                DeliveredAt = seedDate,
                CompletedAt = seedDate,
                RowVersion = 1,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );

        // 3. SubOrderItem (Product 3: Ấm Siêu Tốc Thông Minh Nexus 1.7L)
        modelBuilder.Entity<SubOrderItem>().HasData(
            new SubOrderItem
            {
                Id = Guid.Parse("03000000-0000-0000-0000-000000000001"),
                SubOrderId = subOrderId,
                ProductId = Guid.Parse("33000000-0000-0000-0000-000000000003"), // Ấm Siêu Tốc
                SkuId = Guid.Parse("33100000-0000-0000-0000-000000000003"),     // NX-KETTLE-17
                SellerId = sellerUserId,
                ProductNameSnapshot = "Ấm Siêu Tốc Thông Minh Nexus 1.7L Trắng Tinh Khôi",
                SkuCodeSnapshot = "NX-KETTLE-17",
                SkuAttributesSnapshot = "{}",
                ImageUrlSnapshot = "https://images.nexus.local/products/kettle-white.png",
                Quantity = 1,
                UnitPrice = 850000m,
                UnitPriceAfterDiscount = 850000m,
                LineTotal = 850000m,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );
    }
}
