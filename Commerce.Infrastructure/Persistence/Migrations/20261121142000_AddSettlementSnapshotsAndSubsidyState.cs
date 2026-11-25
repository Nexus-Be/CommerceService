using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations;

/// <summary>
/// Settlement snapshots (the stored payload and retry state of each seller payout, unique per business type and sub-order) and the
/// subsidy lifecycle state of a sub-order. Only additions; existing sub-orders get SubsidyState 0 (None). Idempotent.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261121142000_AddSettlementSnapshotsAndSubsidyState")]
public sealed class AddSettlementSnapshotsAndSubsidyState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"ALTER TABLE ""SubOrders"" ADD COLUMN IF NOT EXISTS ""SubsidyState"" integer NOT NULL DEFAULT 0;");
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""SettlementSnapshots"" (
    ""Id"" uuid NOT NULL,
    ""BusinessType"" character varying(30) NOT NULL,
    ""OrderId"" uuid NOT NULL,
    ""SubOrderId"" uuid NOT NULL,
    ""SellerId"" uuid NOT NULL,
    ""SellerReceiveAmount"" numeric(18,2) NOT NULL,
    ""PlatformFeeAmount"" numeric(18,2) NOT NULL,
    ""ShippingAmount"" numeric(18,2) NOT NULL,
    ""SubsidyAmount"" numeric(18,2) NOT NULL,
    ""OrderEscrowAmount"" numeric(18,2) NOT NULL,
    ""DepositEscrowAmount"" numeric(18,2) NOT NULL,
    ""PayloadVersion"" integer NOT NULL,
    ""Status"" integer NOT NULL,
    ""Attempts"" integer NOT NULL,
    ""NextRetryAt"" timestamp with time zone NULL,
    ""LastErrorCode"" character varying(100) NULL,
    ""LastError"" character varying(500) NULL,
    ""CompletedAt"" timestamp with time zone NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    ""IsDeleted"" boolean NOT NULL,
    ""DeletedAt"" timestamp with time zone NULL,
    CONSTRAINT ""PK_SettlementSnapshots"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SettlementSnapshots_BusinessType_SubOrderId"" ON ""SettlementSnapshots"" (""BusinessType"", ""SubOrderId"");
CREATE INDEX IF NOT EXISTS ""IX_SettlementSnapshots_Status_NextRetryAt"" ON ""SettlementSnapshots"" (""Status"", ""NextRetryAt"");
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""SettlementSnapshots"";");
        migrationBuilder.Sql(@"ALTER TABLE ""SubOrders"" DROP COLUMN IF EXISTS ""SubsidyState"";");
    }
}
