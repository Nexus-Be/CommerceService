namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Commerce.Api.Controllers;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class OrderDisputeEligibilityTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = FulfillmentMocks.WithConfirmedLocations();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<OrderService>> _mockLogger = new();
    private readonly IDistributedCache _cache;
    private readonly OrderService _orderService;
    private readonly OrdersController _controller;
    private readonly IConfiguration _configuration;

    public OrderDisputeEligibilityTests()
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
            _mockLogger.Object
        );

        _controller = new OrdersController(_orderService);

        var configValues = new Dictionary<string, string?>
        {
            { "INTERNAL_SERVICE_SECRET", "test-internal-secret-key-12345" }
        };
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static ShippingAddressSnapshot CreateAddressSnapshot() =>
        new("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM", 1442, "20101", "123 Le Loi, Ben Nghe, Quan 1, TP.HCM");

    [Fact]
    public async Task GetDisputeEligibility_SubOrder_Delivered_IsEligible()
    {
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 130000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            SubOrderNo = "SUB-20261119-001",
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = CreateAddressSnapshot(),
            ContactPhone = "0901234567",
            ItemsAmount = 100000m,
            TotalAmount = 130000m,
            SellerReceiveAmount = 95000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            DeliveredAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var result = await _orderService.GetDisputeEligibilityAsync(subOrder.Id, buyerId, sellerId);

        result.Should().NotBeNull();
        result!.IsEligible.Should().BeTrue();
        result.OrderType.Should().Be("SubOrder");
        result.BuyerId.Should().Be(buyerId);
        result.SellerId.Should().Be(sellerId);
        result.Status.Should().Be("Delivered");
    }

    [Fact]
    public async Task GetDisputeEligibility_SubOrder_Shipped_IsNotEligible()
    {
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 130000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            SubOrderNo = "SUB-20261119-002",
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Shipped,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = CreateAddressSnapshot(),
            ContactPhone = "0901234567",
            ItemsAmount = 100000m,
            TotalAmount = 130000m,
            SellerReceiveAmount = 95000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var result = await _orderService.GetDisputeEligibilityAsync(subOrder.Id, sellerId, buyerId);

        result.Should().NotBeNull();
        result!.IsEligible.Should().BeFalse();
        result.Status.Should().Be("Shipping");
    }

    [Fact]
    public async Task GetDisputeEligibility_AuctionOrder_Completed_IsEligible()
    {
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var auctionOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AO-20261119-002",
            AuctionId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Coin",
            WinningAmount = 500000m,
            SellerReceiveAmount = 475000m,
            PlatformFeeAmount = 25000m,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(24),
            Status = SubOrderStatus.Completed,
            DeliveredAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        var result = await _orderService.GetDisputeEligibilityAsync(auctionOrder.Id, buyerId, sellerId);

        result.Should().NotBeNull();
        result!.IsEligible.Should().BeTrue();
        result.OrderType.Should().Be("AuctionOrder");
    }

    [Fact]
    public async Task GetDisputeEligibility_UserMismatch_ReturnsNull()
    {
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            SubOrderNo = "SUB-20261119-003",
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = CreateAddressSnapshot(),
            ContactPhone = "0901234567",
            ItemsAmount = 100000m,
            TotalAmount = 130000m,
            SellerReceiveAmount = 95000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var result = await _orderService.GetDisputeEligibilityAsync(subOrder.Id, buyerId, strangerId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Controller_GetDisputeEligibility_WhenInternalKeyMissing_Returns401()
    {
        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var actionResult = await _controller.GetDisputeEligibility(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            _configuration,
            CancellationToken.None
        );

        var objResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Controller_GetDisputeEligibility_WhenInternalKeyValid_Returns200()
    {
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 130000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            SubOrderNo = "SUB-20261119-004",
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = CreateAddressSnapshot(),
            ContactPhone = "0901234567",
            ItemsAmount = 100000m,
            TotalAmount = 130000m,
            SellerReceiveAmount = 95000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            DeliveredAt = DateTimeOffset.UtcNow
        };
        _dbContext.Orders.Add(order);
        _dbContext.SubOrders.Add(subOrder);
        await _dbContext.SaveChangesAsync();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Internal-Service-Key"] = "test-internal-secret-key-12345";
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var actionResult = await _controller.GetDisputeEligibility(
            subOrder.Id,
            buyerId,
            sellerId,
            _configuration,
            CancellationToken.None
        );

        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<DisputeEligibilityDto>().Subject;
        dto.IsEligible.Should().BeTrue();
    }
}
