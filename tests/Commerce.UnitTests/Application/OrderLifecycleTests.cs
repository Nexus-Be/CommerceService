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
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<OrderService>> _mockLogger = new();
    private readonly IDistributedCache _cache;

    public OrderLifecycleTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        _mockUserClient
            .Setup(u => u.AdjustReputationAsync(It.IsAny<ReputationAdjustRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReputationAdjustResponse(Guid.NewGuid(), "Seller", false, false, 65, 66, 1, "Standard"));

        _mockUserClient
            .Setup(u => u.CreatePenaltyAsync(It.IsAny<UserPenaltyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserPenaltyResponse(Guid.NewGuid(), "Seller", false, Guid.NewGuid(), 66, 61, -5, "Standard"));

        _mockFulfillmentClient
            .Setup(f => f.ReleaseStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.ReleaseSubOrderStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.CreateShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentCreationResult("GHN-TRACK-001", "GHN", "https://label.pdf"));

        _mockWalletClient
            .Setup(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.SettleSellerSubOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
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
    public async Task CancelOrderAsync_ShouldCancelOrderAndRefund_WhenOrderIsPaidAndNotShipped()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Paid);

        _mockWalletClient.Setup(w => w.RefundSubOrderPaymentAsync(
            subOrder.Id,
            buyerId.ToString(),
            subOrder.TotalAmount,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateOrderService();
        var request = new CancelOrderRequest(CancellationReasonCode.BuyerChangedMind, "Cancelled before shipment");

        // Act
        await service.CancelOrderAsync(buyerId, order.Id, request, CancellationToken.None);

        // Assert
        var updatedOrder = await _dbContext.Orders.Include(o => o.SubOrders).FirstOrDefaultAsync(o => o.Id == order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.Cancelled);
        updatedOrder.SubOrders.First().Status.Should().Be(SubOrderStatus.Cancelled);

        // Verify Wallet refund was invoked
        _mockWalletClient.Verify(w => w.RefundSubOrderPaymentAsync(
            subOrder.Id,
            buyerId.ToString(),
            subOrder.TotalAmount,
            It.IsAny<string>(),
            It.Is<string>(key => key.StartsWith("refund-suborder-cancel-")),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify stock was released
        _mockFulfillmentClient.Verify(f => f.ReleaseStockAsync(order.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldThrowException_WhenSubOrderIsAlreadyShipped()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, _) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Shipping);

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

        var refund = await _dbContext.OrderRefunds.FirstOrDefaultAsync(r => r.SubOrderId == subOrder.Id);
        refund.Should().NotBeNull();
        refund!.Status.Should().Be(RefundStatus.Completed);
        refund.CompletedAt.Should().NotBeNull();

        _mockWalletClient.Verify(w => w.RefundSubOrderPaymentAsync(
            subOrder.Id,
            buyerId.ToString(),
            subOrder.TotalAmount,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
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

    [Fact]
    public async Task CompleteOrder_ShouldRewardSellerReputation_WhenOrderDelivered()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Delivered);

        var service = CreateOrderService();

        // Act
        var response = await service.CompleteOrderAsync(buyerId, order.Id, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(OrderStatus.Completed.ToString());
        response.CompletedSubOrderCount.Should().Be(1);

        var updatedOrder = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.Completed);

        var updatedSubOrder = await _dbContext.SubOrders.FirstOrDefaultAsync(s => s.Id == subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Completed);

        _mockUserClient.Verify(u => u.AdjustReputationAsync(
            It.Is<ReputationAdjustRequest>(r => r.UserId == sellerId && r.ScoreDelta == 1 && r.Reason == "OrderCompleted"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmReceipt_ShouldRewardSellerReputation()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Delivered);

        var service = CreateOrderService();

        // Act
        var response = await service.BuyerConfirmSubOrderReceiptAsync(buyerId, subOrder.Id, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(SubOrderStatus.Completed.ToString());

        var updatedOrder = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.Completed);

        _mockUserClient.Verify(u => u.AdjustReputationAsync(
            It.Is<ReputationAdjustRequest>(r => r.UserId == sellerId && r.ScoreDelta == 1 && r.Reason == "OrderCompleted"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelOrder_ShouldPenalizeSeller_WhenCancelledWithoutReason()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Confirmed);

        var service = CreateOrderService();
        var cancelRequest = new CancelOrderRequest(
            CancellationReasonCode.CancellationWithoutReason,
            "Seller cancelled without reason");

        // Act
        await service.CancelOrderAsync(sellerId, order.Id, cancelRequest, CancellationToken.None);

        // Assert
        var updatedSubOrder = await _dbContext.SubOrders.FirstOrDefaultAsync(s => s.Id == subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Cancelled);

        _mockUserClient.Verify(u => u.CreatePenaltyAsync(
            It.Is<UserPenaltyRequest>(p => p.UserId == sellerId && p.CustomScoreDeduction == 5 && p.ViolationType == "CancellationWithoutReason"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmReceipt_ShouldSyncPurchasesToCatalog_WithDistinctProductIds()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Delivered);

        var product1 = Guid.NewGuid();
        var product2 = Guid.NewGuid();

        var item1 = new SubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            ProductId = product1,
            SkuId = Guid.NewGuid(),
            SellerId = sellerId,
            ProductNameSnapshot = "Product 1 Variant A",
            Quantity = 1,
            UnitPrice = 100000m,
            UnitPriceAfterDiscount = 100000m,
            LineTotal = 100000m
        };
        var item2 = new SubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            ProductId = product1, // Duplicate parent product
            SkuId = Guid.NewGuid(),
            SellerId = sellerId,
            ProductNameSnapshot = "Product 1 Variant B",
            Quantity = 2,
            UnitPrice = 100000m,
            UnitPriceAfterDiscount = 100000m,
            LineTotal = 200000m
        };
        var item3 = new SubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            ProductId = product2,
            SkuId = Guid.NewGuid(),
            SellerId = sellerId,
            ProductNameSnapshot = "Product 2",
            Quantity = 1,
            UnitPrice = 50000m,
            UnitPriceAfterDiscount = 50000m,
            LineTotal = 50000m
        };
        _dbContext.SubOrderItems.AddRange(item1, item2, item3);
        await _dbContext.SaveChangesAsync();

        _mockCatalogClient
            .Setup(c => c.SyncPurchasesBatchAsync(buyerId, subOrder.Id, It.IsAny<IEnumerable<Guid>>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var service = CreateOrderService();

        // Act
        var response = await service.BuyerConfirmSubOrderReceiptAsync(buyerId, subOrder.Id, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(SubOrderStatus.Completed.ToString());

        _mockCatalogClient.Verify(c => c.SyncPurchasesBatchAsync(
            buyerId,
            subOrder.Id,
            It.Is<IEnumerable<Guid>>(ids => ids.Count() == 3),
            It.IsAny<DateTimeOffset?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmReceipt_ShouldNotFail_WhenCatalogSyncThrowsException()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Delivered);

        var item = new SubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            ProductId = Guid.NewGuid(),
            SkuId = Guid.NewGuid(),
            SellerId = sellerId,
            ProductNameSnapshot = "Product 1",
            Quantity = 1,
            UnitPrice = 100000m,
            UnitPriceAfterDiscount = 100000m,
            LineTotal = 100000m
        };
        _dbContext.SubOrderItems.Add(item);
        await _dbContext.SaveChangesAsync();

        _mockCatalogClient
            .Setup(c => c.SyncPurchasesBatchAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("Catalog connection failed"));

        var service = CreateOrderService();

        // Act
        var response = await service.BuyerConfirmSubOrderReceiptAsync(buyerId, subOrder.Id, CancellationToken.None);

        // Assert: Non-blocking fail-safe: order still completes
        response.Should().NotBeNull();
        response.Status.Should().Be(SubOrderStatus.Completed.ToString());

        var updatedSubOrder = await _dbContext.SubOrders.FirstOrDefaultAsync(s => s.Id == subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Completed);
    }

    [Fact]
    public async Task CompleteOrder_ShouldSyncPurchasesToCatalog_ForAllSubOrders()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var seller1 = Guid.NewGuid();
        var (order, subOrder1) = await SeedOrderWithSubOrderAsync(buyerId, seller1, OrderStatus.Paid, SubOrderStatus.Delivered);

        var product1 = Guid.NewGuid();
        var item1 = new SubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder1.Id,
            ProductId = product1,
            SkuId = Guid.NewGuid(),
            SellerId = seller1,
            ProductNameSnapshot = "Product 1",
            Quantity = 1,
            UnitPrice = 100000m,
            UnitPriceAfterDiscount = 100000m,
            LineTotal = 100000m
        };
        _dbContext.SubOrderItems.Add(item1);

        var seller2 = Guid.NewGuid();
        var subOrder2 = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = $"SUB-{DateTime.UtcNow:yyyyMMdd}-002",
            OrderId = order.Id,
            BuyerId = buyerId,
            SellerId = seller2,
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone = "0901234567",
            ItemsAmount = 150000m,
            ShippingFeeAmount = 30000m,
            TotalAmount = 180000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaidAt = DateTimeOffset.UtcNow
        };

        var product2 = Guid.NewGuid();
        var item2 = new SubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder2.Id,
            ProductId = product2,
            SkuId = Guid.NewGuid(),
            SellerId = seller2,
            ProductNameSnapshot = "Product 2",
            Quantity = 1,
            UnitPrice = 150000m,
            UnitPriceAfterDiscount = 150000m,
            LineTotal = 150000m
        };
        subOrder2.Items.Add(item2);

        _dbContext.SubOrders.Add(subOrder2);
        await _dbContext.SaveChangesAsync();

        _mockCatalogClient
            .Setup(c => c.SyncPurchasesBatchAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var service = CreateOrderService();

        // Act
        var response = await service.CompleteOrderAsync(buyerId, order.Id, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(OrderStatus.Completed.ToString());
        response.CompletedSubOrderCount.Should().Be(2);

        _mockCatalogClient.Verify(c => c.SyncPurchasesBatchAsync(
            buyerId,
            subOrder1.Id,
            It.Is<IEnumerable<Guid>>(ids => ids.Contains(product1)),
            It.IsAny<DateTimeOffset?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockCatalogClient.Verify(c => c.SyncPurchasesBatchAsync(
            buyerId,
            subOrder2.Id,
            It.Is<IEnumerable<Guid>>(ids => ids.Contains(product2)),
            It.IsAny<DateTimeOffset?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
