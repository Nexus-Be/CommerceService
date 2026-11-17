using Commerce.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the deposit reference and the shipping weight used for an auction order. DepositAmount, RemainingAmount and OrderSource
/// come from 20261115160000_AddAuctionDepositAndSourceFields, so they are not added again here.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261116090000_AddAuctionDepositSnapshot")]
public sealed class AddAuctionDepositSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid?>("DepositId", "AuctionOrders", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<int>("WeightGramsSnapshot", "AuctionOrders", type: "integer", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("DepositId", "AuctionOrders");
        migrationBuilder.DropColumn("WeightGramsSnapshot", "AuctionOrders");
    }
}
