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

public class OrderFinancialQueryTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<OrderService>> _mockLogger = new();
    private readonly IDistributedCache _cache;

    public OrderFinancialQueryTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
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

    [Fact]
    public async Task GetOrderRefundsAsync_ShouldReturnRefunds_WhenAuthorizedBuyer()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Cancelled,
            TotalAmount = 200000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow
        };

        var refund1 = new OrderRefund
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            SubOrderId = subOrderId,
            Amount = 120000m,
            RefundType = RefundType.Full,
            Status = RefundStatus.Completed,
            ReasonCode = "SELLER_REJECTED",
            ReasonNote = "Seller out of stock",
            ActorRole = OrderActorRole.System,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow
        };

        var refund2 = new OrderRefund
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            SubOrderId = null,
            Amount = 80000m,
            RefundType = RefundType.Partial,
            Status = RefundStatus.Completed,
            ReasonCode = "BUYER_CANCELLED",
            ReasonNote = "Cancelled item",
            ActorRole = OrderActorRole.Buyer,
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Orders.Add(order);
        _dbContext.OrderRefunds.AddRange(refund1, refund2);
        await _dbContext.SaveChangesAsync();

        var service = CreateOrderService();

        // Act
        var result = await service.GetOrderRefundsAsync(buyerId, orderId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain(r => r.Id == refund1.Id && r.Amount == 120000m && r.Status == "Completed");
        result.Should().Contain(r => r.Id == refund2.Id && r.Amount == 80000m && r.RefundType == "Partial");
    }

    [Fact]
    public async Task GetOrderRefundsAsync_ShouldThrowForbidden_WhenUserIsNotOwner()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Cancelled,
            TotalAmount = 200000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow
        };

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        var service = CreateOrderService();

        // Act
        var act = () => service.GetOrderRefundsAsync(otherUserId, orderId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "FORBIDDEN");
    }

    [Fact]
    public async Task GetOrderRefundsAsync_ShouldThrowNotFound_WhenOrderDoesNotExist()
    {
        // Arrange
        var service = CreateOrderService();

        // Act
        var act = () => service.GetOrderRefundsAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<EntityNotFoundException>()
            .Where(e => e.ErrorCode == "ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task GetSubOrderFinancialBreakdownAsync_ShouldReturnBreakdown_WhenAuthorizedSeller()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-FIN-001",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Completed,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            ItemsAmount = 300000m,
            ProductDiscountAmount = 20000m,
            ShopDiscountAmount = 10000m,
            SystemDiscountAmount = 15000m,
            ShippingFeeAmount = 25000m,
            PlatformFeeAmount = 13500m,
            SellerReceiveAmount = 256500m,
            TotalAmount = 280000m,
            IsEscrowSettled = true,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-60),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var service = CreateOrderService();

        // Act
        var breakdown = await service.GetSubOrderFinancialBreakdownAsync(sellerId, subOrderId, CancellationToken.None);

        // Assert
        breakdown.Should().NotBeNull();
        breakdown.SubOrderId.Should().Be(subOrderId);
        breakdown.SubOrderNo.Should().Be("SUB-FIN-001");
        breakdown.SellerId.Should().Be(sellerId);
        breakdown.BuyerId.Should().Be(buyerId);
        breakdown.TotalAmount.Should().Be(280000m);
        breakdown.ItemsAmount.Should().Be(300000m);
        breakdown.ProductDiscountAmount.Should().Be(20000m);
        breakdown.ShopDiscountAmount.Should().Be(10000m);
        breakdown.SystemDiscountAmount.Should().Be(15000m);
        breakdown.ShippingFeeAmount.Should().Be(25000m);
        breakdown.PlatformFeeAmount.Should().Be(13500m);
        breakdown.SellerReceiveAmount.Should().Be(256500m);
        breakdown.IsEscrowSettled.Should().BeTrue();
        breakdown.Status.Should().Be("Completed");
        breakdown.PaymentMethod.Should().Be("Wallet");
    }

    [Fact]
    public async Task GetSubOrderFinancialBreakdownAsync_ShouldThrowForbidden_WhenUserIsNotSellerNorBuyer()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-FIN-FORBIDDEN",
            OrderId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Completed,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-60)
        };

        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var service = CreateOrderService();

        // Act
        var act = () => service.GetSubOrderFinancialBreakdownAsync(otherUserId, subOrderId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "FORBIDDEN");
    }

    [Fact]
    public async Task GetSubOrderFinancialBreakdownAsync_ShouldThrowNotFound_WhenSubOrderDoesNotExist()
    {
        // Arrange
        var service = CreateOrderService();

        // Act
        var act = () => service.GetSubOrderFinancialBreakdownAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<EntityNotFoundException>()
            .Where(e => e.ErrorCode == "SUB_ORDER_NOT_FOUND");
    }
}
