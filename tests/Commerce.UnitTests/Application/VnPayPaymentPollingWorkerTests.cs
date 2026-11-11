namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Commerce.Api.Workers;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;

public class VnPayPaymentPollingWorkerTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<IServiceProvider> _mockServiceProvider = new();
    private readonly Mock<IServiceScope> _mockServiceScope = new();
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory = new();

    public VnPayPaymentPollingWorkerTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();

        _mockFulfillmentClient
            .Setup(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.BatchReleaseStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid> ids, CancellationToken _) => new BatchReleaseStockResult(ids, new List<Guid>(), true));

        var scopedServices = new Mock<IServiceProvider>();
        scopedServices.Setup(s => s.GetService(typeof(ICommerceDbContext))).Returns(_dbContext);
        scopedServices.Setup(s => s.GetService(typeof(IWalletClient))).Returns(_mockWalletClient.Object);
        scopedServices.Setup(s => s.GetService(typeof(IFulfillmentClient))).Returns(_mockFulfillmentClient.Object);

        _mockServiceScope.Setup(s => s.ServiceProvider).Returns(scopedServices.Object);
        _mockScopeFactory.Setup(s => s.CreateScope()).Returns(_mockServiceScope.Object);
        _mockServiceProvider.Setup(s => s.GetService(typeof(IServiceScopeFactory))).Returns(_mockScopeFactory.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private Order SeedVnPayOrder(Guid buyerId, decimal amount = 200000m)
    {
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.PendingPayment,
            TotalAmount = amount,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaymentAttemptCount = 1,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = $"SUB-VNPAY-001",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.VnPay,
            ShippingAddress = new ShippingAddressSnapshot("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone = "0901234567",
            ItemsAmount = amount - 30000m,
            ShippingFeeAmount = 30000m,
            SellerReceiveAmount = amount - 40000m,
            TotalAmount = amount,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            CreatedAt = DateTimeOffset.UtcNow
        };

        order.SubOrders.Add(subOrder);
        _dbContext.Orders.Add(order);
        _dbContext.SaveChanges();

        return order;
    }

    [Fact]
    public async Task ProcessPendingVnPayOrdersAsync_ShouldTransitionToPaid_AndCommitStock_WhenGatewayReturnsSuccess()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var order = SeedVnPayOrder(buyerId);
        var txId = Guid.NewGuid();

        _mockWalletClient
            .Setup(w => w.GetDirectCheckoutStatusAsync(order.Id, buyerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectCheckoutStatusResult(order.Id, txId, "Success", order.TotalAmount, DateTimeOffset.UtcNow));

        var worker = new VnPayPaymentPollingWorker(_mockServiceProvider.Object, NullLogger<VnPayPaymentPollingWorker>.Instance);

        // Act
        await worker.ProcessPendingVnPayOrdersAsync(CancellationToken.None);

        // Assert
        var updatedOrder = await _dbContext.Orders.Include(o => o.SubOrders).FirstOrDefaultAsync(o => o.Id == order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.Paid);
        updatedOrder.WalletTransactionId.Should().Be(txId);
        updatedOrder.SubOrders.Should().AllSatisfy(so =>
        {
            so.Status.Should().Be(SubOrderStatus.Paid);
            so.PaidAt.Should().NotBeNull();
            so.WalletTransactionId.Should().Be(txId);
        });

        _mockFulfillmentClient.Verify(f => f.CommitStockAsync(order.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingVnPayOrdersAsync_ShouldCancelOrder_AndReleaseStock_WhenGatewayReturnsFailed()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var order = SeedVnPayOrder(buyerId);

        _mockWalletClient
            .Setup(w => w.GetDirectCheckoutStatusAsync(order.Id, buyerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectCheckoutStatusResult(order.Id, null, "Failed", order.TotalAmount, null));

        var worker = new VnPayPaymentPollingWorker(_mockServiceProvider.Object, NullLogger<VnPayPaymentPollingWorker>.Instance);

        // Act
        await worker.ProcessPendingVnPayOrdersAsync(CancellationToken.None);

        // Assert
        var updatedOrder = await _dbContext.Orders.Include(o => o.SubOrders).FirstOrDefaultAsync(o => o.Id == order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.Cancelled);
        updatedOrder.SubOrders.Should().AllSatisfy(so =>
        {
            so.Status.Should().Be(SubOrderStatus.Cancelled);
            so.CancelledAt.Should().NotBeNull();
        });

        _mockFulfillmentClient.Verify(f => f.BatchReleaseStockAsync(It.Is<List<Guid>>(ids => ids.Contains(order.Id)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessPendingVnPayOrdersAsync_ShouldDoNothing_WhenGatewayReturnsPending()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var order = SeedVnPayOrder(buyerId);

        _mockWalletClient
            .Setup(w => w.GetDirectCheckoutStatusAsync(order.Id, buyerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectCheckoutStatusResult(order.Id, null, "Pending", order.TotalAmount, null));

        var worker = new VnPayPaymentPollingWorker(_mockServiceProvider.Object, NullLogger<VnPayPaymentPollingWorker>.Instance);

        // Act
        await worker.ProcessPendingVnPayOrdersAsync(CancellationToken.None);

        // Assert
        var updatedOrder = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.PendingPayment);
        _mockFulfillmentClient.Verify(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockFulfillmentClient.Verify(f => f.BatchReleaseStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
