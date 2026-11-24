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
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class OrderSettlementRefundTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = FulfillmentMocks.WithConfirmedLocations();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<OrderService>> _mockLogger = new();
    private readonly IDistributedCache _cache;

    public OrderSettlementRefundTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        _mockFulfillmentClient
            .Setup(f => f.ReleaseSubOrderStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletRefundResult.Success());

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
        OrderStatus orderStatus = OrderStatus.Paid,
        SubOrderStatus subOrderStatus = SubOrderStatus.Delivered,
        decimal totalAmount = 150000m,
        decimal sellerReceiveAmount = 135000m)
    {
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = orderStatus,
            TotalAmount = totalAmount,
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
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone = "0901234567",
            ItemsAmount = totalAmount - 30000m,
            ShippingFeeAmount = 30000m,
            SellerReceiveAmount = sellerReceiveAmount,
            TotalAmount = totalAmount,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaidAt = orderStatus == OrderStatus.Paid ? DateTimeOffset.UtcNow : null
        };

        order.SubOrders.Add(subOrder);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        return (order, subOrder);
    }

    [Fact]
    public async Task BuyerConfirmSubOrderReceipt_ShouldCallSettleSellerSubOrder_WithCorrectAmount()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (_, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Delivered, 150000m, 135000m);

        var service = CreateOrderService();

        // Act
        var response = await service.BuyerConfirmSubOrderReceiptAsync(buyerId, subOrder.Id, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be("Completed");

        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Completed);
        updatedSubOrder.CompletedAt.Should().NotBeNull();

        _mockWalletClient.Verify(w => w.SettleSellerSubOrderAsync(
            subOrder.Id,
            sellerId,
            135000m,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuyerConfirmSubOrderReceipt_ShouldSkipSettle_WhenSellerReceiveAmountIsZero()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (_, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Delivered, 30000m, 0m);

        var service = CreateOrderService();

        // Act
        var response = await service.BuyerConfirmSubOrderReceiptAsync(buyerId, subOrder.Id, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be("Completed");

        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Completed);

        _mockWalletClient.Verify(w => w.SettleSellerSubOrderAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuyerConfirmSubOrderReceipt_ShouldCompleteStatus_WhenSettlementReturnsFalseDueTo409Conflict()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (_, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Delivered, 150000m, 135000m);

        _mockWalletClient
            .Setup(w => w.SettleSellerSubOrderAsync(subOrder.Id, sellerId, 135000m, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateOrderService();

        // Act
        var response = await service.BuyerConfirmSubOrderReceiptAsync(buyerId, subOrder.Id, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be("Completed");

        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Completed);
    }

    [Fact]
    public async Task SellerRejectSubOrder_ShouldTriggerRefundSubOrder_AndRecordCompletedRefund()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Paid, 150000m, 135000m);

        var service = CreateOrderService();
        var request = new SellerRejectOrderRequest(subOrder.Id, CancellationReasonCode.OutOfStock, "Out of stock");

        // Act
        await service.SellerRejectSubOrderAsync(sellerId, order.Id, request, CancellationToken.None);

        // Assert
        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Cancelled);

        var refund = await _dbContext.OrderRefunds.FirstOrDefaultAsync(r => r.SubOrderId == subOrder.Id);
        refund.Should().NotBeNull();
        refund!.Status.Should().Be(RefundStatus.Completed);
        refund.Amount.Should().Be(150000m);

        _mockWalletClient.Verify(w => w.RefundSubOrderPaymentAsync(subOrder.Id, It.IsAny<Guid>(), buyerId.ToString(), 150000m, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // US4: DELIVERY EXCEPTION & AUTOMATED RETURN/REFUND TESTS (T023)
    // =========================================================================

    [Fact]
    public async Task DeliveryAttemptFailed_ShouldRetainShippedStatus_AndNotCancelOrder()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Shipped, 150000m, 135000m);

        var service = CreateOrderService();

        // Act
        await service.UpdateSubOrderDeliveryStatusAsync(subOrder.Id, new UpdateDeliveryStatusRequest(
            Status: "DeliveryAttemptFailed",
            DeliveredAt: DateTimeOffset.UtcNow,
            Note: "Customer phone unreachable, rescheduling tomorrow"
        ), CancellationToken.None);

        // Assert
        var updatedSubOrder = await _dbContext.SubOrders.Include(s => s.StatusHistories).FirstAsync(s => s.Id == subOrder.Id);
        updatedSubOrder.Status.Should().Be(SubOrderStatus.Shipped);
        updatedSubOrder.StatusHistories.Should().Contain(h => h.Reason != null && h.Reason.Contains("DeliveryAttemptFailed"));

        var cancellations = await _dbContext.OrderCancellations.Where(c => c.SubOrderId == subOrder.Id).ToListAsync();
        cancellations.Should().BeEmpty();
    }

    [Fact]
    public async Task Returned_WhenPaid_ShouldCancelOrder_IssueWalletRefund_AndReleaseStock()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Shipped, 150000m, 135000m);

        var skuId = Guid.NewGuid();
        subOrder.Items.Add(new SubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            ProductId = Guid.NewGuid(),
            SkuId = skuId,
            SellerId = sellerId,
            ProductNameSnapshot = "Returned Product",
            Quantity = 2,
            UnitPrice = 60000m,
            UnitPriceAfterDiscount = 60000m,
            LineTotal = 120000m
        });
        subOrder.PaidAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync();

        _mockFulfillmentClient
            .Setup(f => f.ReleaseSubOrderStockAsync(order.Id, It.IsAny<List<StockReservationItem>>(), "PackageReturned", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateOrderService();

        // Act
        await service.UpdateSubOrderDeliveryStatusAsync(subOrder.Id, new UpdateDeliveryStatusRequest(
            Status: "Returned",
            DeliveredAt: DateTimeOffset.UtcNow,
            Note: "Package returned to sender warehouse"
        ), CancellationToken.None);

        // Assert 1: Status Cancelled
        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Cancelled);

        // Assert 2: OrderCancellation recorded with PackageReturned
        var cancellation = await _dbContext.OrderCancellations.FirstOrDefaultAsync(c => c.SubOrderId == subOrder.Id);
        cancellation.Should().NotBeNull();
        cancellation!.ReasonCode.Should().Be(CancellationReasonCode.PackageReturned);

        // Assert 3: OrderRefund recorded and wallet client invoked
        var refund = await _dbContext.OrderRefunds.FirstOrDefaultAsync(r => r.SubOrderId == subOrder.Id);
        refund.Should().NotBeNull();
        refund!.Status.Should().Be(RefundStatus.Completed);
        refund.Amount.Should().Be(150000m);

        _mockWalletClient.Verify(w => w.RefundSubOrderPaymentAsync(subOrder.Id, It.IsAny<Guid>(), buyerId.ToString(), 150000m, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // Assert 4: Warehouse stock release with PackageReturned reason
        _mockFulfillmentClient.Verify(f => f.ReleaseSubOrderStockAsync(
            order.Id,
            It.Is<List<StockReservationItem>>(items => items.Count == 1 && items[0].SkuId == skuId && items[0].Quantity == 2),
            "PackageReturned",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Returned_WhenCod_ShouldCancelOrder_ReleaseStock_AndStrictlySkipWalletRefund()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Shipped, 150000m, 135000m);

        var skuId = Guid.NewGuid();
        subOrder.Items.Add(new SubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            ProductId = Guid.NewGuid(),
            SkuId = skuId,
            SellerId = sellerId,
            ProductNameSnapshot = "Returned COD Product",
            Quantity = 1,
            UnitPrice = 120000m,
            UnitPriceAfterDiscount = 120000m,
            LineTotal = 120000m
        });
        subOrder.PaymentMethod = PaymentMethod.Cod;
        subOrder.PaidAt = null;
        await _dbContext.SaveChangesAsync();

        _mockFulfillmentClient
            .Setup(f => f.ReleaseSubOrderStockAsync(order.Id, It.IsAny<List<StockReservationItem>>(), "PackageReturned", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateOrderService();

        // Act: Webhook cap nhat trang thai Returned cho don COD
        await service.UpdateSubOrderDeliveryStatusAsync(subOrder.Id, new UpdateDeliveryStatusRequest(
            Status: "Returned",
            DeliveredAt: DateTimeOffset.UtcNow,
            Note: "COD Package returned to sender warehouse"
        ), CancellationToken.None);

        // Assert 1: Status Cancelled
        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Cancelled);

        // Assert 2: OrderCancellation recorded with PackageReturned and InventoryReleased = true
        var cancellation = await _dbContext.OrderCancellations.FirstOrDefaultAsync(c => c.SubOrderId == subOrder.Id);
        cancellation.Should().NotBeNull();
        cancellation!.ReasonCode.Should().Be(CancellationReasonCode.PackageReturned);

        // Assert 3: ZERO wallet refund for COD order
        _mockWalletClient.Verify(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        _mockWalletClient.Verify(w => w.RefundOrderPaymentAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        var refunds = await _dbContext.OrderRefunds.Where(r => r.SubOrderId == subOrder.Id).ToListAsync();
        refunds.Should().BeEmpty();

        // Assert 4: Warehouse stock release with PackageReturned reason must be called
        _mockFulfillmentClient.Verify(f => f.ReleaseSubOrderStockAsync(
            order.Id,
            It.Is<List<StockReservationItem>>(items => items.Count == 1 && items[0].SkuId == skuId && items[0].Quantity == 1),
            "PackageReturned",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelOrder_ShouldSkipWalletRefund_WhenPaymentMethodIsCod()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.PendingPayment, SubOrderStatus.PendingPayment, 150000m, 135000m);
        subOrder.PaymentMethod = PaymentMethod.Cod;
        await _dbContext.SaveChangesAsync();

        var service = CreateOrderService();
        var request = new CancelOrderRequest(CancellationReasonCode.BuyerChangedMind, "Changed mind");

        // Act
        await service.CancelOrderAsync(buyerId, order.Id, request, CancellationToken.None);

        // Assert
        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Cancelled);

        // Verify NO wallet refund was called
        _mockWalletClient.Verify(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        _mockWalletClient.Verify(w => w.RefundOrderPaymentAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        var refunds = await _dbContext.OrderRefunds.Where(r => r.SubOrderId == subOrder.Id).ToListAsync();
        refunds.Should().BeEmpty();
    }

    [Fact]
    public async Task SellerRejectSubOrder_ShouldSkipWalletRefund_WhenPaymentMethodIsCod()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.PendingPayment, SubOrderStatus.Confirmed, 150000m, 135000m);
        subOrder.PaymentMethod = PaymentMethod.Cod;
        await _dbContext.SaveChangesAsync();

        var service = CreateOrderService();
        var request = new SellerRejectOrderRequest(subOrder.Id, CancellationReasonCode.OutOfStock, "Out of stock");

        // Act
        await service.SellerRejectSubOrderAsync(sellerId, order.Id, request, CancellationToken.None);

        // Assert
        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Cancelled);

        // Verify NO wallet refund was called
        _mockWalletClient.Verify(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        _mockWalletClient.Verify(w => w.RefundOrderPaymentAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        var refunds = await _dbContext.OrderRefunds.Where(r => r.SubOrderId == subOrder.Id).ToListAsync();
        refunds.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelOrder_ShouldReleaseVouchersAndDecreasesReservedCount_WhenOrderCancelled()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var (order, subOrder) = await SeedOrderWithSubOrderAsync(buyerId, sellerId, OrderStatus.Paid, SubOrderStatus.Paid, 150000m, 135000m);
        subOrder.PaymentMethod = PaymentMethod.Wallet;
        subOrder.PaidAt = DateTimeOffset.UtcNow;

        var discountCode = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = "SAVE20K",
            Name = "Save 20k",
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 20000m,
            ScopeType = DiscountScopeType.System,
            Status = DiscountStatus.Active,
            ReservedCount = 1,
            UsedCount = 0,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-1),
            EndsAt = DateTimeOffset.UtcNow.AddDays(1)
        };

        var discountUsage = new DiscountCodeUsage
        {
            Id = Guid.NewGuid(),
            DiscountCodeId = discountCode.Id,
            UserId = buyerId,
            OrderId = order.Id,
            SubOrderId = subOrder.Id,
            DiscountAmount = 20000m,
            Status = DiscountUsageStatus.Reserved,
            ReservedAt = DateTimeOffset.UtcNow
        };

        _dbContext.DiscountCodes.Add(discountCode);
        _dbContext.DiscountCodeUsages.Add(discountUsage);
        await _dbContext.SaveChangesAsync();

        var service = CreateOrderService();
        var request = new CancelOrderRequest(CancellationReasonCode.BuyerChangedMind, "Changed mind");

        // Act
        await service.CancelOrderAsync(buyerId, order.Id, request, CancellationToken.None);

        // Assert
        var updatedUsage = await _dbContext.DiscountCodeUsages.FindAsync(discountUsage.Id);
        updatedUsage!.Status.Should().Be(DiscountUsageStatus.Released);
        updatedUsage.ReleasedAt.Should().NotBeNull();

        var updatedCode = await _dbContext.DiscountCodes.FindAsync(discountCode.Id);
        updatedCode!.ReservedCount.Should().Be(0);
    }
}
