using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the decision to refund a buyer from the refund fund after the sub-order was paid out (one row per sub-order, with the approvals and the
/// NeedsFinance state). Only an addition. Idempotent.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261125140000_AddRefundFundCases")]
public sealed class AddRefundFundCases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""RefundFundCases"" (
    ""Id"" uuid NOT NULL,
    ""SubOrderId"" uuid NOT NULL,
    ""ParentOrderId"" uuid NOT NULL,
    ""BuyerId"" uuid NOT NULL,
    ""SellerId"" uuid NOT NULL,
    ""Amount"" numeric(18,2) NOT NULL,
    ""ReportId"" uuid NULL,
    ""RequestReason"" character varying(500) NOT NULL,
    ""Status"" integer NOT NULL,
    ""FirstApproverId"" uuid NOT NULL,
    ""SecondApproverId"" uuid NULL,
    ""NeedsFinanceReason"" character varying(100) NULL,
    ""SellerRecovered"" numeric(18,2) NULL,
    ""SellerShortfall"" numeric(18,2) NULL,
    ""BuyerTransactionId"" uuid NULL,
    ""Attempts"" integer NOT NULL,
    ""RefundedAt"" timestamp with time zone NULL,
    ""ResolvedBy"" uuid NULL,
    ""ResolutionNote"" character varying(500) NULL,
    ""ResolvedAt"" timestamp with time zone NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    ""IsDeleted"" boolean NOT NULL,
    ""DeletedAt"" timestamp with time zone NULL,
    CONSTRAINT ""PK_RefundFundCases"" PRIMARY KEY (""Id"")
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RefundFundCases_SubOrderId"" ON ""RefundFundCases"" (""SubOrderId"");
CREATE INDEX IF NOT EXISTS ""IX_RefundFundCases_Status"" ON ""RefundFundCases"" (""Status"");");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""RefundFundCases"";");
    }
}
