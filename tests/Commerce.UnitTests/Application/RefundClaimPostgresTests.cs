namespace Commerce.UnitTests.Application;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Commerce.Api.Workers;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Infrastructure.Persistence;
using Commerce.Infrastructure.Persistence.Migrations;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// G7 against a real PostgreSQL (set COMMERCE_TEST_DB, see NotificationOutboxWorkerTests): the conditional claim is atomic, so two workers
/// never refund the same decided refund, and the data migration sorts the existing "Requested" rows correctly.
/// </summary>
public sealed class RefundClaimPostgresTests : IAsyncLifetime
{
    private string? _admin;
    private string? _database;
    private string _connection = string.Empty;

    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("COMMERCE_TEST_DB");
        if (string.IsNullOrWhiteSpace(_admin)) return;

        _database = "commerce_refund_claim_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(_admin))
        {
            await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin).ExecuteNonQueryAsync();
        }

        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using var db = NewDb();
        // Same approach as the outbox test: the tables come from the model without the seed rows.
        var script = db.Database.GenerateCreateScript();
        foreach (var statement in System.Text.RegularExpressions.Regex.Split(script, @";\s*?
"))
        {
            var sql = statement.Trim();
            if (sql.Length == 0 || sql.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)) continue;
            await db.Database.ExecuteSqlRawAsync(sql);
        }
    }

    public async Task DisposeAsync()
    {
        if (_database is null || _admin is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE \"{_database}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection).Options);

    private static OrderRefund Refund(Order order, SubOrder sub, RefundStatus status, string reason, int minutesAgo = 10, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(), OrderId = order.Id, SubOrderId = sub.Id, Amount = 230000m, RefundType = RefundType.Full, Status = status,
        ReasonCode = reason, ReasonNote = "n", ActorRole = reason == "BUYER_REQUEST" ? OrderActorRole.Buyer : OrderActorRole.System,
        RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), IsDeleted = deleted
    };

    private async Task<(Order Order, SubOrder Sub)> SeedOrderAsync(AppDbContext db)
    {
        var order = new Order { Id = Guid.NewGuid(), BuyerId = Guid.NewGuid(), Status = OrderStatus.Paid, TotalAmount = 230000m, PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(1) };
        var sub = new SubOrder
        {
            Id = Guid.NewGuid(), SubOrderNo = "SUB-" + Guid.NewGuid().ToString("N")[..8], OrderId = order.Id, BuyerId = order.BuyerId, SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Delivered, PaymentMethod = PaymentMethod.Wallet, ContactPhone = "0901234567", TotalAmount = 230000m,
            PaidAt = DateTimeOffset.UtcNow.AddHours(-2), PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        db.Orders.Add(order); db.SubOrders.Add(sub);
        await db.SaveChangesAsync();
        return (order, sub);
    }

    [PostgresFact]
    public async Task Two_workers_claiming_the_same_refund_at_once_get_exactly_one_winner()
    {
        Guid refundId;
        await using (var seed = NewDb())
        {
            var (order, sub) = await SeedOrderAsync(seed);
            var refund = Refund(order, sub, RefundStatus.WalletPending, "BUYER_CANCELLED");
            seed.OrderRefunds.Add(refund);
            await seed.SaveChangesAsync();
            refundId = refund.Id;
        }

        await using var first = NewDb();
        await using var second = NewDb();
        var a = await first.OrderRefunds.SingleAsync(r => r.Id == refundId);
        var b = await second.OrderRefunds.SingleAsync(r => r.Id == refundId);

        var results = await Task.WhenAll(
            PaymentTimeoutWorker.TryClaimRefundAsync(first, a, CancellationToken.None),
            PaymentTimeoutWorker.TryClaimRefundAsync(second, b, CancellationToken.None));

        results.Count(claimed => claimed).Should().Be(1);
    }

    [PostgresFact]
    public async Task Two_workers_sweeping_together_ask_the_wallet_once_per_refund()
    {
        await using (var seed = NewDb())
        {
            var (order, sub) = await SeedOrderAsync(seed);
            seed.OrderRefunds.Add(Refund(order, sub, RefundStatus.WalletPending, "BUYER_CANCELLED"));
            await seed.SaveChangesAsync();
        }

        var calls = 0;
        var wallet = new Mock<IWalletClient>();
        wallet
            .Setup(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, Guid, string, decimal, string, CancellationToken>(async (_, _, _, _, _, _) =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(150);
                return WalletRefundResult.Success(Guid.NewGuid(), false);
            });
        var worker = new PaymentTimeoutWorker(new ServiceCollection().BuildServiceProvider(), NullLogger<PaymentTimeoutWorker>.Instance);

        await using var one = NewDb();
        await using var two = NewDb();
        await Task.WhenAll(
            worker.ReconcilePendingRefundsAsync(one, wallet.Object, CancellationToken.None),
            worker.ReconcilePendingRefundsAsync(two, wallet.Object, CancellationToken.None));

        calls.Should().Be(1);
        await using var check = NewDb();
        (await check.OrderRefunds.SingleAsync()).Status.Should().Be(RefundStatus.Completed);
    }

    [PostgresFact]
    public async Task The_data_migration_moves_only_decided_refunds_to_wallet_pending_and_is_idempotent()
    {
        await using var db = NewDb();
        var (order, sub) = await SeedOrderAsync(db);
        var buyerRequest = Refund(order, sub, RefundStatus.Requested, "BUYER_REQUEST");
        var cancelled = Refund(order, sub, RefundStatus.Requested, "BUYER_CANCELLED");
        var dispute = Refund(order, sub, RefundStatus.Requested, "DISPUTE_RESOLVED");
        var completed = Refund(order, sub, RefundStatus.Completed, "SELLER_REJECTED");
        var deleted = Refund(order, sub, RefundStatus.Requested, "BUYER_CANCELLED", deleted: true);
        db.OrderRefunds.AddRange(buyerRequest, cancelled, dispute, completed, deleted);
        await db.SaveChangesAsync();

        var sql = new ClassifyPendingRefunds().UpOperations.OfType<SqlOperation>().Single().Sql;
        await db.Database.ExecuteSqlRawAsync(sql);
        await db.Database.ExecuteSqlRawAsync(sql);   // a second run changes nothing

        await using var read = NewDb();
        var status = read.OrderRefunds.IgnoreQueryFilters().ToDictionary(r => r.Id, r => r.Status);
        status[buyerRequest.Id].Should().Be(RefundStatus.Requested);
        status[cancelled.Id].Should().Be(RefundStatus.WalletPending);
        status[dispute.Id].Should().Be(RefundStatus.WalletPending);
        status[completed.Id].Should().Be(RefundStatus.Completed);
        status[deleted.Id].Should().Be(RefundStatus.Requested);
    }
}
