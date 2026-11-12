namespace Commerce.UnitTests.Application;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.AuctionOrder;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;

public class AuctionOrderServiceTests
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _walletClientMock = new();
    private readonly Mock<IFulfillmentClient> _fulfillmentClientMock = new();
    private readonly Mock<IAuctionClient> _auctionClientMock = new();
    private readonly Mock<ICatalogClient> _catalogClientMock = new();
    private readonly Mock<ILogger<AuctionOrderService>> _loggerMock = new();
    private readonly AuctionOrderService _service;

    public AuctionOrderServiceTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _service = new AuctionOrderService(
            _dbContext,
            _walletClientMock.Object,
            _fulfillmentClientMock.Object,
            _auctionClientMock.Object,
            _catalogClientMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ReceiveSettlementAsync_ValidRequest_CreatesAuctionOrderWithAOOrderNoAndPendingPaymentStatus()
    {
        // Arrange
        var auctionId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var winningBidId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var deadline = DateTimeOffset.UtcNow.AddHours(24);

        var request = new AuctionSettlementRequest(
            AuctionId: auctionId,
            WinnerId: winnerId,
            SellerId: sellerId,
            WinningBidId: winningBidId,
            ProductId: productId,
            SkuId: null,
            ProductName: "Vintage Mechanical Watch",
            ProductImageUrl: "https://example.com/watch.png",
            SkuAttributes: "{\"color\":\"gold\"}",
            WinningAmount: 2000000m,
            PaymentDeadlineAt: deadline
        );

        // Act
        var result = await _service.ReceiveSettlementAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.IsNewlyCreated.Should().BeTrue();
        result.Response.AuctionId.Should().Be(auctionId);
        result.Response.WinnerId.Should().Be(winnerId);
        result.Response.OrderNo.Should().StartWith("AO-");
        result.Response.Status.Should().Be("PendingPayment");
        result.Response.PaymentDeadlineAt.Should().Be(deadline);

        var savedOrder = await _dbContext.AuctionOrders.FindAsync(result.Response.OrderId);
        savedOrder.Should().NotBeNull();
        savedOrder!.WinningAmount.Should().Be(2000000m);
        savedOrder.PlatformFeeAmount.Should().Be(100000m); // 5% of 2,000,000
        savedOrder.SellerReceiveAmount.Should().Be(1900000m); // 2,000,000 - 100,000
        savedOrder.TotalAmount.Should().BeNull(); // awaiting shipping quote
        savedOrder.Status.Should().Be(SubOrderStatus.PendingPayment);
    }

    [Fact]
    public async Task ReceiveSettlementAsync_DuplicateAuctionIdAndWinnerId_ReturnsExistingOrderWithoutDuplicates()
    {
        // Arrange
        var auctionId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();
        var request = new AuctionSettlementRequest(
            AuctionId: auctionId,
            WinnerId: winnerId,
            SellerId: Guid.NewGuid(),
            WinningBidId: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            SkuId: null,
            ProductName: "Vintage Mechanical Watch",
            ProductImageUrl: null,
            SkuAttributes: "{}",
            WinningAmount: 1500000m,
            PaymentDeadlineAt: DateTimeOffset.UtcNow.AddHours(24)
        );

        // Act 1: Initial creation
        var firstResult = await _service.ReceiveSettlementAsync(request);
        firstResult.IsNewlyCreated.Should().BeTrue();

        // Act 2: Duplicate call
        var secondResult = await _service.ReceiveSettlementAsync(request);

        // Assert
        secondResult.IsNewlyCreated.Should().BeFalse();
        secondResult.Response.OrderId.Should().Be(firstResult.Response.OrderId);
        secondResult.Response.OrderNo.Should().Be(firstResult.Response.OrderNo);
    }

    [Fact]
    public async Task ReceiveSettlementAsync_SameAuctionIdWithDifferentWinner_WhenOriginalCancelled_CreatesNewOrder()
    {
        // Arrange: Original order defaulted and was cancelled
        var auctionId = Guid.NewGuid();
        var originalWinnerId = Guid.NewGuid();
        var runnerUpWinnerId = Guid.NewGuid();

        var originalOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AO-20261111-00001",
            AuctionId = auctionId,
            BuyerId = originalWinnerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Vintage Watch",
            WinningAmount = 2000000m,
            SellerReceiveAmount = 1900000m,
            PlatformFeeAmount = 100000m,
            Status = SubOrderStatus.Cancelled,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(-1),
            CancelledAt = DateTimeOffset.UtcNow
        };
        _dbContext.AuctionOrders.Add(originalOrder);
        await _dbContext.SaveChangesAsync();

        var runnerUpRequest = new AuctionSettlementRequest(
            AuctionId: auctionId,
            WinnerId: runnerUpWinnerId,
            SellerId: originalOrder.SellerId,
            WinningBidId: Guid.NewGuid(),
            ProductId: originalOrder.ProductId,
            SkuId: null,
            ProductName: "Vintage Watch",
            ProductImageUrl: null,
            SkuAttributes: "{}",
            WinningAmount: 1900000m,
            PaymentDeadlineAt: DateTimeOffset.UtcNow.AddHours(24)
        );

        // Act
        var result = await _service.ReceiveSettlementAsync(runnerUpRequest);

        // Assert
        result.IsNewlyCreated.Should().BeTrue();
        result.Response.AuctionId.Should().Be(auctionId);
        result.Response.WinnerId.Should().Be(runnerUpWinnerId);
        result.Response.OrderId.Should().NotBe(originalOrder.Id);
    }

    [Fact]
    public async Task ReceiveSettlementAsync_SameAuctionIdWithDifferentWinner_WhenOriginalActive_ThrowsConflictException()
    {
        // Arrange: Original order is still active (PendingPayment)
        var auctionId = Guid.NewGuid();
        var originalWinnerId = Guid.NewGuid();
        var anotherWinnerId = Guid.NewGuid();

        var activeOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AO-20261111-00001",
            AuctionId = auctionId,
            BuyerId = originalWinnerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Vintage Watch",
            WinningAmount = 2000000m,
            SellerReceiveAmount = 1900000m,
            PlatformFeeAmount = 100000m,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(12)
        };
        _dbContext.AuctionOrders.Add(activeOrder);
        await _dbContext.SaveChangesAsync();

        var conflictingRequest = new AuctionSettlementRequest(
            AuctionId: auctionId,
            WinnerId: anotherWinnerId,
            SellerId: activeOrder.SellerId,
            WinningBidId: Guid.NewGuid(),
            ProductId: activeOrder.ProductId,
            SkuId: null,
            ProductName: "Vintage Watch",
            ProductImageUrl: null,
            SkuAttributes: "{}",
            WinningAmount: 1900000m,
            PaymentDeadlineAt: DateTimeOffset.UtcNow.AddHours(24)
        );

        // Act & Assert
        var act = async () => await _service.ReceiveSettlementAsync(conflictingRequest);
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "AUCTION_ALREADY_SETTLED" && e.StatusCode == 409);
    }

    [Fact]
    public async Task SubmitShippingAsync_ValidBuyerWithinDeadline_CalculatesFeeAndFinalizesTotal()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(20)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        _fulfillmentClientMock
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), default))
            .ReturnsAsync(45000m);

        var request = new SubmitAuctionShippingRequest(
            ShippingAddress: new ShippingAddressDto(
                FullName: "Nguyen Van A",
                PhoneNumber: "0901234567",
                AddressLine: "123 Le Loi",
                ProvinceName: "Ho Chi Minh",
                DistrictName: "District 1",
                WardName: "Ben Nghe",
                GhnDistrictId: 1442,
                GhnWardCode: "20101"
            ),
            ContactPhone: "0901234567"
        );

        // Act
        var response = await _service.SubmitShippingAsync(orderId, buyerId, request);

        // Assert
        response.Should().NotBeNull();
        response.ShippingFeeAmount.Should().Be(45000m);
        response.TotalAmount.Should().Be(5045000m); // 5,000,000 + 45,000
        response.ContactPhone.Should().Be("0901234567");

        var updatedOrder = await _dbContext.AuctionOrders.FindAsync(orderId);
        updatedOrder!.TotalAmount.Should().Be(5045000m);
        updatedOrder.ShippingFeeAmount.Should().Be(45000m);
    }

    [Fact]
    public async Task SubmitShippingAsync_WhenDeadlineExpired_ThrowsPaymentDeadlineExpiredException()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var expiredOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _dbContext.AuctionOrders.Add(expiredOrder);
        await _dbContext.SaveChangesAsync();

        var request = new SubmitAuctionShippingRequest(
            ShippingAddress: new ShippingAddressDto(
                FullName: "Nguyen Van A",
                PhoneNumber: "0901234567",
                AddressLine: "123 Le Loi",
                ProvinceName: "Ho Chi Minh",
                DistrictName: "District 1",
                WardName: "Ben Nghe",
                GhnDistrictId: 1442,
                GhnWardCode: "20101"
            ),
            ContactPhone: "0901234567"
        );

        // Act & Assert
        var act = async () => await _service.SubmitShippingAsync(orderId, buyerId, request);
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "PAYMENT_DEADLINE_EXPIRED" && e.StatusCode == 400);
    }

    [Fact]
    public async Task SubmitShippingAsync_WhenNotBuyer_ThrowsForbiddenException()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(10)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        var request = new SubmitAuctionShippingRequest(
            ShippingAddress: new ShippingAddressDto(
                FullName: "Nguyen Van A",
                PhoneNumber: "0901234567",
                AddressLine: "123 Le Loi",
                ProvinceName: "Ho Chi Minh",
                DistrictName: "District 1",
                WardName: "Ben Nghe",
                GhnDistrictId: 1442,
                GhnWardCode: "20101"
            ),
            ContactPhone: "0901234567"
        );

        // Act & Assert
        var act = async () => await _service.SubmitShippingAsync(orderId, strangerId, request);
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "FORBIDDEN" && e.StatusCode == 403);
    }

    [Fact]
    public async Task PayAuctionOrderAsync_ValidOrderWithinDeadline_DebitsWalletSetsPaidAndDispatchesPaidCallback()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var auctionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = auctionId,
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            ShippingAddress = "{\"fullName\":\"Nguyen Van A\"}",
            ShippingFeeAmount = 50000m,
            TotalAmount = 5050000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(12)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        _walletClientMock
            .Setup(w => w.PayOrderAsync(orderId, buyerId, 5050000m, null, default))
            .ReturnsAsync(WalletPaymentResult.Success(transactionId.ToString()));

        _auctionClientMock
            .Setup(a => a.NotifyPaymentStatusAsync(auctionId, It.Is<AuctionPaymentStatusCallbackRequest>(r =>
                r.OrderId == orderId &&
                r.WinnerId == buyerId &&
                r.Status == "Paid" &&
                r.Amount == 5000000m &&
                r.PaidAt.HasValue &&
                r.EventId.HasValue), default))
            .ReturnsAsync(true);

        // Act
        var response = await _service.PayAuctionOrderAsync(orderId, buyerId);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be("Paid");
        response.WalletTransactionId.Should().Be(transactionId);
        response.SellerRespondDeadlineAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(24), TimeSpan.FromSeconds(5));

        var updatedOrder = await _dbContext.AuctionOrders.FindAsync(orderId);
        updatedOrder!.Status.Should().Be(SubOrderStatus.Paid);
        updatedOrder.PaidAt.Should().NotBeNull();
        updatedOrder.SellerRespondDeadlineAt.Should().NotBeNull();

        _auctionClientMock.Verify(a => a.NotifyPaymentStatusAsync(auctionId, It.IsAny<AuctionPaymentStatusCallbackRequest>(), default), Times.Once);
    }

    [Fact]
    public async Task PayAuctionOrderAsync_WithoutShippingAddressOrTotal_ThrowsShippingAddressRequiredException()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            ShippingAddress = null,
            ShippingFeeAmount = null,
            TotalAmount = null, // Not quoted yet
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(12)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.PayAuctionOrderAsync(orderId, buyerId);
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "SHIPPING_ADDRESS_REQUIRED" && e.StatusCode == 400);
    }

    [Fact]
    public async Task PayAuctionOrderAsync_WhenDeadlineExpired_ThrowsPaymentDeadlineExpiredException()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            ShippingAddress = "{}",
            ShippingFeeAmount = 30000m,
            TotalAmount = 5030000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.PayAuctionOrderAsync(orderId, buyerId);
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "PAYMENT_DEADLINE_EXPIRED" && e.StatusCode == 400);
    }

    [Fact]
    public async Task SellerAcceptAsync_ValidSellerWithinDeadline_SetsConfirmedCreatesShipmentAndDispatchesSellerAcceptedCallback()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var auctionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = auctionId,
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            ShippingAddress = "{}",
            ShippingFeeAmount = 50000m,
            TotalAmount = 5050000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(20),
            PaidAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            SellerRespondDeadlineAt = DateTimeOffset.UtcNow.AddHours(23)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        _fulfillmentClientMock
            .Setup(f => f.CreateShipmentAsync(orderId, default))
            .ReturnsAsync(new ShipmentCreationResult("GHN-TRACK-001", "GHN", "https://print.ghn.vn/label"));

        _auctionClientMock
            .Setup(a => a.NotifyPaymentStatusAsync(auctionId, It.Is<AuctionPaymentStatusCallbackRequest>(r =>
                r.OrderId == orderId &&
                r.WinnerId == buyerId &&
                r.Status == "SellerAccepted" &&
                r.EventId.HasValue), default))
            .ReturnsAsync(true);

        // Act
        var response = await _service.SellerAcceptAsync(orderId, sellerId, new SellerAcceptAuctionOrderRequest("Accepted quickly"));

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be("Confirmed");
        response.Action.Should().Be("Accepted");
        response.TrackingCode.Should().Be("GHN-TRACK-001");

        var updatedOrder = await _dbContext.AuctionOrders.FindAsync(orderId);
        updatedOrder!.Status.Should().Be(SubOrderStatus.Confirmed);
        updatedOrder.SellerAcceptedAt.Should().NotBeNull();

        _auctionClientMock.Verify(a => a.NotifyPaymentStatusAsync(auctionId, It.IsAny<AuctionPaymentStatusCallbackRequest>(), default), Times.Once);
    }

    [Fact]
    public async Task SellerAcceptAsync_WhenDeadlineExpired_ThrowsSellerRespondTimeoutException()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(10),
            PaidAt = DateTimeOffset.UtcNow.AddHours(-25),
            SellerRespondDeadlineAt = DateTimeOffset.UtcNow.AddHours(-1) // Expired
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.SellerAcceptAsync(orderId, sellerId, new SellerAcceptAuctionOrderRequest());
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "SELLER_RESPOND_TIMEOUT" && e.StatusCode == 400);
    }

    [Fact]
    public async Task SellerAcceptAsync_WhenNotSeller_ThrowsForbiddenException()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(10),
            PaidAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            SellerRespondDeadlineAt = DateTimeOffset.UtcNow.AddHours(23)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.SellerAcceptAsync(orderId, strangerId, new SellerAcceptAuctionOrderRequest());
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "FORBIDDEN" && e.StatusCode == 403);
    }

    [Fact]
    public async Task SellerRejectAsync_ValidSellerPaidOrder_RefundsBuyerCancelsOrderAndDispatchesSellerRejectedCallback()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var auctionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = auctionId,
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            ShippingAddress = "{}",
            ShippingFeeAmount = 50000m,
            TotalAmount = 5050000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(20),
            PaidAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            SellerRespondDeadlineAt = DateTimeOffset.UtcNow.AddHours(23)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        _walletClientMock
            .Setup(w => w.RefundOrderPaymentAsync(orderId, buyerId.ToString(), 5050000m, It.IsAny<string>(), null, default))
            .ReturnsAsync(true);

        _auctionClientMock
            .Setup(a => a.NotifyPaymentStatusAsync(auctionId, It.Is<AuctionPaymentStatusCallbackRequest>(r =>
                r.OrderId == orderId &&
                r.WinnerId == buyerId &&
                r.Status == "SellerRejected" &&
                r.EventId.HasValue), default))
            .ReturnsAsync(true);

        var request = new SellerRejectAuctionOrderRequest(ReasonNote: "Item damaged during storage inspection.");

        // Act
        var response = await _service.SellerRejectAsync(orderId, sellerId, request);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be("Cancelled");
        response.Action.Should().Be("Rejected");
        response.RefundAmount.Should().Be(5050000m);

        var updatedOrder = await _dbContext.AuctionOrders.FindAsync(orderId);
        updatedOrder!.Status.Should().Be(SubOrderStatus.Cancelled);
        updatedOrder.CancelledAt.Should().NotBeNull();

        _walletClientMock.Verify(w => w.RefundOrderPaymentAsync(orderId, buyerId.ToString(), 5050000m, It.IsAny<string>(), null, default), Times.Once);
        _auctionClientMock.Verify(a => a.NotifyPaymentStatusAsync(auctionId, It.IsAny<AuctionPaymentStatusCallbackRequest>(), default), Times.Once);
    }

    [Fact]
    public async Task SellerRejectAsync_WhenAuctionClientFails_DoesNotRollbackRefundAndSucceeds()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var auctionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = auctionId,
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            ShippingAddress = "{}",
            ShippingFeeAmount = 50000m,
            TotalAmount = 5050000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(20),
            PaidAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            SellerRespondDeadlineAt = DateTimeOffset.UtcNow.AddHours(23)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        _walletClientMock
            .Setup(w => w.RefundOrderPaymentAsync(orderId, buyerId.ToString(), 5050000m, It.IsAny<string>(), null, default))
            .ReturnsAsync(true);

        // Fail-safe: Auction callback fails due to network outage
        _auctionClientMock
            .Setup(a => a.NotifyPaymentStatusAsync(auctionId, It.IsAny<AuctionPaymentStatusCallbackRequest>(), default))
            .ReturnsAsync(false);

        var request = new SellerRejectAuctionOrderRequest(ReasonNote: "Item damaged.");

        // Act
        var response = await _service.SellerRejectAsync(orderId, sellerId, request);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be("Cancelled");

        var updatedOrder = await _dbContext.AuctionOrders.FindAsync(orderId);
        updatedOrder!.Status.Should().Be(SubOrderStatus.Cancelled);
    }

    [Fact]
    public async Task SellerRejectAsync_WhenOrderAlreadyShipped_ThrowsInvalidStatusException()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.Shipped, // Already shipped
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(10)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        var request = new SellerRejectAuctionOrderRequest(ReasonNote: "Cannot fulfill.");

        // Act & Assert
        var act = async () => await _service.SellerRejectAsync(orderId, sellerId, request);
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "INVALID_STATUS" && e.StatusCode == 400);
    }

    [Fact]
    public async Task ConfirmReceiptAsync_ValidBuyerDeliveredOrder_CompletesOrderAndSettlesSellerEscrow()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            TotalAmount = 5050000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(-2),
            PaidAt = DateTimeOffset.UtcNow.AddDays(-2),
            DeliveredAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        _walletClientMock
            .Setup(w => w.SettleSellerSubOrderAsync(orderId, sellerId, 4750000m, null, default))
            .ReturnsAsync(true);

        // Act
        var response = await _service.ConfirmReceiptAsync(orderId, buyerId);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be("Completed");

        var updatedOrder = await _dbContext.AuctionOrders.FindAsync(orderId);
        updatedOrder!.Status.Should().Be(SubOrderStatus.Completed);
        updatedOrder.CompletedAt.Should().NotBeNull();

        _walletClientMock.Verify(w => w.SettleSellerSubOrderAsync(orderId, sellerId, 4750000m, null, default), Times.Once);

        _catalogClientMock.Verify(c => c.SyncPurchaseRecordAsync(
            It.Is<CatalogPurchaseSyncRequest>(r => r.BuyerId == buyerId && r.OrderId == orderId && r.ProductId == auctionOrder.ProductId),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmReceiptAsync_WhenCatalogSyncThrowsException_StillCompletesOrderAndSettlesEscrow()
    {
        // Arrange
        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00002",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Sculpture",
            WinningAmount = 1000000m,
            TotalAmount = 1030000m,
            SellerReceiveAmount = 950000m,
            PlatformFeeAmount = 50000m,
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(-1),
            PaidAt = DateTimeOffset.UtcNow.AddDays(-1),
            DeliveredAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        _walletClientMock
            .Setup(w => w.SettleSellerSubOrderAsync(orderId, sellerId, 950000m, null, default))
            .ReturnsAsync(true);

        _catalogClientMock
            .Setup(c => c.SyncPurchaseRecordAsync(It.IsAny<CatalogPurchaseSyncRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("Catalog connection timeout"));

        // Act
        var response = await _service.ConfirmReceiptAsync(orderId, buyerId);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be("Completed");

        var updatedOrder = await _dbContext.AuctionOrders.FindAsync(orderId);
        updatedOrder!.Status.Should().Be(SubOrderStatus.Completed);
        updatedOrder.CompletedAt.Should().NotBeNull();

        _walletClientMock.Verify(w => w.SettleSellerSubOrderAsync(orderId, sellerId, 950000m, null, default), Times.Once);
    }

    [Fact]
    public async Task ConfirmReceiptAsync_WhenStatusNotDeliveredOrShipped_ThrowsInvalidStatusException()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(10)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.ConfirmReceiptAsync(orderId, buyerId);
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "INVALID_STATUS" && e.StatusCode == 400);
    }

    [Fact]
    public async Task ConfirmReceiptAsync_WhenNotBuyer_ThrowsForbiddenException()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        var act = async () => await _service.ConfirmReceiptAsync(orderId, strangerId);
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "FORBIDDEN" && e.StatusCode == 403);
    }

    [Fact]
    public async Task GetOrderDetailAsync_WhenAuthorized_ReturnsDetailResponse()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Painting",
            WinningAmount = 5000000m,
            SellerReceiveAmount = 4750000m,
            PlatformFeeAmount = 250000m,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(10)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        // Act
        var response = await _service.GetOrderDetailAsync(orderId, buyerId);

        // Assert
        response.Should().NotBeNull();
        response.Id.Should().Be(orderId);
        response.ProductName.Should().Be("Rare Painting");
        response.WinningAmount.Should().Be(5000000m);
    }

    [Fact]
    public async Task ListSellerOrdersAsync_ReturnsPaginatedList()
    {
        // Arrange
        var sellerId = Guid.NewGuid();

        for (int i = 0; i < 3; i++)
        {
            _dbContext.AuctionOrders.Add(new AuctionOrder
            {
                Id = Guid.NewGuid(),
                OrderNo = $"AO-20261111-{i:D5}",
                AuctionId = Guid.NewGuid(),
                BuyerId = Guid.NewGuid(),
                SellerId = sellerId,
                WinningBidId = Guid.NewGuid(),
                ProductId = Guid.NewGuid(),
                ProductNameSnapshot = $"Product {i}",
                WinningAmount = 1000000m * (i + 1),
                SellerReceiveAmount = 950000m * (i + 1),
                PlatformFeeAmount = 50000m * (i + 1),
                Status = SubOrderStatus.PendingPayment,
                PaymentMethod = PaymentMethod.Wallet,
                PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(10)
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var response = await _service.ListSellerOrdersAsync(sellerId, 1, 2);

        // Assert
        response.Should().NotBeNull();
        response.TotalCount.Should().Be(3);
        response.TotalPages.Should().Be(2);
        response.Items.Should().HaveCount(2);
        response.HasNextPage.Should().BeTrue();
    }
}
