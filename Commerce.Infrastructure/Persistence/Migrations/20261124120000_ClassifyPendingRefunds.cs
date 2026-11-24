using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commerce.Infrastructure.Persistence.Migrations;

/// <summary>
/// Before this change a refund in status "Requested" (0) meant two different things: waiting for a decision (a buyer's request) and already
/// decided but not yet confirmed by the wallet (cancellation, rejection, return, dispute). The retry workers took every "Requested" refund,
/// so they could refund a buyer's request before anybody approved it. From now on "Requested" only means "waiting for a decision" and the
/// decided ones wait as "WalletPending" (4), which is the only status the workers retry.
///
/// This data-only migration sorts the existing rows once, before any worker runs: every "Requested" refund that is not a buyer's request
/// had already been decided, so it becomes "WalletPending"; buyer requests stay "Requested". No schema change (the status is stored as an
/// integer). Idempotent: a second run finds nothing left to move.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261124120000_ClassifyPendingRefunds")]
public sealed class ClassifyPendingRefunds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
UPDATE ""OrderRefunds""
SET ""Status"" = 4, ""UpdatedAt"" = now()
WHERE ""Status"" = 0
  AND ""IsDeleted"" = false
  AND ""ReasonCode"" <> 'BUYER_REQUEST';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The old meaning of "Requested" covered both kinds, so going back means folding the decided ones into it again.
        migrationBuilder.Sql(@"UPDATE ""OrderRefunds"" SET ""Status"" = 0 WHERE ""Status"" = 4;");
    }
}
