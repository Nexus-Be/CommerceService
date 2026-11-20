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
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class OrderDeliveryStatusTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<OrderService>> _mockLogger = new();
    private readonly IDistributedCache _cache;
    private readonly OrderService _orderService;

    public OrderDeliveryStatusTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        _mockUserClient
            .Setup(u => u.AdjustReputationAsync(It.IsAny<ReputationAdjustRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReputationAdjustResponse(Guid.NewGuid(), "Buyer", false, false, 80, 75, -5, "Standard"));

        _mockWalletClient
            .Setup(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.ReleaseSubOrderStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _orderService = new OrderService(
            _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockUserClient.Object,
            _cache,
            _mockLogger.Object);
    }

    [Fact]
    public async Task UpdateSubOrderDeliveryStatusAsync_StatusReturning_TransitionsToReturningStatusAndLogsHistory()
    {
        // Arrange
        var subOrderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            OrderId = Guid.NewGuid(),
            SubOrderNo = "SO-RETURNING-001",
            BuyerId = buyerId,
            SellerId = sellerId,
            ContactPhone = "0901234567",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(1),
            ItemsAmount = 100000m,
            TotalAmount = 130000m,
            SellerReceiveAmount = 95000m,
            PlatformFeeAmount = 5000m,
            Status = SubOrderStatus.Shipped,
            PaymentMethod = PaymentMethod.Wallet,
            PaidAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateDeliveryStatusRequest(
            Status: "Returning",
            Note: "Package returning to seller after 3 failed delivery attempts."
        );

        // Act
        await _orderService.UpdateSubOrderDeliveryStatusAsync(subOrderId, request);

        // Assert
        var updated = await _dbContext.SubOrders.FindAsync(subOrderId);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(SubOrderStatus.Returning);

        var history = await _dbContext.OrderStatusHistories
            .FirstOrDefaultAsync(h => h.SubOrderId == subOrderId && h.ToStatus == SubOrderStatus.Returning);
        history.Should().NotBeNull();
        history!.Reason.Should().Contain("Returning");
    }

    [Fact]
    public async Task UpdateSubOrderDeliveryStatusAsync_StatusReturned_DeductsBuyerReputationAndCancelsOrder()
    {
        // Arrange
        var subOrderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            OrderId = Guid.NewGuid(),
            SubOrderNo = "SO-RETURNED-001",
            BuyerId = buyerId,
            SellerId = sellerId,
            ContactPhone = "0901234567",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(1),
            ItemsAmount = 200000m,
            TotalAmount = 230000m,
            SellerReceiveAmount = 190000m,
            PlatformFeeAmount = 10000m,
            Status = SubOrderStatus.Returning,
            PaymentMethod = PaymentMethod.Wallet,
            PaidAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateDeliveryStatusRequest(
            Status: "Returned",
            Note: "Package returned to merchant facility."
        );

        // Act
        await _orderService.UpdateSubOrderDeliveryStatusAsync(subOrderId, request);

        // Assert
        var updated = await _dbContext.SubOrders.FindAsync(subOrderId);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(SubOrderStatus.Cancelled);

        // Verify reputation penalty: -5 points to Buyer
        _mockUserClient.Verify(u => u.AdjustReputationAsync(
            It.Is<ReputationAdjustRequest>(r =>
                r.UserId == buyerId &&
                r.TargetRole == "Buyer" &&
                r.PartnerUserId == sellerId &&
                r.ScoreDelta == -5 &&
                r.Reason == "PackageReturnedDeliveryFailed"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateAuctionOrderDeliveryStatusAsync_DeliveryFailed_DoesNotCancelAuctionOrder()
    {
        // Arrange
        var auctionOrderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = auctionOrderId,
            OrderNo = "AO-FAIL-001",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Collectable Item",
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(1),
            WinningAmount = 1000000m,
            DepositAmount = 100000m,
            RemainingAmount = 900000m,
            PlatformFeeAmount = 50000m,
            SellerReceiveAmount = 950000m,
            Status = SubOrderStatus.Shipped,
            PaidAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateDeliveryStatusRequest(
            Status: "DeliveryFailed",
            Note: "Recipient unavailable on first attempt."
        );

        // Act
        await _orderService.UpdateSubOrderDeliveryStatusAsync(auctionOrderId, request);

        // Assert
        var updated = await _dbContext.AuctionOrders.FindAsync(auctionOrderId);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(SubOrderStatus.Shipped); // MUST NOT be cancelled
    }

    [Fact]
    public async Task UpdateAuctionOrderDeliveryStatusAsync_Returning_TransitionsToReturning()
    {
        // Arrange
        var auctionOrderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = auctionOrderId,
            OrderNo = "AO-RETURNING-001",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Collectable Item",
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(1),
            WinningAmount = 1000000m,
            DepositAmount = 100000m,
            RemainingAmount = 900000m,
            PlatformFeeAmount = 50000m,
            SellerReceiveAmount = 950000m,
            Status = SubOrderStatus.Shipped,
            PaidAt = DateTimeOffset.UtcNow.AddDays(-2)
        };

        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateDeliveryStatusRequest(
            Status: "Returning",
            Note: "Returning package to sender."
        );

        // Act
        await _orderService.UpdateSubOrderDeliveryStatusAsync(auctionOrderId, request);

        // Assert
        var updated = await _dbContext.AuctionOrders.FindAsync(auctionOrderId);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(SubOrderStatus.Returning);
    }

    [Fact]
    public async Task UpdateAuctionOrderDeliveryStatusAsync_Returned_CancelsAuctionOrder()
    {
        // Arrange
        var auctionOrderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = auctionOrderId,
            OrderNo = "AO-RETURNED-001",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Collectable Item",
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(1),
            WinningAmount = 1000000m,
            DepositAmount = 100000m,
            RemainingAmount = 900000m,
            PlatformFeeAmount = 50000m,
            SellerReceiveAmount = 950000m,
            Status = SubOrderStatus.Returning,
            PaidAt = DateTimeOffset.UtcNow.AddDays(-3)
        };

        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateDeliveryStatusRequest(
            Status: "Returned",
            Note: "Package returned to warehouse."
        );

        // Act
        await _orderService.UpdateSubOrderDeliveryStatusAsync(auctionOrderId, request);

        // Assert
        var updated = await _dbContext.AuctionOrders.FindAsync(auctionOrderId);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(SubOrderStatus.Cancelled);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
