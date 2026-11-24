namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.AuctionOrder;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class AuctionOrderExtendedLifecycleTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = FulfillmentMocks.WithConfirmedLocations();
    private readonly Mock<IAuctionClient> _mockAuctionClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<AuctionOrderService>> _mockAuctionLogger = new();
    private readonly AuctionOrderService _auctionOrderService;

    public AuctionOrderExtendedLifecycleTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();

        _auctionOrderService = new AuctionOrderService(
            _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockAuctionClient.Object,
            _mockCatalogClient.Object,
            _mockUserClient.Object,
            _mockAuctionLogger.Object
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetAuctionShipmentTimeline_ReturnsTimelineEventsAndCarrierDetails()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var auctionOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AO-TL-01",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Antique Vase",
            WinningAmount = 800000m,
            RemainingAmount = 800000m,
            SellerReceiveAmount = 760000m,
            PlatformFeeAmount = 40000m,
            PaymentMethod = PaymentMethod.Wallet,
            CarrierCode = "GHN",
            TrackingCode = "GHNAUCTION123",
            Status = SubOrderStatus.Shipping,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            StatusHistories = new List<OrderStatusHistory>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    FromStatus = SubOrderStatus.PendingPayment,
                    ToStatus = SubOrderStatus.Paid,
                    ActorRole = OrderActorRole.Buyer,
                    Reason = "Paid via wallet",
                    CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    FromStatus = SubOrderStatus.Paid,
                    ToStatus = SubOrderStatus.Confirmed,
                    ActorRole = OrderActorRole.Seller,
                    Reason = "Seller accepted order",
                    CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
                }
            }
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _auctionOrderService.GetAuctionShipmentTimelineAsync(buyerId, auctionOrder.Id);

        // Assert
        result.Should().NotBeNull();
        result.SubOrderNo.Should().Be("AO-TL-01");
        result.CarrierCode.Should().Be("GHN");
        result.TrackingCode.Should().Be("GHNAUCTION123");
        result.CurrentStatus.Should().Be("Shipping");
        result.Timeline.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task CancelAuctionOrder_WhenPendingPayment_CancelsWithoutRefund()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var auctionOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AO-CAN-01",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Coin",
            WinningAmount = 300000m,
            RemainingAmount = 300000m,
            SellerReceiveAmount = 285000m,
            PlatformFeeAmount = 15000m,
            PaymentMethod = PaymentMethod.Wallet,
            Status = SubOrderStatus.PendingPayment,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        var request = new CancelAuctionOrderRequest(Reason: "Changed my mind");

        // Act
        var result = await _auctionOrderService.CancelAuctionOrderAsync(buyerId, auctionOrder.Id, request);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Cancelled");
        result.RefundedAmount.Should().Be(0m);

        var updated = await _dbContext.AuctionOrders.FindAsync(auctionOrder.Id);
        updated!.Status.Should().Be(SubOrderStatus.Cancelled);
        updated.CancelledAt.Should().NotBeNull();

        _mockWalletClient.Verify(w => w.RefundOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelAuctionOrder_WhenPaid_RefundsWalletAndSendsCallback()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var auctionOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AO-CAN-02",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Gold Ring",
            WinningAmount = 1200000m,
            RemainingAmount = 1200000m,
            ShippingFeeAmount = 30000m,
            TotalAmount = 1230000m,
            SellerReceiveAmount = 1140000m,
            PlatformFeeAmount = 60000m,
            PaymentMethod = PaymentMethod.Wallet,
            Status = SubOrderStatus.Paid,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        _mockWalletClient
            .Setup(w => w.RefundOrderPaymentAsync(auctionOrder.Id, buyerId.ToString(), 1230000m, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new CancelAuctionOrderRequest(Reason: "Accidental purchase");

        // Act
        var result = await _auctionOrderService.CancelAuctionOrderAsync(buyerId, auctionOrder.Id, request);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Cancelled");
        result.RefundedAmount.Should().Be(1230000m);

        var updated = await _dbContext.AuctionOrders.FindAsync(auctionOrder.Id);
        updated!.Status.Should().Be(SubOrderStatus.Cancelled);

        _mockWalletClient.Verify(w => w.RefundOrderPaymentAsync(auctionOrder.Id, buyerId.ToString(), 1230000m, It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _mockAuctionClient.Verify(a => a.NotifyPaymentStatusAsync(auctionOrder.AuctionId, It.IsAny<AuctionPaymentStatusCallbackRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAuctionOrder_WhenAlreadyAccepted_ThrowsCannotBeCancelled_400()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var auctionOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AO-CAN-03",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Gold Ring",
            WinningAmount = 1200000m,
            RemainingAmount = 1200000m,
            SellerReceiveAmount = 1140000m,
            PlatformFeeAmount = 60000m,
            PaymentMethod = PaymentMethod.Wallet,
            Status = SubOrderStatus.Confirmed,
            SellerAcceptedAt = DateTimeOffset.UtcNow.AddMinutes(-5), // Seller already accepted!
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        var request = new CancelAuctionOrderRequest(Reason: "Try cancel");

        // Act
        var act = async () => await _auctionOrderService.CancelAuctionOrderAsync(buyerId, auctionOrder.Id, request);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("AUCTION_CANNOT_BE_CANCELLED");
        ex.Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task RetryAuctionShipment_WhenConfirmed_CreatesShipmentAndUpdatesTracking()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var auctionOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AO-RETRY-01",
            AuctionId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Vintage Watch",
            WinningAmount = 900000m,
            RemainingAmount = 900000m,
            SellerReceiveAmount = 855000m,
            PlatformFeeAmount = 45000m,
            PaymentMethod = PaymentMethod.Wallet,
            WeightGramsSnapshot = 600,
            Status = SubOrderStatus.Confirmed,
            ShippingAddress = "{\"fullName\":\"John Doe\",\"phoneNumber\":\"0909123456\",\"addressLine\":\"123 Main St\",\"provinceName\":\"HCM\",\"districtName\":\"District 1\",\"wardName\":\"Ward 1\",\"ghnDistrictId\":1442,\"ghnWardCode\":\"20101\"}",
            ContactPhone = "0909123456",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        _mockFulfillmentClient
            .Setup(f => f.CreateShipmentAsync(It.IsAny<ShipmentCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentCreationResult(
                TrackingCode: "GHNRETRY999",
                CarrierCode: "GHN",
                LabelPrintUrl: "https://ghn.vn/print/999"
            ));

        // Act
        var result = await _auctionOrderService.RetryAuctionShipmentAsync(sellerId, auctionOrder.Id);

        // Assert
        result.Should().NotBeNull();
        result.TrackingCode.Should().Be("GHNRETRY999");
        result.CarrierCode.Should().Be("GHN");
        result.LabelPrintUrl.Should().Be("https://ghn.vn/print/999");

        var updated = await _dbContext.AuctionOrders.FindAsync(auctionOrder.Id);
        updated!.Status.Should().Be(SubOrderStatus.Shipping);
        updated.TrackingCode.Should().Be("GHNRETRY999");
        updated.ShippedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RetryAuctionShipment_WhenAlreadyHasTracking_ThrowsShipmentAlreadyExists_400()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var auctionOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AO-RETRY-02",
            AuctionId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Vintage Watch",
            WinningAmount = 900000m,
            RemainingAmount = 900000m,
            SellerReceiveAmount = 855000m,
            PlatformFeeAmount = 45000m,
            PaymentMethod = PaymentMethod.Wallet,
            Status = SubOrderStatus.Confirmed,
            TrackingCode = "EXISTING_TRACKING_123", // Already has tracking!
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        // Act
        var act = async () => await _auctionOrderService.RetryAuctionShipmentAsync(sellerId, auctionOrder.Id);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("SHIPMENT_ALREADY_EXISTS");
        ex.Which.StatusCode.Should().Be(400);
    }
}
