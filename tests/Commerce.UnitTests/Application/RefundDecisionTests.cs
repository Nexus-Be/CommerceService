namespace Commerce.UnitTests.Application;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Commerce.Api.Workers;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// G7: <see cref="RefundStatus.Requested"/> only means "waiting for a decision" and the retry worker never touches it; a refund that has
/// been decided waits for the wallet as <see cref="RefundStatus.WalletPending"/> and is retried. G4: the wallet transaction is stored
/// and a refund that did not cause the credit is never marked as refunded.
/// </summary>
public class RefundDecisionTests : IDisposable
{
    private readonly TestCommerceDbContext _db = TestDbContextFactory.CreateInMemoryDbContext();
    private readonly Mock<IWalletClient> _wallet = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();

    public RefundDecisionTests()
    {
        var scoped = new Mock<IServiceProvider>();
        scoped.Setup(s => s.GetService(typeof(ICommerceDbContext))).Returns(_db);
        scoped.Setup(s => s.GetService(typeof(IWalletClient))).Returns(_wallet.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scoped.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(s => s.CreateScope()).Returns(scope.Object);
        _serviceProvider.Setup(s => s.GetService(typeof(IServiceScopeFactory))).Returns(factory.Object);
    }

    public void Dispose() => _db.Dispose();

    private PaymentTimeoutWorker Worker() => new(_serviceProvider.Object, NullLogger<PaymentTimeoutWorker>.Instance);

    private OrderService Service() => new(
        _db, _wallet.Object, new Mock<IFulfillmentClient>().Object, new Mock<ICatalogClient>().Object, new Mock<IUserClient>().Object,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<OrderService>.Instance);

    private async Task<(SubOrder Sub, OrderRefund Refund)> SeedAsync(RefundStatus status, string reason = "BUYER_REQUEST", int updatedMinutesAgo = 10,
        SubOrderStatus subStatus = SubOrderStatus.Delivered)
    {
        var order = new Order { Id = Guid.NewGuid(), BuyerId = Guid.NewGuid(), Status = OrderStatus.Paid, TotalAmount = 230000m, PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(1) };
        var sub = new SubOrder
        {
            Id = Guid.NewGuid(), SubOrderNo = "SUB-G7", OrderId = order.Id, BuyerId = order.BuyerId, SellerId = Guid.NewGuid(),
            Status = subStatus, PaymentMethod = PaymentMethod.Wallet, ContactPhone = "0901234567", TotalAmount = 230000m, PaidAt = DateTimeOffset.UtcNow.AddHours(-2),
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        var refund = new OrderRefund
        {
            Id = Guid.NewGuid(), OrderId = order.Id, SubOrderId = sub.Id, Amount = 230000m, RefundType = RefundType.Full, Status = status,
            ReasonCode = reason, ReasonNote = "note", ActorRole = reason == "BUYER_REQUEST" ? OrderActorRole.Buyer : OrderActorRole.System,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-updatedMinutesAgo), UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-updatedMinutesAgo)
        };
        _db.Orders.Add(order); _db.SubOrders.Add(sub); _db.OrderRefunds.Add(refund);
        await _db.SaveChangesAsync();
        return (sub, refund);
    }

