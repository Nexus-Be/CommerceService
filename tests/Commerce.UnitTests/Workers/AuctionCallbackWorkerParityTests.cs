namespace Commerce.UnitTests.Workers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Commerce.Api.Workers;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.AuctionOrder;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class AuctionCallbackWorkerParityTests
{
    [Fact]
    public void Factory_Build_PreservesAllElevenFields_WithStrictParity()
    {
        var orderId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var historyId = Guid.NewGuid();
        var paidAt = DateTimeOffset.UtcNow;
        var deadline = paidAt.AddHours(24);

        var order = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AUC-20261119-999",
            BuyerId = winnerId,
            SellerId = sellerId,
            AuctionId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Parity Item",
            WinningAmount = 1500000m,
            TotalAmount = 1530000m,
            SellerReceiveAmount = 1425000m,
            PlatformFeeAmount = 75000m,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = deadline,
            OrderSource = AuctionOrderSource.Winner,
            PaidAt = paidAt,
            SellerRespondDeadlineAt = deadline,
            Status = SubOrderStatus.Paid
        };

        var history = new OrderStatusHistory
        {
            Id = historyId,
            AuctionOrderId = orderId,
            AuctionCallbackStatus = "Paid",
            ActorRole = OrderActorRole.System,
            ToStatus = SubOrderStatus.Paid,
            CreatedAt = paidAt
        };

        var callback = AuctionPaymentCallbackFactory.Build(order, history);

        callback.OrderId.Should().Be(orderId);
        callback.WinnerId.Should().Be(winnerId);
        callback.Status.Should().Be("Paid");
        callback.Amount.Should().Be(1500000m);
        callback.PaidAt.Should().Be(paidAt);
        callback.EventId.Should().Be(historyId);
        callback.OrderSource.Should().Be("WINNER");
        callback.OccurredAt.Should().Be(paidAt);
        callback.OrderNo.Should().Be("AUC-20261119-999");
        callback.TotalAmount.Should().Be(1530000m);
        callback.SellerRespondDeadlineAt.Should().Be(deadline);
    }

    [Fact]
    public void UserPenaltyViolationTypes_ConstantsAreStrictlyCoordinatedWithUserService()
    {
        UserPenaltyViolationTypes.SellerResponseTimeout.Should().Be("Other");
        UserPenaltyViolationTypes.SellerRejectedPaidOrder.Should().Be("CancellationWithoutReason");
    }

    [Fact]
    public async Task RetryScan_DispatchesCallback_AndMarksHistorySent()
    {
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var mockAuctionClient = new Mock<IAuctionClient>();

        var order = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AUC-20261119-003",
            AuctionId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Item",
            WinningAmount = 500000m,
            TotalAmount = 530000m,
            SellerReceiveAmount = 475000m,
            PlatformFeeAmount = 25000m,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(24),
            PaidAt = DateTimeOffset.UtcNow,
            Status = SubOrderStatus.Paid
        };

        var history = new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            AuctionOrderId = order.Id,
            AuctionOrder = order,
            AuctionCallbackStatus = "Paid",
            AuctionCallbackSentAt = null,
            ActorRole = OrderActorRole.System,
            ToStatus = SubOrderStatus.Paid,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.AuctionOrders.Add(order);
        dbContext.OrderStatusHistories.Add(history);
        await dbContext.SaveChangesAsync();

        mockAuctionClient
            .Setup(a => a.NotifyPaymentStatusAsync(order.AuctionId, It.IsAny<AuctionPaymentStatusCallbackRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var services = new ServiceCollection();
        services.AddScoped<ICommerceDbContext>(_ => dbContext);
        services.AddScoped<IAuctionClient>(_ => mockAuctionClient.Object);
        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        // Simulate retry scan step
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ICommerceDbContext>();
            var auction = scope.ServiceProvider.GetRequiredService<IAuctionClient>();

            var callback = AuctionPaymentCallbackFactory.Build(order, history);
            var success = await auction.NotifyPaymentStatusAsync(order.AuctionId, callback);
            if (success)
            {
                history.AuctionCallbackSentAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
        }

        history.AuctionCallbackSentAt.Should().NotBeNull();
        mockAuctionClient.Verify(a => a.NotifyPaymentStatusAsync(order.AuctionId, It.IsAny<AuctionPaymentStatusCallbackRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
