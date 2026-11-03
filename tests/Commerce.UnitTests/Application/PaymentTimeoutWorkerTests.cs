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
using Commerce.UnitTests.Common;

public class PaymentTimeoutWorkerTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<IServiceProvider> _mockServiceProvider = new();
    private readonly Mock<IServiceScope> _mockServiceScope = new();
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory = new();

    public PaymentTimeoutWorkerTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();

        _mockFulfillmentClient
            .Setup(f => f.BatchReleaseStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid> ids, CancellationToken _) => new BatchReleaseStockResult(ids, new List<Guid>(), true));

        var scopedServices = new Mock<IServiceProvider>();
        scopedServices.Setup(s => s.GetService(typeof(ICommerceDbContext))).Returns(_dbContext);
        scopedServices.Setup(s => s.GetService(typeof(IFulfillmentClient))).Returns(_mockFulfillmentClient.Object);

        _mockServiceScope.Setup(s => s.ServiceProvider).Returns(scopedServices.Object);

        _mockScopeFactory.Setup(s => s.CreateScope()).Returns(_mockServiceScope.Object);
        _mockServiceProvider.Setup(s => s.GetService(typeof(IServiceScopeFactory))).Returns(_mockScopeFactory.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task ProcessExpiredOrdersAsync_ShouldBatchCancelOrdersAndReleaseStockInSingleCall()
    {
        // Arrange: Tạo 2 đơn hàng quá hạn thanh toán
        var now = DateTimeOffset.UtcNow;
        var orderId1 = Guid.NewGuid();
        var buyerId1 = Guid.NewGuid();
        var expiredOrder1 = new Order
        {
            Id = orderId1,
            BuyerId = buyerId1,
            Status = OrderStatus.PendingPayment,
            TotalAmount = 70000m,
            PaymentDeadlineAt = now.AddMinutes(-10),
            SubOrders = new List<SubOrder>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderNo = "SO-TEST-001",
                    OrderId = orderId1,
                    BuyerId = buyerId1,
                    SellerId = Guid.NewGuid(),
                    Status = SubOrderStatus.PendingPayment,
                    PaymentMethod = PaymentMethod.Wallet,
                    ContactPhone = "0901234567",
                    PaymentDeadlineAt = now.AddMinutes(-10)
                }
            }
        };

        var orderId2 = Guid.NewGuid();
        var buyerId2 = Guid.NewGuid();
        var expiredOrder2 = new Order
        {
            Id = orderId2,
            BuyerId = buyerId2,
            Status = OrderStatus.PendingPayment,
            TotalAmount = 130000m,
            PaymentDeadlineAt = now.AddMinutes(-5),
            SubOrders = new List<SubOrder>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderNo = "SO-TEST-002",
                    OrderId = orderId2,
                    BuyerId = buyerId2,
                    SellerId = Guid.NewGuid(),
                    Status = SubOrderStatus.PendingPayment,
                    PaymentMethod = PaymentMethod.Cod,
                    ContactPhone = "0912345678",
                    PaymentDeadlineAt = now.AddMinutes(-5)
                }
            }
        };

        // Đơn chưa quá hạn (không được hủy)
        var validOrder = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            Status = OrderStatus.PendingPayment,
            TotalAmount = 50000m,
            PaymentDeadlineAt = now.AddMinutes(10)
        };

        _dbContext.Orders.AddRange(expiredOrder1, expiredOrder2, validOrder);
        await _dbContext.SaveChangesAsync();

        var worker = new PaymentTimeoutWorker(_mockServiceProvider.Object, NullLogger<PaymentTimeoutWorker>.Instance);

        // Act
        await worker.ProcessExpiredOrdersAsync(CancellationToken.None);

        // Assert
        var order1InDb = await _dbContext.Orders.Include(o => o.SubOrders).FirstAsync(o => o.Id == expiredOrder1.Id);
        var order2InDb = await _dbContext.Orders.Include(o => o.SubOrders).FirstAsync(o => o.Id == expiredOrder2.Id);
        var validOrderInDb = await _dbContext.Orders.FirstAsync(o => o.Id == validOrder.Id);

        order1InDb.Status.Should().Be(OrderStatus.Cancelled);
        order1InDb.SubOrders.First().Status.Should().Be(SubOrderStatus.Cancelled);

        order2InDb.Status.Should().Be(OrderStatus.Cancelled);
        order2InDb.SubOrders.First().Status.Should().Be(SubOrderStatus.Cancelled);

        validOrderInDb.Status.Should().Be(OrderStatus.PendingPayment);

        var cancellations = await _dbContext.OrderCancellations.ToListAsync();
        cancellations.Should().HaveCount(2);

        // VERIFY: Gọi BatchReleaseStockAsync đúng 1 lần cho cả 2 đơn
        _mockFulfillmentClient.Verify(
            f => f.BatchReleaseStockAsync(
                It.Is<List<Guid>>(ids => ids.Count == 2 && ids.Contains(expiredOrder1.Id) && ids.Contains(expiredOrder2.Id)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // VERIFY: Tuyệt đối KHÔNG gọi hàm đơn lẻ ReleaseStockAsync (Triệt tiêu N+1)
        _mockFulfillmentClient.Verify(
            f => f.ReleaseStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredOrdersAsync_ShouldDoNothing_WhenNoOrdersAreExpired()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var validOrder = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            Status = OrderStatus.PendingPayment,
            TotalAmount = 50000m,
            PaymentDeadlineAt = now.AddMinutes(10)
        };

        _dbContext.Orders.Add(validOrder);
        await _dbContext.SaveChangesAsync();

        var worker = new PaymentTimeoutWorker(_mockServiceProvider.Object, NullLogger<PaymentTimeoutWorker>.Instance);

        // Act
        await worker.ProcessExpiredOrdersAsync(CancellationToken.None);

        // Assert: Không gọi batch release khi không có đơn quá hạn
        _mockFulfillmentClient.Verify(
            f => f.BatchReleaseStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
