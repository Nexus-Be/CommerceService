using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations;

/// <summary>
/// Records which seller payout protocol each order uses (1 = legacy route, 2 = atomic allocation command).
/// Existing orders default to 1. Idempotent.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261121141000_AddSettlementProtocolVersion")]
public sealed class AddSettlementProtocolVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""SettlementProtocolVersion"" integer NOT NULL DEFAULT 1;");
        migrationBuilder.Sql(@"ALTER TABLE ""AuctionOrders"" ADD COLUMN IF NOT EXISTS ""SettlementProtocolVersion"" integer NOT NULL DEFAULT 1;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""SettlementProtocolVersion"";");
        migrationBuilder.Sql(@"ALTER TABLE ""AuctionOrders"" DROP COLUMN IF EXISTS ""SettlementProtocolVersion"";");
    }
}
