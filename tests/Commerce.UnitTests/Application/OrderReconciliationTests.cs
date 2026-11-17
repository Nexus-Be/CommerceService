namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Commerce.Api.Workers;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class OrderReconciliationTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IServiceProvider> _mockServiceProvider = new();

    public OrderReconciliationTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();

        var scopedServices = new Mock<IServiceProvider>();
        scopedServices.Setup(s => s.GetService(typeof(ICommerceDbContext))).Returns(_dbContext);
        scopedServices.Setup(s => s.GetService(typeof(IFulfillmentClient))).Returns(_mockFulfillmentClient.Object);
        scopedServices.Setup(s => s.GetService(typeof(IWalletClient))).Returns(_mockWalletClient.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scopedServices.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(s => s.CreateScope()).Returns(scope.Object);

        _mockServiceProvider.Setup(s => s.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private PaymentTimeoutWorker CreateWorker()
    {
        return new PaymentTimeoutWorker(_mockServiceProvider.Object, NullLogger<PaymentTimeoutWorker>.Instance);
    }

    [Fact]
    public async Task ReconcilePendingRefundsAsync_ShouldRetryAndCompleteRefund_ForSubOrderRefund()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SO-RECON-001",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Cancelled,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        var refundId = Guid.NewGuid();
        var pendingRefund = new OrderRefund
        {
            Id = refundId,
            OrderId = orderId,
            SubOrderId = subOrderId,
            Amount = 150000m,
            RefundType = RefundType.Full,
            Status = RefundStatus.Requested,
            ReasonCode = "SELLER_REJECTED",
            ReasonNote = "Seller out of stock retry",
            ActorRole = OrderActorRole.System,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        _dbContext.SubOrders.Add(subOrder);
        _dbContext.OrderRefunds.Add(pendingRefund);
        await _dbContext.SaveChangesAsync();

        _mockWalletClient
            .Setup(w => w.RefundSubOrderPaymentAsync(subOrderId, buyerId.ToString(), 150000m, It.IsAny<string>(), $"refund-retry-{refundId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var worker = CreateWorker();

        // Act
        await worker.ReconcilePendingRefundsAsync(_dbContext, _mockWalletClient.Object, CancellationToken.None);

        // Assert
        var updatedRefund = await _dbContext.OrderRefunds.FindAsync(refundId);
        updatedRefund!.Status.Should().Be(RefundStatus.Completed);
        updatedRefund.CompletedAt.Should().NotBeNull();

        _mockWalletClient.Verify(w => w.RefundSubOrderPaymentAsync(
            subOrderId,
            buyerId.ToString(),
            150000m,
            It.IsAny<string>(),
            $"refund-retry-{refundId}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconcilePendingRefundsAsync_ShouldRetryAndCompleteRefund_ForMasterOrderRefund()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Cancelled,
            TotalAmount = 300000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        var refundId = Guid.NewGuid();
        var pendingRefund = new OrderRefund
        {
            Id = refundId,
            OrderId = orderId,
            SubOrderId = null,
            Amount = 300000m,
            RefundType = RefundType.Full,
            Status = RefundStatus.Requested,
            ReasonCode = "BUYER_CANCELLED",
            ReasonNote = "Buyer cancelled entire order",
            ActorRole = OrderActorRole.Buyer,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        _dbContext.Orders.Add(order);
        _dbContext.OrderRefunds.Add(pendingRefund);
        await _dbContext.SaveChangesAsync();

        _mockWalletClient
            .Setup(w => w.RefundOrderPaymentAsync(orderId, buyerId.ToString(), 300000m, It.IsAny<string>(), $"refund-retry-{refundId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var worker = CreateWorker();

        // Act
        await worker.ReconcilePendingRefundsAsync(_dbContext, _mockWalletClient.Object, CancellationToken.None);

        // Assert
        var updatedRefund = await _dbContext.OrderRefunds.FindAsync(refundId);
        updatedRefund!.Status.Should().Be(RefundStatus.Completed);
        updatedRefund.CompletedAt.Should().NotBeNull();

        _mockWalletClient.Verify(w => w.RefundOrderPaymentAsync(
            orderId,
            buyerId.ToString(),
            300000m,
            It.IsAny<string>(),
            $"refund-retry-{refundId}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconcilePendingRefundsAsync_ShouldKeepRequestedStatus_WhenWalletRefundFails()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SO-RECON-FAIL-001",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Cancelled,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        var refundId = Guid.NewGuid();
        var pendingRefund = new OrderRefund
        {
            Id = refundId,
            OrderId = orderId,
            SubOrderId = subOrderId,
            Amount = 150000m,
            RefundType = RefundType.Full,
            Status = RefundStatus.Requested,
            ReasonCode = "SELLER_REJECTED",
            ActorRole = OrderActorRole.System,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        _dbContext.SubOrders.Add(subOrder);
        _dbContext.OrderRefunds.Add(pendingRefund);
        await _dbContext.SaveChangesAsync();

        _mockWalletClient
            .Setup(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var worker = CreateWorker();

        // Act
        await worker.ReconcilePendingRefundsAsync(_dbContext, _mockWalletClient.Object, CancellationToken.None);

        // Assert
        var updatedRefund = await _dbContext.OrderRefunds.FindAsync(refundId);
        updatedRefund!.Status.Should().Be(RefundStatus.Requested);
        updatedRefund.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task ReconcileUnsettledEscrowsAsync_ShouldSettleAndSetIsEscrowSettledTrue_WhenWalletSucceeds()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SO-UNSETTLED-001",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Completed,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            ItemsAmount = 200000m,
            SellerReceiveAmount = 190000m,
            PlatformFeeAmount = 10000m,
            TotalAmount = 200000m,
            IsEscrowSettled = false,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-60),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        _mockWalletClient
            .Setup(w => w.SettleSellerSubOrderAsync(subOrderId, sellerId, 190000m, $"settle-{subOrderId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var worker = CreateWorker();

        // Act
        await worker.ReconcileUnsettledEscrowsAsync(_dbContext, _mockWalletClient.Object, CancellationToken.None);

        // Assert
        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrderId);
        updatedSubOrder!.IsEscrowSettled.Should().BeTrue();

        _mockWalletClient.Verify(w => w.SettleSellerSubOrderAsync(
            subOrderId,
            sellerId,
            190000m,
            $"settle-{subOrderId}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconcileUnsettledEscrowsAsync_ShouldKeepIsEscrowSettledFalse_WhenWalletFails()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SO-UNSETTLED-FAIL-001",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Completed,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            ItemsAmount = 200000m,
            SellerReceiveAmount = 190000m,
            PlatformFeeAmount = 10000m,
            TotalAmount = 200000m,
            IsEscrowSettled = false,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-60),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        _mockWalletClient
            .Setup(w => w.SettleSellerSubOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var worker = CreateWorker();

        // Act
        await worker.ReconcileUnsettledEscrowsAsync(_dbContext, _mockWalletClient.Object, CancellationToken.None);

        // Assert
        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrderId);
        updatedSubOrder!.IsEscrowSettled.Should().BeFalse();
    }
}
