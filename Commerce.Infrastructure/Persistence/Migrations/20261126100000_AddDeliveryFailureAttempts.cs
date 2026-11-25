using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations;

/// <summary>
/// Keeps each failed delivery attempt of a parcel with the carrier's own time, so a return is judged only against the attempts of the same
/// parcel that came before it. Only an addition. Idempotent.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261126100000_AddDeliveryFailureAttempts")]
public sealed class AddDeliveryFailureAttempts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""DeliveryFailureAttempts"" (
    ""Id"" uuid NOT NULL,
    ""SubOrderId"" uuid NOT NULL,
    ""TrackingCode"" character varying(100) NULL,
    ""ReasonCode"" character varying(100) NULL,
    ""Reason"" character varying(500) NULL,
    ""EventType"" character varying(50) NULL,
    ""EventTime"" timestamp with time zone NULL,
    ""ReceivedAt"" timestamp with time zone NOT NULL,
    ""DedupeKey"" character varying(250) NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    ""IsDeleted"" boolean NOT NULL,
    ""DeletedAt"" timestamp with time zone NULL,
    CONSTRAINT ""PK_DeliveryFailureAttempts"" PRIMARY KEY (""Id"")
);
CREATE INDEX IF NOT EXISTS ""IX_DeliveryFailureAttempts_SubOrderId"" ON ""DeliveryFailureAttempts"" (""SubOrderId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_DeliveryFailureAttempts_SubOrderId_DedupeKey"" ON ""DeliveryFailureAttempts"" (""SubOrderId"", ""DedupeKey"") WHERE ""DedupeKey"" IS NOT NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""DeliveryFailureAttempts"";");
    }
}
