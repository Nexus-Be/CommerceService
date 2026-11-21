using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations;

/// <summary>
/// Captures the platform fee rate on each order so a later rate change (or a cold cache) cannot alter
/// how an existing order is settled. Nullable: orders created before this migration have no recorded rate.
/// Idempotent so it is safe on databases where the columns were added by hand.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261121140000_AddPlatformFeePercentSnapshot")]
public sealed class AddPlatformFeePercentSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""PlatformFeePercent"" numeric(9,4) NULL;");
        migrationBuilder.Sql(@"ALTER TABLE ""AuctionOrders"" ADD COLUMN IF NOT EXISTS ""PlatformFeePercent"" numeric(9,4) NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""PlatformFeePercent"";");
        migrationBuilder.Sql(@"ALTER TABLE ""AuctionOrders"" DROP COLUMN IF EXISTS ""PlatformFeePercent"";");
    }
}
