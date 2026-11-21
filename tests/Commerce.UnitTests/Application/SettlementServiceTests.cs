namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Settlement;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class SettlementServiceTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly TestCommerceDbContext _db;
    private readonly Mock<IWalletClient> _wallet = new();

    public SettlementServiceTests()
    {
        _db = TestDbContextFactory.CreateInMemoryDbContext(_dbName);
    }

    public void Dispose() => _db.Dispose();

    private SettlementService Create(bool legacyEnabled = true, int protocol = 1) => new(
        _db,
        _wallet.Object,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Settlement:LegacyV1Enabled"] = legacyEnabled.ToString(),
            ["Settlement:ProtocolVersion"] = protocol.ToString()
        }).Build(),
        NullLogger<SettlementService>.Instance);

    /// <summary>Items 200,000, fee 10% (20,000), shipping 30,000, no discount: total 230,000, seller 180,000.</summary>
    private SubOrder NewSubOrder(int protocol, decimal systemDiscount = 0m)
    {
        var sub = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SUB-" + Guid.NewGuid().ToString("N")[..8],
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Completed,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new Commerce.Domain.ValueObjects.ShippingAddressSnapshot { ReceiverName = "A", ContactPhone = "0900000000", Street = "x", Ward = "w", District = "d", Province = "p" },
            ContactPhone = "0900000000",
            PaymentDeadlineAt = DateTimeOffset.UtcNow,
            SettlementProtocolVersion = protocol
        };
        sub.Items.Add(new SubOrderItem
        {
            Id = Guid.NewGuid(), SubOrderId = sub.Id, ProductId = Guid.NewGuid(), SkuId = Guid.NewGuid(), SellerId = sub.SellerId,
            ProductNameSnapshot = "p", Quantity = 2, UnitPrice = 100_000m, UnitPriceAfterDiscount = 100_000m, LineTotal = 200_000m
        });
        sub.CalculateFinancials(shippingFee: 30_000m, shopDiscount: 0m, systemDiscount: systemDiscount, platformMarkupPercent: 10m, productDiscount: 0m);
        _db.SubOrders.Add(sub);
        _db.SaveChanges();
        return sub;
    }

    private static WalletCallOutcome Permanent(string code) => new(false, 409, code, code);

    [Fact]
    public async Task A_v1_sub_order_still_uses_the_legacy_route_while_it_is_open()
    {
        var sub = NewSubOrder(protocol: 1);
        _wallet.Setup(w => w.SettleSellerSubOrderAsync(sub.Id, sub.SellerId, 180_000m, It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var paid = await Create().SettleSubOrderAsync(sub, default);

        paid.Should().BeTrue();
        _wallet.Verify(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        (await _db.SettlementSnapshots.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_v2_payout_commits_its_snapshot_before_calling_wallet_and_sends_the_full_allocation()
    {
        var sub = NewSubOrder(protocol: 2);
        WalletSettlementRequest? sent = null;
        var snapshotWasCommitted = false;
        _wallet.Setup(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<WalletSettlementRequest, CancellationToken>((request, _) =>
            {
                sent = request;
                // At the moment Wallet is called, the payload must already be stored (committed): a crash now still resends the same payload.
                snapshotWasCommitted = TestDbContextFactory.CreateInMemoryDbContext(_dbName).SettlementSnapshots.Any(s => s.SubOrderId == request.SubOrderId);
            })
            .ReturnsAsync(WalletCallOutcome.Ok());

        var paid = await Create().SettleSubOrderAsync(sub, default);

        paid.Should().BeTrue();
        snapshotWasCommitted.Should().BeTrue();
        sent.Should().NotBeNull();
        sent!.BusinessType.Should().Be("OrderPayout");
        sent.ParentOrderId.Should().Be(sub.OrderId);
        sent.SubOrderId.Should().Be(sub.Id);
        sent.SellerReceiveAmount.Should().Be(180_000m);
        sent.PlatformFeeAmount.Should().Be(20_000m);
        sent.ShippingAmount.Should().Be(30_000m);
        sent.SubsidyAmount.Should().Be(0m);
        sent.OrderEscrowAmount.Should().Be(230_000m);
        sent.DepositEscrowAmount.Should().Be(0m);
        (await _db.SettlementSnapshots.SingleAsync()).Status.Should().Be(SettlementSnapshotStatus.Completed);
    }

    [Fact]
    public async Task A_completed_payout_is_not_sent_again()
    {
        var sub = NewSubOrder(protocol: 2);
        _wallet.Setup(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(WalletCallOutcome.Ok());
        var service = Create();

        (await service.SettleSubOrderAsync(sub, default)).Should().BeTrue();
        (await service.SettleSubOrderAsync(sub, default)).Should().BeTrue();

        _wallet.Verify(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_transient_failure_keeps_the_payout_pending_with_a_backoff_and_resends_the_same_payload_even_if_the_order_changed()
    {
        var sub = NewSubOrder(protocol: 2);
        var sent = new List<WalletSettlementRequest>();
        var outcomes = new Queue<WalletCallOutcome>(new[] { WalletCallOutcome.Transient("WALLET_UNAVAILABLE", "timeout"), WalletCallOutcome.Ok() });
        _wallet.Setup(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<WalletSettlementRequest, CancellationToken>((request, _) => sent.Add(request))
            .Returns(() => Task.FromResult(outcomes.Dequeue()));
        var service = Create();

        (await service.SettleSubOrderAsync(sub, default)).Should().BeFalse();
        var snapshot = await _db.SettlementSnapshots.SingleAsync();
        snapshot.Status.Should().Be(SettlementSnapshotStatus.Pending);
        snapshot.Attempts.Should().Be(1);
        snapshot.NextRetryAt.Should().NotBeNull().And.BeAfter(DateTimeOffset.UtcNow);

        // A worker pass before the next attempt is due does not call Wallet.
        (await service.SettleSubOrderAsync(sub, default, respectBackoff: true)).Should().BeFalse();
        _wallet.Verify(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        // The order figures change afterwards (a re-priced fee, say): the stored payload is what is sent.
        sub.PlatformFeeAmount = 99m;
        sub.SellerReceiveAmount = 1m;
        snapshot.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await _db.SaveChangesAsync();
        (await service.SettleSubOrderAsync(sub, default, respectBackoff: true)).Should().BeTrue();

        sent.Should().HaveCount(2);
        sent[1].Should().Be(sent[0]);
        sent[1].SellerReceiveAmount.Should().Be(180_000m);
    }

    [Fact]
    public async Task A_permanent_refusal_needs_finance_and_is_never_retried()
    {
        var sub = NewSubOrder(protocol: 2);
        _wallet.Setup(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Permanent("SETTLEMENT_CONFLICT"));
        var service = Create();

        (await service.SettleSubOrderAsync(sub, default)).Should().BeFalse();
        (await service.SettleSubOrderAsync(sub, default)).Should().BeFalse();

        var snapshot = await _db.SettlementSnapshots.SingleAsync();
        snapshot.Status.Should().Be(SettlementSnapshotStatus.NeedsFinance);
        snapshot.LastErrorCode.Should().Be("SETTLEMENT_CONFLICT");
        _wallet.Verify(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("DEPOSIT_ESCROW_INSUFFICIENT")]
    [InlineData("DEPOSIT_ESCROW_NOT_FOUND")]
    public async Task A_deposit_escrow_that_is_not_filled_yet_is_retried_not_sent_to_finance(string code)
    {
        var order = NewAuctionOrder(protocol: 2);
        var outcomes = new Queue<WalletCallOutcome>(new[] { new WalletCallOutcome(false, 409, code, "not yet"), WalletCallOutcome.Ok() });
        _wallet.Setup(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(outcomes.Dequeue()));
        var service = Create();

        (await service.SettleAuctionOrderAsync(order, default)).Should().BeFalse();
        var snapshot = await _db.SettlementSnapshots.SingleAsync();
        snapshot.Status.Should().Be(SettlementSnapshotStatus.Pending);
        snapshot.NextRetryAt.Should().NotBeNull();

        snapshot.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await _db.SaveChangesAsync();
        (await service.SettleAuctionOrderAsync(order, default, respectBackoff: true)).Should().BeTrue();
    }

    [Fact]
    public async Task Too_many_transient_failures_move_the_payout_to_finance()
    {
        var sub = NewSubOrder(protocol: 2);
        _wallet.Setup(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(WalletCallOutcome.Transient(null, "down"));
        var service = Create();

        for (var i = 0; i < SettlementService.MaxAttempts; i++)
        {
            await service.SettleSubOrderAsync(sub, default);
        }

        (await _db.SettlementSnapshots.SingleAsync()).Status.Should().Be(SettlementSnapshotStatus.NeedsFinance);
    }

    [Fact]
    public async Task A_payload_that_does_not_balance_goes_to_finance_without_calling_wallet()
    {
        var sub = NewSubOrder(protocol: 2);
        sub.SellerReceiveAmount = 170_000m; // does not add up to what the buyer paid
        await _db.SaveChangesAsync();

        (await Create().SettleSubOrderAsync(sub, default)).Should().BeFalse();

        var snapshot = await _db.SettlementSnapshots.SingleAsync();
        snapshot.Status.Should().Be(SettlementSnapshotStatus.NeedsFinance);
        snapshot.LastErrorCode.Should().Be("SETTLEMENT_IDENTITY_INVALID");
        _wallet.Verify(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_platform_discount_is_sent_as_a_subsidy_and_marked_used_after_the_payout()
    {
        var sub = NewSubOrder(protocol: 2, systemDiscount: 10_000m);
        sub.SubsidyState = SubsidyState.Held;
        WalletSettlementRequest? sent = null;
        _wallet.Setup(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<WalletSettlementRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(WalletCallOutcome.Ok());

        (await Create().SettleSubOrderAsync(sub, default)).Should().BeTrue();

        sent!.SubsidyAmount.Should().Be(10_000m);
        sent.OrderEscrowAmount.Should().Be(220_000m);
        sub.SubsidyState.Should().Be(SubsidyState.Used);
    }

    [Fact]
    public async Task When_the_legacy_route_is_closed_an_unpaid_v1_order_continues_through_v2_and_never_through_v1()
    {
        var sub = NewSubOrder(protocol: 1);
        _wallet.Setup(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(WalletCallOutcome.Ok());

        (await Create(legacyEnabled: false).SettleSubOrderAsync(sub, default)).Should().BeTrue();

        sub.SettlementProtocolVersion.Should().Be(2);
        _wallet.Verify(w => w.SettleSellerSubOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_cod_sub_order_never_touches_the_wallet()
    {
        var sub = NewSubOrder(protocol: 2);
        sub.PaymentMethod = PaymentMethod.Cod;

        (await Create().SettleSubOrderAsync(sub, default)).Should().BeTrue();

        _wallet.VerifyNoOtherCalls();
    }

    // ------------------------------------------------------------------ auction

    private AuctionOrder NewAuctionOrder(int protocol, decimal? total = 935_000m)
    {
        var order = new AuctionOrder
        {
            Id = Guid.NewGuid(), OrderNo = "AO-1", AuctionId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductNameSnapshot = "p",
            WinningAmount = 1_000_000m, DepositAmount = 100_000m, RemainingAmount = 900_000m,
            ShippingFeeAmount = 35_000m, TotalAmount = total,
            SellerReceiveAmount = 950_000m, PlatformFeeAmount = 50_000m,
            Status = SubOrderStatus.Completed, PaymentMethod = PaymentMethod.Wallet, PaymentDeadlineAt = DateTimeOffset.UtcNow,
            SettlementProtocolVersion = protocol
        };
        _db.AuctionOrders.Add(order);
        _db.SaveChanges();
        return order;
    }

    [Fact]
    public async Task An_auction_payout_uses_both_escrows_and_pays_the_seller_the_winning_amount_minus_the_fee()
    {
        var order = NewAuctionOrder(protocol: 2);
        WalletSettlementRequest? sent = null;
        _wallet.Setup(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<WalletSettlementRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(WalletCallOutcome.Ok());

        (await Create().SettleAuctionOrderAsync(order, default)).Should().BeTrue();

        sent!.BusinessType.Should().Be("AuctionPayout");
        sent.ParentOrderId.Should().Be(order.Id);
        sent.SubOrderId.Should().Be(order.Id);
        sent.SellerReceiveAmount.Should().Be(950_000m);
        sent.PlatformFeeAmount.Should().Be(50_000m);
        sent.ShippingAmount.Should().Be(35_000m);
        sent.OrderEscrowAmount.Should().Be(935_000m);
        sent.DepositEscrowAmount.Should().Be(100_000m);
    }

    [Theory]
    [InlineData("Winner", 1_000_000, 100_000, 35_000)]        // deposit is part of the price, buyer pays the rest plus shipping
    [InlineData("SecondChance", 800_000, 80_000, 30_000)]     // second-chance bidder: own deposit, own price
    [InlineData("BuyNow", 3_000_000, 3_000_000, 40_000)]      // Buy Now: the whole price is already held, the buyer only pays shipping
    public async Task Every_auction_source_balances_and_uses_the_deposit_exactly_once(string source, decimal win, decimal deposit, decimal shipping)
    {
        var fee = Math.Round(win * 0.05m, 0, MidpointRounding.AwayFromZero);
        var order = new AuctionOrder
        {
            Id = Guid.NewGuid(), OrderNo = "AO-S", AuctionId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductNameSnapshot = "p",
            WinningAmount = win, DepositAmount = deposit, RemainingAmount = win - deposit, ShippingFeeAmount = shipping,
            TotalAmount = win - deposit + shipping,
            SellerReceiveAmount = win - fee, PlatformFeeAmount = fee,
            OrderSource = Enum.Parse<AuctionOrderSource>(source),
            Status = SubOrderStatus.Completed, PaymentMethod = PaymentMethod.Wallet, PaymentDeadlineAt = DateTimeOffset.UtcNow,
            SettlementProtocolVersion = 2
        };
        _db.AuctionOrders.Add(order);
        _db.SaveChanges();
        WalletSettlementRequest? sent = null;
        _wallet.Setup(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>()))
            .Callback<WalletSettlementRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(WalletCallOutcome.Ok());

        (await Create().SettleAuctionOrderAsync(order, default)).Should().BeTrue();

        sent.Should().NotBeNull();
        // What leaves both escrows equals what is paid to the seller plus the fee and shipping allocation: nothing is created or lost.
        (sent!.OrderEscrowAmount + sent.DepositEscrowAmount + sent.SubsidyAmount).Should().Be(sent.SellerReceiveAmount + sent.PlatformFeeAmount + sent.ShippingAmount);
        sent.SellerReceiveAmount.Should().Be(win - fee);
        sent.DepositEscrowAmount.Should().Be(deposit); // the deposit is used once, as one of the two sources
        sent.OrderEscrowAmount.Should().Be(win - deposit + shipping);
    }

    [Fact]
    public async Task An_unpaid_auction_order_has_nothing_to_pay_out_and_goes_to_finance()
    {
        var order = NewAuctionOrder(protocol: 2, total: null);

        (await Create().SettleAuctionOrderAsync(order, default)).Should().BeFalse();

        (await _db.SettlementSnapshots.SingleAsync()).LastErrorCode.Should().Be("ORDER_NOT_PAID");
        _wallet.Verify(w => w.SettleOrderPayoutAsync(It.IsAny<WalletSettlementRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------ worker pass

    [Fact]
    public async Task The_worker_pass_finishes_due_payouts_sets_the_order_flags_and_skips_the_rest()
    {
        var due = NewSubOrder(protocol: 2);
        var waiting = NewSubOrder(protocol: 2);
        var stuck = NewSubOrder(protocol: 2);
        var auction = NewAuctionOrder(protocol: 2);
        _wallet.Setup(w => w.SettleOrderPayoutAsync(It.Is<WalletSettlementRequest>(r => r.SubOrderId == due.Id || r.SubOrderId == auction.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletCallOutcome.Ok());
        var now = DateTimeOffset.UtcNow;
        foreach (var (sub, status, next) in new[]
                 {
                     (due.Id, SettlementSnapshotStatus.Pending, (DateTimeOffset?)null),
                     (waiting.Id, SettlementSnapshotStatus.Pending, (DateTimeOffset?)now.AddMinutes(30)),
                     (stuck.Id, SettlementSnapshotStatus.NeedsFinance, null)
                 })
        {
            _db.SettlementSnapshots.Add(new SettlementSnapshot
            {
                Id = Guid.NewGuid(), BusinessType = "OrderPayout", OrderId = Guid.NewGuid(), SubOrderId = sub, SellerId = Guid.NewGuid(),
                SellerReceiveAmount = 180_000m, PlatformFeeAmount = 20_000m, ShippingAmount = 30_000m, SubsidyAmount = 0m,
                OrderEscrowAmount = 230_000m, DepositEscrowAmount = 0m, Status = status, NextRetryAt = next
            });
        }

        _db.SettlementSnapshots.Add(new SettlementSnapshot
        {
            Id = Guid.NewGuid(), BusinessType = "AuctionPayout", OrderId = auction.Id, SubOrderId = auction.Id, SellerId = auction.SellerId,
            SellerReceiveAmount = 950_000m, PlatformFeeAmount = 50_000m, ShippingAmount = 35_000m, SubsidyAmount = 0m,
            OrderEscrowAmount = 935_000m, DepositEscrowAmount = 100_000m, Status = SettlementSnapshotStatus.Pending
        });
        await _db.SaveChangesAsync();

        var completed = await Create().RunPendingWorkAsync(50, default);

        completed.Should().Be(2);
        (await _db.SubOrders.SingleAsync(s => s.Id == due.Id)).IsEscrowSettled.Should().BeTrue();
        (await _db.SubOrders.SingleAsync(s => s.Id == waiting.Id)).IsEscrowSettled.Should().BeFalse();
        (await _db.SubOrders.SingleAsync(s => s.Id == stuck.Id)).IsEscrowSettled.Should().BeFalse();
        (await _db.AuctionOrders.SingleAsync(a => a.Id == auction.Id)).PayoutCompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task The_worker_pass_releases_the_subsidy_of_cancelled_sub_orders_and_keeps_it_when_wallet_is_down()
    {
        var cancelled = NewSubOrder(protocol: 2, systemDiscount: 10_000m);
        cancelled.Status = SubOrderStatus.Cancelled;
        cancelled.SubsidyState = SubsidyState.Held;
        var stillOpen = NewSubOrder(protocol: 2, systemDiscount: 10_000m);
        stillOpen.Status = SubOrderStatus.Paid;
        stillOpen.SubsidyState = SubsidyState.Held;
        await _db.SaveChangesAsync();
        _wallet.Setup(w => w.ReleasePlatformSubsidyAsync(cancelled.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletCallOutcome.Transient(null, "down"));
        var service = Create();

        await service.RunPendingWorkAsync(50, default);
        (await _db.SubOrders.SingleAsync(s => s.Id == cancelled.Id)).SubsidyState.Should().Be(SubsidyState.Held);

        _wallet.Setup(w => w.ReleasePlatformSubsidyAsync(cancelled.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletCallOutcome.Ok());
        await service.RunPendingWorkAsync(50, default);

        (await _db.SubOrders.SingleAsync(s => s.Id == cancelled.Id)).SubsidyState.Should().Be(SubsidyState.Released);
        (await _db.SubOrders.SingleAsync(s => s.Id == stillOpen.Id)).SubsidyState.Should().Be(SubsidyState.Held);
        _wallet.Verify(w => w.ReleasePlatformSubsidyAsync(stillOpen.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------ subsidy hold

    [Fact]
    public async Task A_budget_refusal_becomes_a_409_asking_for_a_new_quote()
    {
        _wallet.Setup(w => w.HoldPlatformSubsidyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Permanent("SUBSIDY_BUDGET_EXHAUSTED"));

        var act = () => Create().HoldSubsidyAsync(Guid.NewGuid(), Guid.NewGuid(), 10_000m, default);

        var ex = (await act.Should().ThrowAsync<CommerceDomainException>()).Which;
        ex.ErrorCode.Should().Be("VOUCHER_BUDGET_EXHAUSTED");
        ex.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task A_wallet_outage_while_holding_the_subsidy_fails_closed_with_a_503()
    {
        _wallet.Setup(w => w.HoldPlatformSubsidyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletCallOutcome.Transient(null, "down"));

        var act = () => Create().HoldSubsidyAsync(Guid.NewGuid(), Guid.NewGuid(), 10_000m, default);

        var ex = (await act.Should().ThrowAsync<CommerceDomainException>()).Which;
        ex.ErrorCode.Should().Be("SUBSIDY_UNAVAILABLE");
        ex.StatusCode.Should().Be(503);
    }
}
