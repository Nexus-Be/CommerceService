using Commerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261116120000_AddAuctionCallbackOutbox")]
public sealed class AddAuctionCallbackOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset?>("PayoutCompletedAt", "AuctionOrders",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("AuctionCallbackStatus", "OrderStatusHistories",
            type: "character varying(30)", maxLength: 30, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset?>("AuctionCallbackSentAt", "OrderStatusHistories",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.CreateIndex("IX_OrderStatusHistories_AuctionCallbackSentAt_CreatedAt",
            "OrderStatusHistories", new[] { "AuctionCallbackSentAt", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_AuctionOrders_OneActiveOrderPerAuction", "AuctionOrders",
            "AuctionId", unique: true, filter: "\"Status\" <> 6 AND \"IsDeleted\" = false");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("PayoutCompletedAt", "AuctionOrders");
        migrationBuilder.DropIndex("IX_OrderStatusHistories_AuctionCallbackSentAt_CreatedAt", "OrderStatusHistories");
        migrationBuilder.DropIndex("IX_AuctionOrders_OneActiveOrderPerAuction", "AuctionOrders");
        migrationBuilder.DropColumn("AuctionCallbackStatus", "OrderStatusHistories");
        migrationBuilder.DropColumn("AuctionCallbackSentAt", "OrderStatusHistories");
    }
}
