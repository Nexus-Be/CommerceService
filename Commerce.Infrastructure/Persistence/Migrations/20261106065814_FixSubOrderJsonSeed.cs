using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixSubOrderJsonSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SubOrderItems",
                keyColumn: "Id",
                keyValue: new Guid("03000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "SubOrders",
                keyColumn: "Id",
                keyValue: new Guid("02000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Orders",
                keyColumn: "Id",
                keyValue: new Guid("01000000-0000-0000-0000-000000000001"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Orders",
                columns: new[] { "Id", "BuyerId", "CartId", "CreatedAt", "DeletedAt", "IsDeleted", "PaymentAttemptCount", "PaymentDeadlineAt", "RowVersion", "Status", "TotalAmount", "UpdatedAt", "WalletTransactionId" },
                values: new object[] { new Guid("01000000-0000-0000-0000-000000000001"), new Guid("dddddddd-4444-4444-4444-444444444444"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, false, 1, new DateTimeOffset(new DateTime(2026, 1, 2, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1L, 2, 850000m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });

            migrationBuilder.InsertData(
                table: "SubOrders",
                columns: new[] { "Id", "BuyerId", "CancelledAt", "CompletedAt", "ContactPhone", "CreatedAt", "DeletedAt", "DeliveredAt", "IsDeleted", "ItemsAmount", "OrderId", "PaidAt", "PaymentDeadlineAt", "PaymentMethod", "PlatformFeeAmount", "PlatformMarkupAmount", "ProductDiscountAmount", "RowVersion", "SellerAcceptedAt", "SellerId", "SellerReceiveAmount", "SellerRespondDeadlineAt", "ShippedAt", "ShippingAddress", "ShippingFeeAmount", "ShopDiscountAmount", "Status", "SubOrderNo", "SystemDiscountAmount", "TotalAmount", "UpdatedAt", "WalletTransactionId" },
                values: new object[] { new Guid("02000000-0000-0000-0000-000000000001"), new Guid("dddddddd-4444-4444-4444-444444444444"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "0900000004", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, 850000m, new Guid("01000000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new DateTimeOffset(new DateTime(2026, 1, 2, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0, 42500m, 0m, 0m, 1L, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("cccccccc-3333-3333-3333-333333333333"), 807500m, null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "{\"fullName\":\"John Buyer\",\"phone\":\"0900000004\",\"address\":\"123 Nguyen Trai, Thanh Xuan, Ha Noi\"}", 0m, 0m, 5, "SUB-20260101-0001", 0m, 850000m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });

            migrationBuilder.InsertData(
                table: "SubOrderItems",
                columns: new[] { "Id", "CreatedAt", "DeletedAt", "DiscountCodeId", "ImageUrlSnapshot", "IsDeleted", "LineTotal", "ProductId", "ProductNameSnapshot", "Quantity", "SellerId", "SkuAttributesSnapshot", "SkuCodeSnapshot", "SkuId", "SubOrderId", "UnitPrice", "UnitPriceAfterDiscount", "UpdatedAt" },
                values: new object[] { new Guid("03000000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, "https://images.nexus.local/products/kettle-white.png", false, 850000m, new Guid("33000000-0000-0000-0000-000000000003"), "Ấm Siêu Tốc Thông Minh Nexus 1.7L Trắng Tinh Khôi", 1, new Guid("cccccccc-3333-3333-3333-333333333333"), "{}", "NX-KETTLE-17", new Guid("33100000-0000-0000-0000-000000000003"), new Guid("02000000-0000-0000-0000-000000000001"), 850000m, 850000m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });
        }
    }
}
