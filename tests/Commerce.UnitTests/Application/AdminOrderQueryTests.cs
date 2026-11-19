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
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class AdminOrderQueryTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<OrderService>> _mockOrderLogger = new();
    private readonly IDistributedCache _cache;
    private readonly OrderService _orderService;

    public AdminOrderQueryTests()
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
    public async Task ListAdminOrders_FiltersByBuyerId_ReturnsMatchingOrders()
    {
        // Arrange
        var targetBuyerId = Guid.NewGuid();
        var otherBuyerId = Guid.NewGuid();

        _dbContext.Orders.AddRange(
            new Order
            {
                Id = Guid.NewGuid(),
                BuyerId = targetBuyerId,
                Status = OrderStatus.Paid,
                TotalAmount = 200000m,
                PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
            },
            new Order
            {
                Id = Guid.NewGuid(),
                BuyerId = targetBuyerId,
                Status = OrderStatus.PendingPayment,
                TotalAmount = 300000m,
                PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
            },
            new Order
            {
                Id = Guid.NewGuid(),
                BuyerId = otherBuyerId,
                Status = OrderStatus.Paid,
                TotalAmount = 400000m,
                PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
            }
        );
        await _dbContext.SaveChangesAsync();

        var filter = new AdminOrderFilterRequest(BuyerId: targetBuyerId, Page: 1, PageSize: 10);

        // Act
        var result = await _orderService.ListAdminOrdersAsync(filter);

        // Assert
        result.Should().NotBeNull();
        result.Pagination.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAdminOrders_FiltersByStatus_ReturnsMatchingOrders()
    {
        // Arrange
        _dbContext.Orders.AddRange(
            new Order
            {
                Id = Guid.NewGuid(),
                BuyerId = Guid.NewGuid(),
                Status = OrderStatus.Completed,
                TotalAmount = 500000m,
                PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
            },
            new Order
            {
                Id = Guid.NewGuid(),
                BuyerId = Guid.NewGuid(),
                Status = OrderStatus.PendingPayment,
                TotalAmount = 100000m,
                PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
            }
        );
        await _dbContext.SaveChangesAsync();

        var filter = new AdminOrderFilterRequest(Status: OrderStatus.Completed, Page: 1, PageSize: 10);

        // Act
        var result = await _orderService.ListAdminOrdersAsync(filter);

        // Assert
        result.Should().NotBeNull();
        result.Pagination.TotalCount.Should().Be(1);
        result.Items[0].TotalAmount.Should().Be(500000m);
    }

    [Fact]
    public async Task ListAdminSubOrders_FiltersBySellerId_ReturnsMatchingSubOrders()
    {
        // Arrange
        var targetSellerId = Guid.NewGuid();
        var otherSellerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            Status = OrderStatus.Paid,
            TotalAmount = 600000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.Orders.Add(order);

        _dbContext.SubOrders.AddRange(
            new SubOrder
            {
                Id = Guid.NewGuid(),
                SubOrderNo = "SO-ADM-01",
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                SellerId = targetSellerId,
                Status = SubOrderStatus.Paid,
                PaymentMethod = PaymentMethod.Wallet,
                ContactPhone = "0901234567",
                TotalAmount = 200000m,
                PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
            },
            new SubOrder
            {
                Id = Guid.NewGuid(),
                SubOrderNo = "SO-ADM-02",
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                SellerId = otherSellerId,
                Status = SubOrderStatus.Paid,
                PaymentMethod = PaymentMethod.Wallet,
                ContactPhone = "0901234567",
                TotalAmount = 400000m,
                PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
            }
        );
        await _dbContext.SaveChangesAsync();

        var filter = new AdminSubOrderFilterRequest(SellerId: targetSellerId, Page: 1, PageSize: 10);

        // Act
        var result = await _orderService.ListAdminSubOrdersAsync(filter);

        // Assert
        result.Should().NotBeNull();
        result.Pagination.TotalCount.Should().Be(1);
        result.Items[0].SubOrderNo.Should().Be("SO-ADM-01");
        result.Items[0].SellerId.Should().Be(targetSellerId);
    }

    [Fact]
    public async Task ListAdminSubOrders_FiltersByTrackingCode_ReturnsMatchingSubOrders()
    {
        // Arrange
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            Status = OrderStatus.Paid,
            TotalAmount = 500000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.Orders.Add(order);

        _dbContext.SubOrders.AddRange(
            new SubOrder
            {
                Id = Guid.NewGuid(),
                SubOrderNo = "SO-ADM-03",
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                SellerId = Guid.NewGuid(),
                Status = SubOrderStatus.Shipping,
                PaymentMethod = PaymentMethod.Wallet,
                ContactPhone = "0901234567",
                TrackingCode = "GHNADMINTRACK888",
                TotalAmount = 250000m,
                PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
            },
            new SubOrder
            {
                Id = Guid.NewGuid(),
                SubOrderNo = "SO-ADM-04",
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                SellerId = Guid.NewGuid(),
                Status = SubOrderStatus.Shipping,
                PaymentMethod = PaymentMethod.Wallet,
                ContactPhone = "0901234567",
                TrackingCode = "GHNADMINTRACK999",
                TotalAmount = 250000m,
                PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
            }
        );
        await _dbContext.SaveChangesAsync();

        var filter = new AdminSubOrderFilterRequest(TrackingCode: "GHNADMINTRACK888", Page: 1, PageSize: 10);

        // Act
        var result = await _orderService.ListAdminSubOrdersAsync(filter);

        // Assert
        result.Should().NotBeNull();
        result.Pagination.TotalCount.Should().Be(1);
        result.Items[0].SubOrderNo.Should().Be("SO-ADM-03");
    }
}