    private void WalletAnswers(WalletRefundResult result) => _wallet
        .Setup(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(result);

    // ---------------------------------------------------------------- the retry worker

    [Fact]
    public async Task The_worker_never_touches_a_request_that_is_still_waiting_for_a_decision()
    {
        var (_, refund) = await SeedAsync(RefundStatus.Requested);
        WalletAnswers(WalletRefundResult.Success(Guid.NewGuid(), false));

        await Worker().ReconcilePendingRefundsAsync(_db, _wallet.Object, CancellationToken.None);

        (await _db.OrderRefunds.FindAsync(refund.Id))!.Status.Should().Be(RefundStatus.Requested);
        _wallet.Verify(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _wallet.Verify(w => w.RefundOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_worker_waits_for_the_lease_before_retrying_a_decided_refund()
    {
        await SeedAsync(RefundStatus.WalletPending, "BUYER_CANCELLED", updatedMinutesAgo: 0);
        WalletAnswers(WalletRefundResult.Success());

        await Worker().ReconcilePendingRefundsAsync(_db, _wallet.Object, CancellationToken.None);

        _wallet.Verify(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_worker_sends_the_parent_order_id_and_stores_the_wallet_transaction()
    {
        var (sub, refund) = await SeedAsync(RefundStatus.WalletPending, "SELLER_REJECTED", subStatus: SubOrderStatus.Cancelled);
        var transaction = Guid.NewGuid();
        WalletAnswers(WalletRefundResult.Success(transaction, false));

        await Worker().ReconcilePendingRefundsAsync(_db, _wallet.Object, CancellationToken.None);

        var done = (await _db.OrderRefunds.FindAsync(refund.Id))!;
        done.Status.Should().Be(RefundStatus.Completed);
        done.RefundTransactionId.Should().Be(transaction);
        _wallet.Verify(w => w.RefundSubOrderPaymentAsync(sub.Id, sub.OrderId, sub.BuyerId.ToString(), 230000m, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        (await _db.SubOrders.FindAsync(sub.Id))!.Status.Should().Be(SubOrderStatus.Cancelled);   // only an approved buyer request changes the sub-order
    }

    [Fact]
    public async Task An_approved_buyer_request_is_finished_by_the_worker_like_the_approval_finishes_it()
    {
        var (sub, refund) = await SeedAsync(RefundStatus.WalletPending, "BUYER_REQUEST");
        WalletAnswers(WalletRefundResult.Success(Guid.NewGuid(), false));

        await Worker().ReconcilePendingRefundsAsync(_db, _wallet.Object, CancellationToken.None);

        (await _db.OrderRefunds.FindAsync(refund.Id))!.Status.Should().Be(RefundStatus.Completed);
        (await _db.SubOrders.FindAsync(sub.Id))!.Status.Should().Be(SubOrderStatus.Refunded);
        _db.OrderStatusHistories.Any(h => h.SubOrderId == sub.Id && h.ToStatus == SubOrderStatus.Refunded).Should().BeTrue();
    }

    [Fact]
    public async Task A_refusal_keeps_the_refund_pending_and_records_the_wallet_code()
    {
        var (_, refund) = await SeedAsync(RefundStatus.WalletPending, "BUYER_CANCELLED");
        WalletAnswers(WalletRefundResult.Failure("FEATURE_DISABLED"));

        await Worker().ReconcilePendingRefundsAsync(_db, _wallet.Object, CancellationToken.None);

        var kept = (await _db.OrderRefunds.FindAsync(refund.Id))!;
        kept.Status.Should().Be(RefundStatus.WalletPending);
        kept.FailureCode.Should().Be("FEATURE_DISABLED");
        kept.RefundTransactionId.Should().BeNull();
    }

    [Fact]
    public async Task A_credit_that_already_belongs_to_another_refund_is_not_counted_as_this_refund()
    {
        var (sub, refund) = await SeedAsync(RefundStatus.WalletPending, "BUYER_CANCELLED");
        var transaction = Guid.NewGuid();
        _db.OrderRefunds.Add(new OrderRefund
        {
            Id = Guid.NewGuid(), OrderId = sub.OrderId, SubOrderId = sub.Id, Amount = 230000m, RefundType = RefundType.Full,
            Status = RefundStatus.Completed, ReasonCode = "DISPUTE_RESOLVED", ActorRole = OrderActorRole.System, RefundTransactionId = transaction
        });
        await _db.SaveChangesAsync();
        WalletAnswers(WalletRefundResult.Success(transaction, true));

        await Worker().ReconcilePendingRefundsAsync(_db, _wallet.Object, CancellationToken.None);

        var second = (await _db.OrderRefunds.FindAsync(refund.Id))!;
        second.Status.Should().Be(RefundStatus.Failed);
        second.FailureCode.Should().Be(RefundOutcome.AlreadyHeldByAnotherRefund);
        second.RefundTransactionId.Should().BeNull();
    }

    // ---------------------------------------------------------------- approving a buyer's request

    [Fact]
    public async Task Approving_records_the_decision_before_the_wallet_is_asked_and_completes_it()
    {
        var (sub, refund) = await SeedAsync(RefundStatus.Requested);
        var transaction = Guid.NewGuid();
        RefundStatus? statusWhenTheWalletWasAsked = null;
        _wallet
            .Setup(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, Guid, string, decimal, string, CancellationToken>((_, _, _, _, _, _) =>
            {
                statusWhenTheWalletWasAsked = _db.OrderRefunds.AsNoTracking().Single(r => r.Id == refund.Id).Status;
                return Task.FromResult(WalletRefundResult.Success(transaction, false));
            });

        var response = await Service().ApproveRefundAsync(sub.SellerId, refund.Id, new Commerce.Application.Services.Order.ApproveRefundRequest("ok"), false, CancellationToken.None);

        statusWhenTheWalletWasAsked.Should().Be(RefundStatus.WalletPending);
        response.Status.Should().Be("Completed");
        (await _db.OrderRefunds.FindAsync(refund.Id))!.RefundTransactionId.Should().Be(transaction);
        (await _db.SubOrders.FindAsync(sub.Id))!.Status.Should().Be(SubOrderStatus.Refunded);
        _wallet.Verify(w => w.RefundSubOrderPaymentAsync(sub.Id, sub.OrderId, sub.BuyerId.ToString(), 230000m, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task When_the_wallet_refuses_an_approval_the_refund_stays_pending_and_the_sub_order_is_not_marked_refunded()
    {
        var (sub, refund) = await SeedAsync(RefundStatus.Requested);
        WalletAnswers(WalletRefundResult.Failure("FEATURE_DISABLED"));

        var response = await Service().ApproveRefundAsync(sub.SellerId, refund.Id, new Commerce.Application.Services.Order.ApproveRefundRequest("ok"), false, CancellationToken.None);

        response.Status.Should().Be("WalletPending");
        response.CompletedAt.Should().BeNull();
        (await _db.OrderRefunds.FindAsync(refund.Id))!.Status.Should().Be(RefundStatus.WalletPending);
        (await _db.SubOrders.FindAsync(sub.Id))!.Status.Should().Be(SubOrderStatus.Delivered);
    }

    // ---------------------------------------------------------------- RefundOutcome

    [Fact]
    public async Task A_replay_that_belongs_to_nobody_else_completes_the_refund_that_asked_for_it()
    {
        // The wallet's first answer was lost (a timeout): the retry gets the credit it made back, and the refund is completed once.
        var (_, refund) = await SeedAsync(RefundStatus.WalletPending, "BUYER_CANCELLED");
        var transaction = Guid.NewGuid();

        var done = await RefundOutcome.ApplyAsync(_db, refund, WalletRefundResult.Success(transaction, true), CancellationToken.None);

        done.Should().BeTrue();
        refund.Status.Should().Be(RefundStatus.Completed);
        refund.RefundTransactionId.Should().Be(transaction);
    }

    [Fact]
    public async Task A_failure_changes_nothing()
    {
        var (_, refund) = await SeedAsync(RefundStatus.WalletPending, "BUYER_CANCELLED");

        var done = await RefundOutcome.ApplyAsync(_db, refund, WalletRefundResult.Failure("X"), CancellationToken.None);

        done.Should().BeFalse();
        refund.Status.Should().Be(RefundStatus.WalletPending);
        refund.RefundTransactionId.Should().BeNull();
    }
}
