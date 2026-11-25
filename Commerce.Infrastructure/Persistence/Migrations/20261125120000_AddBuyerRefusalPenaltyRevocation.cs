using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations;

/// <summary>
/// Keeps who withdrew a buyer refusal penalty, why, when it was requested and confirmed, and the points User gave back. Existing rows have
/// no withdrawal. Idempotent.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261125120000_AddBuyerRefusalPenaltyRevocation")]
public sealed class AddBuyerRefusalPenaltyRevocation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""BuyerRefusalPenaltyRevokedBy"" uuid NULL;
ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""BuyerRefusalPenaltyRevokeReason"" character varying(500) NULL;
ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""BuyerRefusalPenaltyRevokeRequestedAt"" timestamp with time zone NULL;
ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""BuyerRefusalPenaltyRevokedAt"" timestamp with time zone NULL;
ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""BuyerRefusalPenaltyRestoredPoints"" integer NULL;
CREATE INDEX IF NOT EXISTS ""IX_SubOrders_BuyerRefusalPenaltyStatus_RevokePending"" ON ""SubOrders"" (""BuyerRefusalPenaltyStatus"") WHERE ""BuyerRefusalPenaltyStatus"" = 4;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""IX_SubOrders_BuyerRefusalPenaltyStatus_RevokePending"";
ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""BuyerRefusalPenaltyRestoredPoints"";
ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""BuyerRefusalPenaltyRevokedAt"";
ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""BuyerRefusalPenaltyRevokeRequestedAt"";
ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""BuyerRefusalPenaltyRevokeReason"";
ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""BuyerRefusalPenaltyRevokedBy"";");
    }
}
