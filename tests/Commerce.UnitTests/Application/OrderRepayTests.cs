namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
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

public class OrderRepayTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<OrderService>> _mockLogger = new();
    private readonly IDistributedCache _cache;

    public OrderRepayTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        _mockFulfillmentClient
            .Setup(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletPaymentResult(true, Guid.NewGuid().ToString(), null, null));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private OrderService CreateOrderService()
    {
        return new OrderService(
            _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockUserClient.Object,
            _cache,
            _mockLogger.Object);
    }

    private async Task<(Order order, SubOrder subOrder)> SeedPendingOrderAsync(
        Guid buyerId,
        Guid sellerId,
        decimal totalAmount = 150000m,
        PaymentMethod paymentMethod = PaymentMethod.Wallet,
        DateTimeOffset? deadline = null)
    {
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.PendingPayment,
            TotalAmount = totalAmount,
            PaymentDeadlineAt = deadline ?? DateTimeOffset.UtcNow.AddMinutes(15),
            PaymentAttemptCount = 1
        };

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = $"SUB-{DateTime.UtcNow:yyyyMMdd}-001",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = paymentMethod,
            ShippingAddress = new ShippingAddressSnapshot("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone = "0901234567",
            ItemsAmount = totalAmount - 30000m,
            ShippingFeeAmount = 30000m,
            SellerReceiveAmount = totalAmount - 30000m - 5000m,
            PlatformFeeAmount = 5000m,
            TotalAmount = totalAmount,
            PaymentDeadlineAt = deadline ?? DateTimeOffset.UtcNow.AddMinutes(15)
        };

        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        return (order, subOrder);
    }

    [Fact]
    public async Task RepayOrderAsync_ShouldSucceed_WhenPendingPaymentWithinDeadline()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedPendingOrderAsync(buyerId, sellerId, 250000m);

        var txId = Guid.NewGuid();
        _mockWalletClient
            .Setup(w => w.PayOrderAsync(order.Id, buyerId, 250000m, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletPaymentResult(true, txId.ToString(), null, null));

        var service = CreateOrderService();

        // Act
        var response = await service.RepayOrderAsync(buyerId, order.Id, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.OrderId.Should().Be(order.Id);
        response.Status.Should().Be("Paid");
        response.TotalAmount.Should().Be(250000m);
        response.WalletTransactionId.Should().Be(txId);

        var updatedOrder = await _dbContext.Orders.Include(o => o.SubOrders).FirstAsync(o => o.Id == order.Id);
        updatedOrder.Status.Should().Be(OrderStatus.Paid);
        updatedOrder.WalletTransactionId.Should().Be(txId);
        updatedOrder.PaymentAttemptCount.Should().Be(2);

        var updatedSubOrder = updatedOrder.SubOrders.First();
        updatedSubOrder.Status.Should().Be(SubOrderStatus.Paid);
        updatedSubOrder.PaidAt.Should().NotBeNull();
        updatedSubOrder.WalletTransactionId.Should().Be(txId);

        _mockFulfillmentClient.Verify(f => f.CommitStockAsync(order.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RepayOrderAsync_ShouldThrowForbidden_WhenUserIsNotOwner()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var (order, _) = await SeedPendingOrderAsync(buyerId, Guid.NewGuid());

        var service = CreateOrderService();

        // Act
        var act = () => service.RepayOrderAsync(otherUserId, order.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "FORBIDDEN");
    }

    [Fact]
    public async Task RepayOrderAsync_ShouldThrowInvalidStatus_WhenOrderIsNotPendingPayment()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var (order, _) = await SeedPendingOrderAsync(buyerId, Guid.NewGuid());
        order.Status = OrderStatus.Paid;
        await _dbContext.SaveChangesAsync();

        var service = CreateOrderService();

        // Act
        var act = () => service.RepayOrderAsync(buyerId, order.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "INVALID_STATUS");
    }

    [Fact]
    public async Task RepayOrderAsync_ShouldThrowDeadlineExpired_WhenDeadlineHasPassed()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var expiredDeadline = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (order, _) = await SeedPendingOrderAsync(buyerId, Guid.NewGuid(), deadline: expiredDeadline);

        var service = CreateOrderService();

        // Act
        var act = () => service.RepayOrderAsync(buyerId, order.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "ORDER_PAYMENT_DEADLINE_EXPIRED" && e.Message == "Payment deadline for this order has expired.");
    }

    [Fact]
    public async Task RepayOrderAsync_ShouldThrowInvalidPaymentMethod_WhenOrderIsCod()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var (order, subOrder) = await SeedPendingOrderAsync(buyerId, Guid.NewGuid(), paymentMethod: PaymentMethod.Cod);

        var service = CreateOrderService();

        // Act
        var act = () => service.RepayOrderAsync(buyerId, order.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "INVALID_PAYMENT_METHOD" && e.Message == "COD orders cannot be paid online.");
    }

    [Fact]
    public async Task RepayOrderAsync_ShouldThrowInsufficientBalance_WhenWalletReturnsInsufficientBalance()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var (order, _) = await SeedPendingOrderAsync(buyerId, Guid.NewGuid(), 250000m);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(order.Id, buyerId, 250000m, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletPaymentResult(false, null, "INSUFFICIENT_WALLET_BALANCE", "Balance insufficient"));

        var service = CreateOrderService();

        // Act
        var act = () => service.RepayOrderAsync(buyerId, order.Id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "INSUFFICIENT_WALLET_BALANCE" && e.Message == "Your wallet balance is insufficient to pay for this order.");

        _mockFulfillmentClient.Verify(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
