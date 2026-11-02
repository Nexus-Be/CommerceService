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

public class OrderLifecycleTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<ILogger<OrderService>> _mockLogger = new();
    private readonly IDistributedCache _cache;

    public OrderLifecycleTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        _mockFulfillmentClient
            .Setup(f => f.ReleaseStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.ReleaseSubOrderStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.CreateShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentCreationResult("GHN-TRACK-001", "GHN", "https://label.pdf"));
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
            _cache,
            _mockLogger.Object);
    }

    private async Task<(Order order, SubOrder subOrder)> SeedOrderWithSubOrderAsync(
        Guid buyerId,
        Guid sellerId,
        OrderStatus orderStatus = OrderStatus.PendingPayment,
        SubOrderStatus subOrderStatus = SubOrderStatus.PendingPayment,
        PaymentMethod paymentMethod = PaymentMethod.Wallet)
    {
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = orderStatus,
            TotalAmount = 250000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaymentAttemptCount = 1
        };

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = $"SUB-{DateTime.UtcNow:yyyyMMdd}-001",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = subOrderStatus,
            PaymentMethod = paymentMethod,
            ShippingAddress = new ShippingAddressSnapshot("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone = "0901234567",
            ItemsAmount = 220000m,
            ShippingFeeAmount = 30000m,
            TotalAmount = 250000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaidAt = orderStatus == OrderStatus.Paid ? DateTimeOffset.UtcNow : null
        };

        order.SubOrders.Add(subOrder);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        return (order, subOrder);
    }

    // =========================================================================
    // ORD-LIFE-01 & 02: CANCEL ORDER & VALIDATION
    // =========================================================================

    [Fact]
    public async Task CancelOrderAsync_ShouldCancelOrderAndReleaseStock_WhenPendingPayment()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, _) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.PendingPayment, SubOrderStatus.PendingPayment);

        var service = CreateOrderService();
        var request = new CancelOrderRequest(CancellationReasonCode.BuyerChangedMind, "Doi y khong mua nua");

        // Act
        await service.CancelOrderAsync(buyerId, order.Id, request, CancellationToken.None);

        // Assert
        var updatedOrder = await _dbContext.Orders.Include(o => o.SubOrders).FirstOrDefaultAsync(o => o.Id == order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.Cancelled);
        updatedOrder.SubOrders.First().Status.Should().Be(SubOrderStatus.Cancelled);

        // Verify OrderCancellation record created
        var cancellation = await _dbContext.OrderCancellations.FirstOrDefaultAsync(c => c.SubOrderId == updatedOrder.SubOrders.First().Id);
        cancellation.Should().NotBeNull();
        cancellation!.ReasonCode.Should().Be(CancellationReasonCode.BuyerChangedMind);

        // Verify Fulfillment was asked to release stock
        _mockFulfillmentClient.Verify(f => f.ReleaseStockAsync(order.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldThrowException_WhenOrderIsAlreadyPaid()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, _) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Paid);

        var service = CreateOrderService();
        var request = new CancelOrderRequest(CancellationReasonCode.BuyerChangedMind);

        // Act
        var act = () => service.CancelOrderAsync(buyerId, order.Id, request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "ORDER_CANNOT_BE_CANCELLED");

        _mockFulfillmentClient.Verify(f => f.ReleaseStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldThrowForbidden_WhenUserIsNotOwner()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var (order, _) = await SeedOrderWithSubOrderAsync(buyerId, Guid.NewGuid(), OrderStatus.PendingPayment, SubOrderStatus.PendingPayment);

        var service = CreateOrderService();
        var request = new CancelOrderRequest();

        // Act: User khac co tinh huy don
        var act = () => service.CancelOrderAsync(otherUserId, order.Id, request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "FORBIDDEN");
    }

    // =========================================================================
    // ORD-LIFE-04 & 05: SELLER ACCEPT SUB-ORDER
    // =========================================================================

    [Fact]
    public async Task SellerAcceptSubOrderAsync_ShouldConfirmSubOrderAndCreateShipment_WhenPaid()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Paid);

        var service = CreateOrderService();
        var request = new SellerAcceptOrderRequest(subOrder.Id, "Shop da chuan bi xong hang");

        // Act
        await service.SellerAcceptSubOrderAsync(sellerId, order.Id, request, CancellationToken.None);

        // Assert
        var updatedSubOrder = await _dbContext.SubOrders.FirstOrDefaultAsync(s => s.Id == subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Confirmed);
        updatedSubOrder.SellerAcceptedAt.Should().NotBeNull();

        // Verify GHN shipment created
        _mockFulfillmentClient.Verify(f => f.CreateShipmentAsync(subOrder.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SellerAcceptSubOrderAsync_ShouldThrowException_WhenOnlineOrderNotPaidYet()
    {
        // Arrange: Don Online nhung con PendingPayment
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.PendingPayment, SubOrderStatus.PendingPayment, PaymentMethod.Wallet);

        var service = CreateOrderService();
        var request = new SellerAcceptOrderRequest(subOrder.Id);

        // Act
        var act = () => service.SellerAcceptSubOrderAsync(sellerId, order.Id, request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "INVALID_STATUS");

        _mockFulfillmentClient.Verify(f => f.CreateShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================
    // ORD-LIFE-06 & 07: SELLER REJECT SUB-ORDER
    // =========================================================================

    [Fact]
    public async Task SellerRejectSubOrderAsync_ShouldCancelSubOrder_WhenValid()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Paid);

        var service = CreateOrderService();
        var request = new SellerRejectOrderRequest(subOrder.Id, CancellationReasonCode.OutOfStock, "Tam thoi het mau nay");

        // Act
        await service.SellerRejectSubOrderAsync(sellerId, order.Id, request, CancellationToken.None);

        // Assert
        var updatedSubOrder = await _dbContext.SubOrders.FirstOrDefaultAsync(s => s.Id == subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Cancelled);
        updatedSubOrder.CancelledAt.Should().NotBeNull();

        var cancellation = await _dbContext.OrderCancellations.FirstOrDefaultAsync(c => c.SubOrderId == subOrder.Id);
        cancellation.Should().NotBeNull();
        cancellation!.ReasonCode.Should().Be(CancellationReasonCode.OutOfStock);
        cancellation.ActorRole.Should().Be(OrderActorRole.Seller);
    }

    [Fact]
    public async Task SellerRejectSubOrderAsync_ShouldThrowException_WhenOrderAlreadyShipped()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Shipping);

        var service = CreateOrderService();
        var request = new SellerRejectOrderRequest(subOrder.Id);

        // Act
        var act = () => service.SellerRejectSubOrderAsync(sellerId, order.Id, request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "INVALID_STATUS");
    }
}
