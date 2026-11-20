namespace Commerce.UnitTests.Api;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Controllers;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public class DisputeRefundEndpointTests : IDisposable
{
    private const string ValidServiceKey = "internal-dispute-test-secret-key-32chars";

    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<OrderService>> _mockOrderLogger = new();
    private readonly IDistributedCache _cache;
    private readonly OrderService _orderService;
    private readonly IConfiguration _configuration;

    public DisputeRefundEndpointTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        _orderService = new OrderService(
            _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockUserClient.Object,
            _cache,
            _mockOrderLogger.Object);

        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InternalSecurity:ServiceKey"] = ValidServiceKey
        });
        _configuration = configBuilder.Build();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private OrdersController CreateController(IOrderService orderService, string? headerKey = null)
    {
        var controller = new OrdersController(orderService);
        var httpContext = new DefaultHttpContext();
        if (headerKey != null)
        {
            httpContext.Request.Headers["X-Internal-Service-Key"] = headerKey;
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    private SubOrder SeedSubOrder(SubOrderStatus status, decimal totalAmount = 500_000m)
    {
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = $"SUB-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            ContactPhone = "0901234567",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaymentMethod = PaymentMethod.Wallet,
            Status = status,
            ItemsAmount = totalAmount,
            TotalAmount = totalAmount,
            SellerReceiveAmount = totalAmount,
            PlatformFeeAmount = 0m,
            PaidAt = DateTimeOffset.UtcNow.AddDays(-2),
            DeliveredAt = status == SubOrderStatus.Delivered || status == SubOrderStatus.Completed ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            CompletedAt = status == SubOrderStatus.Completed ? DateTimeOffset.UtcNow.AddHours(-1) : null
        };

        _dbContext.SubOrders.Add(subOrder);
        _dbContext.SaveChanges();

        return subOrder;
    }

    #region Controller Security Tests

    [Fact]
    public async Task DisputeRefundSubOrder_WhenHeaderMissing_Returns401Unauthorized()
    {
        // Arrange
        var mockService = new Mock<IOrderService>();
        var controller = CreateController(mockService.Object, headerKey: null);
        var subOrderId = Guid.NewGuid();
        var request = new DisputeRefundRequest(Guid.NewGuid(), 100_000m, "Damaged item");

        // Act
        var result = await controller.DisputeRefundSubOrder(subOrderId, request, _configuration, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        mockService.Verify(s => s.DisputeRefundSubOrderAsync(It.IsAny<Guid>(), It.IsAny<DisputeRefundRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisputeRefundSubOrder_WhenHeaderInvalid_Returns401Unauthorized()
    {
        // Arrange
        var mockService = new Mock<IOrderService>();
        var controller = CreateController(mockService.Object, headerKey: "invalid-wrong-key");
        var subOrderId = Guid.NewGuid();
        var request = new DisputeRefundRequest(Guid.NewGuid(), 100_000m, "Damaged item");

        // Act
        var result = await controller.DisputeRefundSubOrder(subOrderId, request, _configuration, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        mockService.Verify(s => s.DisputeRefundSubOrderAsync(It.IsAny<Guid>(), It.IsAny<DisputeRefundRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisputeRefundSubOrder_WhenKeyValid_DelegatesToServiceAndReturns200()
    {
        // Arrange
        var subOrderId = Guid.NewGuid();
        var disputeId = Guid.NewGuid();
        var request = new DisputeRefundRequest(disputeId, 250_000m, "Damaged item", "Support approved");
        var expectedResponse = new DisputeRefundResponse(subOrderId, Guid.NewGuid(), 250_000m, "Completed", DateTimeOffset.UtcNow);

        var mockService = new Mock<IOrderService>();
        mockService.Setup(s => s.DisputeRefundSubOrderAsync(subOrderId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var controller = CreateController(mockService.Object, headerKey: ValidServiceKey);

        // Act
        var result = await controller.DisputeRefundSubOrder(subOrderId, request, _configuration, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().BeEquivalentTo(expectedResponse);
    }

    #endregion

    #region Service Logic Tests

    [Fact]
    public async Task DisputeRefundSubOrderAsync_WhenSubOrderNotFound_ThrowsEntityNotFoundException_404()
    {
        // Arrange
        var nonExistentSubOrderId = Guid.NewGuid();
        var request = new DisputeRefundRequest(Guid.NewGuid(), 100_000m, "Item damaged");

        // Act
        var act = () => _orderService.DisputeRefundSubOrderAsync(nonExistentSubOrderId, request, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<EntityNotFoundException>();
        ex.Which.ErrorCode.Should().Be("SUB_ORDER_NOT_FOUND");
    }

    [Theory]
    [InlineData(SubOrderStatus.PendingPayment)]
    [InlineData(SubOrderStatus.Confirmed)]
    [InlineData(SubOrderStatus.Shipped)]
    [InlineData(SubOrderStatus.Cancelled)]
    [InlineData(SubOrderStatus.Refunded)]
    [InlineData(SubOrderStatus.Returning)]
    public async Task DisputeRefundSubOrderAsync_WhenStatusIneligible_ThrowsInvalidRefundStatus_400(SubOrderStatus invalidStatus)
    {
        // Arrange
        var subOrder = SeedSubOrder(invalidStatus, 400_000m);
        var request = new DisputeRefundRequest(Guid.NewGuid(), 400_000m, "Dispute claim");

        // Act
        var act = () => _orderService.DisputeRefundSubOrderAsync(subOrder.Id, request, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_REFUND_STATUS");
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.Message.Should().Be("Sub-order status is not eligible for dispute refund.");
    }

    [Fact]
    public async Task DisputeRefundSubOrderAsync_WhenEligible_ExecutesRefundTransitionsToRefundedAndPersistsHistory()
    {
        // Arrange
        var subOrder = SeedSubOrder(SubOrderStatus.Delivered, 850_000m);
        var disputeId = Guid.NewGuid();
        var request = new DisputeRefundRequest(disputeId, 850_000m, "Damaged goods in transit", "Resolved in favor of buyer");

        _mockWalletClient
            .Setup(w => w.RefundSubOrderPaymentAsync(
                subOrder.Id,
                subOrder.BuyerId.ToString(),
                850_000m,
                request.Reason,
                $"dispute-refund-{disputeId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _orderService.DisputeRefundSubOrderAsync(subOrder.Id, request, CancellationToken.None);

        // Assert Response
        response.Should().NotBeNull();
        response.SubOrderId.Should().Be(subOrder.Id);
        response.Amount.Should().Be(850_000m);
        response.Status.Should().Be("Completed");
        response.RefundId.Should().NotBeEmpty();

        // Assert Database State
        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Refunded);

        var savedRefund = _dbContext.OrderRefunds.FirstOrDefault(r => r.SubOrderId == subOrder.Id);
        savedRefund.Should().NotBeNull();
        savedRefund!.Status.Should().Be(RefundStatus.Completed);
        savedRefund.Amount.Should().Be(850_000m);
        savedRefund.ReasonCode.Should().Be("DISPUTE_RESOLUTION");
        savedRefund.ReasonNote.Should().Contain("Damaged goods in transit");

        var statusHistory = _dbContext.OrderStatusHistories.FirstOrDefault(h => h.SubOrderId == subOrder.Id && h.ToStatus == SubOrderStatus.Refunded);
        statusHistory.Should().NotBeNull();
        statusHistory!.FromStatus.Should().Be(SubOrderStatus.Delivered);
        statusHistory.ActorRole.Should().Be(OrderActorRole.System);
        statusHistory.Reason.Should().Contain("Damaged goods in transit");

        // Verify Wallet Client Invocation
        _mockWalletClient.Verify(w => w.RefundSubOrderPaymentAsync(
            subOrder.Id,
            subOrder.BuyerId.ToString(),
            850_000m,
            request.Reason,
            $"dispute-refund-{disputeId}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisputeRefundSubOrderAsync_WhenWalletFails_ThrowsWalletRefundFailed_400()
    {
        // Arrange
        var subOrder = SeedSubOrder(SubOrderStatus.Completed, 300_000m);
        var disputeId = Guid.NewGuid();
        var request = new DisputeRefundRequest(disputeId, 300_000m, "Wrong product");

        _mockWalletClient
            .Setup(w => w.RefundSubOrderPaymentAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var act = () => _orderService.DisputeRefundSubOrderAsync(subOrder.Id, request, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("WALLET_REFUND_FAILED");
        ex.Which.StatusCode.Should().Be(400);

        // Verify SubOrder was not changed
        var unchangedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        unchangedSubOrder!.Status.Should().Be(SubOrderStatus.Completed);
    }

    [Fact]
    public async Task DisputeRefundSubOrderAsync_WhenPendingRefundExists_UpdatesAndCompletesExistingRefund()
    {
        // Arrange
        var subOrder = SeedSubOrder(SubOrderStatus.Delivered, 600_000m);
        var existingRefund = new OrderRefund
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            OrderId = subOrder.OrderId,
            RefundType = RefundType.Full,
            Amount = 600_000m,
            Status = RefundStatus.Requested,
            ReasonCode = "BUYER_REQUEST",
            ActorRole = OrderActorRole.Buyer,
            RequestedBy = subOrder.BuyerId,
            RequestedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        _dbContext.OrderRefunds.Add(existingRefund);
        await _dbContext.SaveChangesAsync();

        var disputeId = Guid.NewGuid();
        var request = new DisputeRefundRequest(disputeId, 600_000m, "Dispute resolved", "Support approved refund");

        _mockWalletClient
            .Setup(w => w.RefundSubOrderPaymentAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _orderService.DisputeRefundSubOrderAsync(subOrder.Id, request, CancellationToken.None);

        // Assert
        response.RefundId.Should().Be(existingRefund.Id);
        response.Status.Should().Be("Completed");

        var refunds = _dbContext.OrderRefunds.Where(r => r.SubOrderId == subOrder.Id).ToList();
        refunds.Should().HaveCount(1);
        refunds[0].Status.Should().Be(RefundStatus.Completed);
        refunds[0].ReasonCode.Should().Be("DISPUTE_RESOLUTION");
    }

    #endregion
}
