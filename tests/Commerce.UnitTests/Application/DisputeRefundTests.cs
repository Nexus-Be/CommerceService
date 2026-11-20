namespace Commerce.UnitTests.Application;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// T14: refunding a sub-order because a dispute was decided for the buyer. Commerce works the amount out, refunds at most once per
/// report, and leaves anything it cannot refund safely (already paid to the seller, parent order refunded) to a person.
/// </summary>
public class DisputeRefundTests : IDisposable
{
    private readonly TestCommerceDbContext _db = TestDbContextFactory.CreateInMemoryDbContext();
    private readonly Mock<IWalletClient> _wallet = new();

    public DisputeRefundTests()
    {
        _wallet
            .Setup(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    public void Dispose() => _db.Dispose();

    private OrderService CreateService() => new(
        _db,
        _wallet.Object,
        new Mock<IFulfillmentClient>().Object,
        new Mock<ICatalogClient>().Object,
        new Mock<IUserClient>().Object,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        NullLogger<OrderService>.Instance);

    private async Task<SubOrder> SeedAsync(
        SubOrderStatus status = SubOrderStatus.Delivered,
        PaymentMethod method = PaymentMethod.Wallet,
        bool paid = true,
        bool escrowSettled = false)
    {
        var order = new Order { Id = Guid.NewGuid(), BuyerId = Guid.NewGuid(), Status = OrderStatus.Paid, TotalAmount = 250000m, PaymentDeadlineAt = DateTimeOffset.UtcNow };
        var sub = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SUB-DISPUTE-001",
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            SellerId = Guid.NewGuid(),
            Status = status,
            PaymentMethod = method,
            ShippingAddress = new ShippingAddressSnapshot("A", "0901234567", "1 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone = "0901234567",
            ItemsAmount = 220000m,
            ShippingFeeAmount = 30000m,
            TotalAmount = 250000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow,
            PaidAt = paid ? DateTimeOffset.UtcNow : null,
            IsEscrowSettled = escrowSettled
        };
        order.SubOrders.Add(sub);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return sub;
    }

    [Fact]
    public async Task Refund_ForADeliveredWalletOrder_RefundsTheFullPaidAmountOnce_UnderAStableKey()
    {
        var sub = await SeedAsync();
        var report = Guid.NewGuid();

        var result = await CreateService().RefundSubOrderForDisputeAsync(sub.Id, report, "Empty box");

        result.Status.Should().Be("Refunded");
        result.Amount.Should().Be(250000m);
        _wallet.Verify(w => w.RefundSubOrderPaymentAsync(
            sub.Id, sub.BuyerId.ToString(), 250000m, "Empty box", $"refund-dispute-{report}-{sub.Id}", It.IsAny<CancellationToken>()), Times.Once);
        var saved = await _db.OrderRefunds.SingleAsync();
        saved.Status.Should().Be(RefundStatus.Completed);
        saved.RequestEventId.Should().Be(report);
        saved.ReasonCode.Should().Be("DISPUTE_RESOLVED");
    }

    [Fact]
    public async Task Refund_CalledTwiceForTheSameReport_MovesMoneyOnlyOnce()
    {
        var sub = await SeedAsync();
        var report = Guid.NewGuid();
        var service = CreateService();

        var first = await service.RefundSubOrderForDisputeAsync(sub.Id, report, null);
        var second = await service.RefundSubOrderForDisputeAsync(sub.Id, report, null);

        first.Status.Should().Be("Refunded");
        second.Status.Should().Be("AlreadyRefunded");
        second.Amount.Should().Be(250000m);
        _wallet.Verify(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        (await _db.OrderRefunds.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Refund_WhenTheSellerWasAlreadyPaid_IsLeftToAPerson_AndNoMoneyMoves()
    {
        var sub = await SeedAsync(status: SubOrderStatus.Completed, escrowSettled: true);

        var result = await CreateService().RefundSubOrderForDisputeAsync(sub.Id, Guid.NewGuid(), null);

        result.Status.Should().Be("ManualProcessingRequired");
        _wallet.Verify(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        (await _db.OrderRefunds.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Refund_WhenTheParentOrderWasAlreadyRefunded_IsLeftToAPerson()
    {
        var sub = await SeedAsync();
        _db.OrderRefunds.Add(new OrderRefund
        {
            Id = Guid.NewGuid(), OrderId = sub.OrderId, SubOrderId = null, RefundType = RefundType.Full, Amount = 250000m,
            Status = RefundStatus.Completed, ReasonCode = "BUYER_CANCELLED", ActorRole = OrderActorRole.Buyer, RequestedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await CreateService().RefundSubOrderForDisputeAsync(sub.Id, Guid.NewGuid(), null);

        result.Status.Should().Be("ManualProcessingRequired");
        _wallet.Verify(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refund_WhenTheWalletDoesNotConfirm_ReportsFailureAndDoesNotMarkItRefunded_ThenARetrySucceeds()
    {
        var sub = await SeedAsync();
        var report = Guid.NewGuid();
        _wallet
            .SetupSequence(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        var service = CreateService();

        var first = await service.RefundSubOrderForDisputeAsync(sub.Id, report, null);
        (await _db.OrderRefunds.SingleAsync()).Status.Should().NotBe(RefundStatus.Completed);
        var retry = await service.RefundSubOrderForDisputeAsync(sub.Id, report, null);

        first.Status.Should().Be("Failed");
        retry.Status.Should().Be("Refunded");
        (await _db.OrderRefunds.CountAsync()).Should().Be(1);
        (await _db.OrderRefunds.SingleAsync()).Status.Should().Be(RefundStatus.Completed);
    }

    [Fact]
    public async Task Refund_WhenTheOrderIsNotDeliveredYet_IsRefused()
    {
        var sub = await SeedAsync(status: SubOrderStatus.Shipped);

        var act = () => CreateService().RefundSubOrderForDisputeAsync(sub.Id, Guid.NewGuid(), null);

        (await act.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be("REFUND_NOT_ALLOWED");
    }

    [Fact]
    public async Task Refund_ForACodOrder_IsLeftToAPerson()
    {
        var sub = await SeedAsync(method: PaymentMethod.Cod);

        var result = await CreateService().RefundSubOrderForDisputeAsync(sub.Id, Guid.NewGuid(), null);

        result.Status.Should().Be("ManualProcessingRequired");
    }

    [Fact]
    public async Task Refund_ForAnUnknownSubOrder_IsNotFound()
    {
        var act = () => CreateService().RefundSubOrderForDisputeAsync(Guid.NewGuid(), Guid.NewGuid(), null);

        await act.Should().ThrowAsync<EntityNotFoundException>();
    }
}
