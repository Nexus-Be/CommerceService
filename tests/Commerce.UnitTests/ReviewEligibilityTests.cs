namespace Commerce.UnitTests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Api.Controllers;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
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

/// <summary>
/// UserService asks Commerce who bought, who sold and whether the order finished before it accepts a review.
/// The reference is a sub-order (never the parent order) or an auction order.
/// </summary>
public sealed class ReviewEligibilityTests
{
    private const string Key = "unit-test-internal-key";

    // --- the lookup --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task SubOrder_ReturnsBuyerSellerStatusAndCompletionTime()
    {
        var (service, db) = CreateService();
        var completedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var subOrder = await AddSubOrder(db, SubOrderStatus.Completed, completedAt);

        var result = await service.GetReviewEligibilityAsync("Order", subOrder.Id);

        result.Should().NotBeNull();
        result!.ReferenceType.Should().Be("Order");
        result.ReferenceId.Should().Be(subOrder.Id);
        result.Status.Should().Be("Completed");
        result.BuyerId.Should().Be(subOrder.BuyerId);
        result.SellerId.Should().Be(subOrder.SellerId);
        result.CompletedAt.Should().Be(completedAt);
    }

    [Fact]
    public async Task ParentOrderId_IsNotAReference()
    {
        var (service, db) = CreateService();
        var subOrder = await AddSubOrder(db, SubOrderStatus.Completed, DateTimeOffset.UtcNow);

        var result = await service.GetReviewEligibilityAsync("Order", subOrder.OrderId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task NotFinishedSubOrder_IsReportedWithItsStatus_SoTheCallerCanRefuse()
    {
        var (service, db) = CreateService();
        var subOrder = await AddSubOrder(db, SubOrderStatus.Delivered, null);

        var result = await service.GetReviewEligibilityAsync("order", subOrder.Id);   // the type is not case sensitive

        result!.Status.Should().Be("Delivered");
        result.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task AuctionOrder_IsLookedUpByItsOwnId()
    {
        var (service, db) = CreateService();
        var completedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var auctionOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(), OrderNo = "AO-1", AuctionId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductNameSnapshot = "Vase", WinningAmount = 1000000m,
            SellerReceiveAmount = 950000m, PlatformFeeAmount = 50000m, Status = SubOrderStatus.Completed,
            PaymentMethod = PaymentMethod.Wallet, PaymentDeadlineAt = DateTimeOffset.UtcNow, CompletedAt = completedAt
        };
        db.AuctionOrders.Add(auctionOrder);
        await db.SaveChangesAsync();

        var result = await service.GetReviewEligibilityAsync("Auction", auctionOrder.Id);
        var asOrder = await service.GetReviewEligibilityAsync("Order", auctionOrder.Id);

        result!.ReferenceType.Should().Be("Auction");
        result.Status.Should().Be("Completed");
        result.BuyerId.Should().Be(auctionOrder.BuyerId);
        result.SellerId.Should().Be(auctionOrder.SellerId);
        result.CompletedAt.Should().Be(completedAt);
        asOrder.Should().BeNull();                                    // an auction order is not a sub-order
    }

    [Theory]
    [InlineData("")]
    [InlineData("Dispute")]
    [InlineData(null)]
    public async Task UnknownReferenceType_FindsNothing(string? type)
    {
        var (service, db) = CreateService();
        var subOrder = await AddSubOrder(db, SubOrderStatus.Completed, DateTimeOffset.UtcNow);

        (await service.GetReviewEligibilityAsync(type!, subOrder.Id)).Should().BeNull();
    }

    [Fact]
    public async Task EmptyOrUnknownId_FindsNothing()
    {
        var (service, _) = CreateService();

        (await service.GetReviewEligibilityAsync("Order", Guid.Empty)).Should().BeNull();
        (await service.GetReviewEligibilityAsync("Order", Guid.NewGuid())).Should().BeNull();
        (await service.GetReviewEligibilityAsync("Auction", Guid.NewGuid())).Should().BeNull();
    }

    // --- the endpoint ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Endpoint_WithoutAConfiguredKey_RefusesEveryCaller()
    {
        var controller = CreateController(Mock.Of<IOrderService>(), headerKey: Key);

        var result = await controller.GetReviewEligibility("Order", Guid.NewGuid(), new ConfigurationBuilder().Build(), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    [InlineData("unit-test-internal-ke")]
    public async Task Endpoint_WithAMissingOrWrongKey_Returns401_AndAsksNothing(string? header)
    {
        var orders = new Mock<IOrderService>();
        var controller = CreateController(orders.Object, header);

        var result = await controller.GetReviewEligibility("Order", Guid.NewGuid(), Configuration(), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        orders.Verify(o => o.GetReviewEligibilityAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Dispute", "11111111-1111-1111-1111-111111111111")]
    [InlineData("Order", "00000000-0000-0000-0000-000000000000")]
    public async Task Endpoint_WithABadReference_Returns400(string type, string id)
    {
        var controller = CreateController(Mock.Of<IOrderService>(), Key);

        var result = await controller.GetReviewEligibility(type, Guid.Parse(id), Configuration(), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Endpoint_Returns404ForAnUnknownOrder_AndTheAnswerForAKnownOne()
    {
        var known = new ReviewEligibilityDto("Order", Guid.NewGuid(), "Completed", Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        var orders = new Mock<IOrderService>();
        orders.Setup(o => o.GetReviewEligibilityAsync("Order", known.ReferenceId, It.IsAny<CancellationToken>())).ReturnsAsync(known);
        orders.Setup(o => o.GetReviewEligibilityAsync("Order", It.Is<Guid>(g => g != known.ReferenceId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReviewEligibilityDto?)null);
        var controller = CreateController(orders.Object, Key);

        var found = await controller.GetReviewEligibility("Order", known.ReferenceId, Configuration(), CancellationToken.None);
        var missing = await controller.GetReviewEligibility("Order", Guid.NewGuid(), Configuration(), CancellationToken.None);

        found.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(known);
        missing.Should().BeOfType<NotFoundObjectResult>();
    }

    // --- helpers -----------------------------------------------------------------------------------------------------

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["InternalSecurity:ServiceKey"] = Key }).Build();

    private static OrdersController CreateController(IOrderService orders, string? headerKey)
    {
        var controller = new OrdersController(orders);
        var httpContext = new DefaultHttpContext();
        if (headerKey is not null)
            httpContext.Request.Headers["X-Internal-Service-Key"] = headerKey;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static (OrderService Service, TestCommerceDbContext Db) CreateService()
    {
        var db = TestDbContextFactory.CreateInMemoryDbContext();
        var service = new OrderService(
            db, Mock.Of<IWalletClient>(), Mock.Of<IFulfillmentClient>(), Mock.Of<ICatalogClient>(), Mock.Of<IUserClient>(),
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Mock.Of<ILogger<OrderService>>());
        return (service, db);
    }

    private static async Task<SubOrder> AddSubOrder(TestCommerceDbContext db, SubOrderStatus status, DateTimeOffset? completedAt)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(), BuyerId = Guid.NewGuid(), Status = OrderStatus.Completed, TotalAmount = 250000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow
        };
        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(), SubOrderNo = $"SUB-{Guid.NewGuid():N}"[..20], OrderId = order.Id, BuyerId = order.BuyerId,
            SellerId = Guid.NewGuid(), Status = status, PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone = "0901234567", ItemsAmount = 220000m, ShippingFeeAmount = 30000m, TotalAmount = 250000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow, CompletedAt = completedAt
        };
        db.Orders.Add(order);
        db.SubOrders.Add(subOrder);
        await db.SaveChangesAsync();
        return subOrder;
    }
}
