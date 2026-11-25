using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations;

/// <summary>
/// Keeps the carrier's reasons for failed deliveries on the sub-order, and the state of the reputation penalty owed when a buyer refused a
/// parcel. Existing rows default to "no penalty owed". Idempotent.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261122120000_AddBuyerRefusalEvidence")]
public sealed class AddBuyerRefusalEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""DeliveryFailureCodes"" character varying(500) NULL;
ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""LastDeliveryFailureReason"" character varying(500) NULL;
ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""LastDeliveryFailureAt"" timestamp with time zone NULL;
ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""ReturnClassification"" character varying(32) NULL;
ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""BuyerRefusalPenaltyStatus"" integer NOT NULL DEFAULT 0;
ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""BuyerRefusalPenaltyAttempts"" integer NOT NULL DEFAULT 0;
ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""BuyerRefusalPenaltyAppliedAt"" timestamp with time zone NULL;
CREATE INDEX IF NOT EXISTS ""IX_SubOrders_BuyerRefusalPenaltyStatus_Pending"" ON ""SubOrders"" (""BuyerRefusalPenaltyStatus"") WHERE ""BuyerRefusalPenaltyStatus"" = 1;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""IX_SubOrders_BuyerRefusalPenaltyStatus_Pending"";
ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""BuyerRefusalPenaltyAppliedAt"";
ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""BuyerRefusalPenaltyAttempts"";
ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""BuyerRefusalPenaltyStatus"";
ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""ReturnClassification"";
ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""LastDeliveryFailureAt"";
ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""LastDeliveryFailureReason"";
ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""DeliveryFailureCodes"";");
    }
}
