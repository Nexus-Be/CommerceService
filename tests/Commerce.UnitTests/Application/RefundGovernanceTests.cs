namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class RefundGovernanceTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<OrderService>> _mockOrderLogger = new();
    private readonly IDistributedCache _cache;
    private readonly OrderService _orderService;

    public RefundGovernanceTests()
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
            _mockOrderLogger.Object
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task RefundSubOrder_WhenStatusNotDeliveredOrCompleted_ThrowsInvalidRefundStatus_400()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 100000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SO-REF-01",
            OrderId = order.Id,
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Shipping, // Shipping is not eligible!
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            TotalAmount = 100000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var request = new SubOrderRefundRequest(Reason: "Damaged item", Note: "Cracked screen");

        // Act
        var act = async () => await _orderService.RefundSubOrderAsync(buyerId, subOrder.Id, request);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_REFUND_STATUS");
        ex.Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task RefundSubOrder_WhenPendingRefundExists_ThrowsRefundRequestAlreadyExists_409()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.Delivered,
            TotalAmount = 100000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SO-REF-02",
            OrderId = order.Id,
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            TotalAmount = 100000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var existingRefund = new OrderRefund
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            OrderId = order.Id,
            RefundType = RefundType.Full,
            Amount = 100000m,
            Status = RefundStatus.Requested, // Pending request exists!
            ReasonCode = "BUYER_REQUEST",
            ActorRole = OrderActorRole.Buyer,
            RequestedBy = buyerId,
            RequestedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        _dbContext.OrderRefunds.Add(existingRefund);
        await _dbContext.SaveChangesAsync();

        var request = new SubOrderRefundRequest(Reason: "Duplicate request", Note: "Please refund");

        // Act
        var act = async () => await _orderService.RefundSubOrderAsync(buyerId, subOrder.Id, request);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("REFUND_REQUEST_ALREADY_EXISTS");
        ex.Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task RefundSubOrder_WhenDelivered_CreatesRefundSuccessfully()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.Delivered,
            TotalAmount = 100000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SO-REF-03",
            OrderId = order.Id,
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            TotalAmount = 100000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var request = new SubOrderRefundRequest(Reason: "Damaged item", Note: "Defective on arrival");

        // Act
        await _orderService.RefundSubOrderAsync(buyerId, subOrder.Id, request);

        // Assert
        var refunds = await _orderService.ListRefundsAsync(buyerId, 1, 10);
        refunds.Items.Should().HaveCount(1);
        refunds.Items[0].SubOrderId.Should().Be(subOrder.Id);
        refunds.Items[0].Amount.Should().Be(100000m);
        refunds.Items[0].Status.Should().Be("Requested");
    }

    [Fact]
    public async Task ApproveRefund_CallsWalletClient_SetsCompletedAndSubOrderRefunded()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.Delivered,
            TotalAmount = 150000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SO-REF-04",
            OrderId = order.Id,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            TotalAmount = 150000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var refund = new OrderRefund
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            OrderId = order.Id,
            RefundType = RefundType.Full,
            Amount = 150000m,
            Status = RefundStatus.Requested,
            ReasonCode = "BUYER_REQUEST",
            ActorRole = OrderActorRole.Buyer,
            RequestedBy = buyerId,
            RequestedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        _dbContext.OrderRefunds.Add(refund);
        await _dbContext.SaveChangesAsync();

        _mockWalletClient
            .Setup(w => w.RefundSubOrderPaymentAsync(subOrder.Id, It.IsAny<Guid>(), buyerId.ToString(), 150000m, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletRefundResult.Success());

        var request = new ApproveRefundRequest(Note: "Item accepted for return");

        // Act
        var result = await _orderService.ApproveRefundAsync(sellerId, refund.Id, request);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Completed");
        result.SellerNote.Should().Be("Item accepted for return");

        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrder.Id);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Refunded);

        var updatedRefund = await _dbContext.OrderRefunds.FindAsync(refund.Id);
        updatedRefund!.Status.Should().Be(RefundStatus.Completed);
        updatedRefund.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RejectRefund_WithValidReason_SetsRejected()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.Delivered,
            TotalAmount = 200000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SO-REF-05",
            OrderId = order.Id,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            TotalAmount = 200000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var refund = new OrderRefund
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            OrderId = order.Id,
            RefundType = RefundType.Full,
            Amount = 200000m,
            Status = RefundStatus.Requested,
            ReasonCode = "BUYER_REQUEST",
            ActorRole = OrderActorRole.Buyer,
            RequestedBy = buyerId,
            RequestedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        _dbContext.OrderRefunds.Add(refund);
        await _dbContext.SaveChangesAsync();

        var request = new RejectRefundRequest(Reason: "Product used and damaged by buyer", Note: "No refund allowed");

        // Act
        var result = await _orderService.RejectRefundAsync(sellerId, refund.Id, request);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Rejected");
        result.RejectReason.Should().Be("Product used and damaged by buyer");

        var updatedRefund = await _dbContext.OrderRefunds.FindAsync(refund.Id);
        updatedRefund!.Status.Should().Be(RefundStatus.Rejected);
        updatedRefund.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RejectRefund_WhenReasonEmpty_ThrowsValidationException_400()
    {
        // Arrange
        var refundId = Guid.NewGuid();
        var request = new RejectRefundRequest(Reason: "  ");

        // Act
        var act = async () => await _orderService.RejectRefundAsync(Guid.NewGuid(), refundId, request);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("VAL_INVALID_INPUT");
        ex.Which.StatusCode.Should().Be(400);
    }
}
