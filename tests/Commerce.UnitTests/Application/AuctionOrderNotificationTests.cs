namespace Commerce.UnitTests.Application;

using System;
using System.Threading.Tasks;
using Commerce.Api.Controllers.Public;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.AuctionOrder;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// What Commerce tells Auction when an auction order is paid, and what it answers when Auction asks for the order data the notification needs.
/// The first send and every retry must carry the same content, the total is the order total (not the winning amount), and an unpaid order has
/// nothing to announce.
/// </summary>
public sealed class AuctionOrderNotificationTests
{
    private const string Key = "unit-test-internal-key";

    private readonly TestCommerceDbContext _db = TestDbContextFactory.CreateInMemoryDbContext();
    private readonly Mock<IWalletClient> _wallet = new();
    private readonly Mock<IAuctionClient> _auction = new();
    private readonly AuctionOrderService _service;

    public AuctionOrderNotificationTests()
    {
        _service = new AuctionOrderService(
            _db, _wallet.Object, Mock.Of<IFulfillmentClient>(), _auction.Object, Mock.Of<ICatalogClient>(), Mock.Of<IUserClient>(),
            Mock.Of<ILogger<AuctionOrderService>>());
    }

    private static AuctionOrder NewOrder(SubOrderStatus status = SubOrderStatus.PendingPayment) => new()
    {
        Id = Guid.NewGuid(), OrderNo = "AO-20261117-00007", AuctionId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
        WinningBidId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductNameSnapshot = "Rare Painting", WinningAmount = 5_000_000m,
        ShippingAddress = "{\"fullName\":\"A\"}", ShippingFeeAmount = 50_000m, TotalAmount = 5_050_000m,
        SellerReceiveAmount = 4_750_000m, PlatformFeeAmount = 250_000m, Status = status, PaymentMethod = PaymentMethod.Wallet,
        PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(12)
    };

    [Fact]
    public async Task ThePaidCallback_CarriesTheOrderNumberTheTotalWithShippingAndTheSellerDeadline()
    {
        var order = NewOrder();
        _db.AuctionOrders.Add(order);
        await _db.SaveChangesAsync();
        _wallet.Setup(w => w.PayOrderAsync(order.Id, order.BuyerId, 5_050_000m, null, default)).ReturnsAsync(WalletPaymentResult.Success(Guid.NewGuid().ToString()));
        AuctionPaymentStatusCallbackRequest? sent = null;
        _auction.Setup(a => a.NotifyPaymentStatusAsync(order.AuctionId, It.IsAny<AuctionPaymentStatusCallbackRequest>(), default))
            .Callback<Guid, AuctionPaymentStatusCallbackRequest, System.Threading.CancellationToken>((_, request, _) => sent = request)
            .ReturnsAsync(true);

        await _service.PayAuctionOrderAsync(order.Id, order.BuyerId);

        sent.Should().NotBeNull();
        sent!.Status.Should().Be("Paid");
        sent.Amount.Should().Be(5_000_000m, "Auction still checks the winning amount");
        sent.TotalAmount.Should().Be(5_050_000m, "the notification shows the order total, shipping included");
        sent.OrderNo.Should().Be("AO-20261117-00007");
        var stored = await _db.AuctionOrders.FindAsync(order.Id);
        sent.SellerRespondDeadlineAt.Should().Be(stored!.SellerRespondDeadlineAt);
        sent.PaidAt.Should().Be(stored.PaidAt);
    }

    [Fact]
    public async Task ARetry_BuildsExactlyTheCallbackOfTheFirstSend()
    {
        var order = NewOrder();
        _db.AuctionOrders.Add(order);
        await _db.SaveChangesAsync();
        _wallet.Setup(w => w.PayOrderAsync(order.Id, order.BuyerId, 5_050_000m, null, default)).ReturnsAsync(WalletPaymentResult.Success(Guid.NewGuid().ToString()));
        AuctionPaymentStatusCallbackRequest? first = null;
        _auction.Setup(a => a.NotifyPaymentStatusAsync(order.AuctionId, It.IsAny<AuctionPaymentStatusCallbackRequest>(), default))
            .Callback<Guid, AuctionPaymentStatusCallbackRequest, System.Threading.CancellationToken>((_, request, _) => first = request)
            .ReturnsAsync(false);   // not delivered: the worker will send it again

        await _service.PayAuctionOrderAsync(order.Id, order.BuyerId);

        var stored = await _db.AuctionOrders.FindAsync(order.Id);
        var history = _db.OrderStatusHistories.Single(h => h.AuctionOrderId == order.Id && h.AuctionCallbackStatus == "Paid");
        history.AuctionCallbackSentAt.Should().BeNull();
        var retry = AuctionPaymentCallbackFactory.Build(stored!, history);
        retry.Should().Be(first!, "the same event id must carry the same content or Auction refuses the retry");
        retry.OrderSource.Should().NotBeNullOrEmpty("the retry used to leave out the order source and the time");
        retry.OccurredAt.Should().NotBeNull();
    }

    [Fact]
    public void ANonPaidCallback_CarriesNoPaymentData()
    {
        var order = NewOrder(SubOrderStatus.Cancelled);
        var history = new OrderStatusHistory { Id = Guid.NewGuid(), AuctionOrderId = order.Id, AuctionCallbackStatus = "Expired", ActorRole = OrderActorRole.System, ToStatus = SubOrderStatus.Cancelled };

        var callback = AuctionPaymentCallbackFactory.Build(order, history);

        callback.Status.Should().Be("Expired");
        callback.Amount.Should().BeNull();
        callback.TotalAmount.Should().BeNull();
        callback.OrderNo.Should().BeNull();
        callback.PaidAt.Should().BeNull();
        callback.EventId.Should().Be(history.Id);
    }

    // ------------------------------------------------------------------ the snapshot

    [Fact]
    public async Task APaidOrder_HasASnapshot_WithTheTotalAndTheParties()
    {
        var order = NewOrder(SubOrderStatus.Paid);
        order.PaidAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        order.SellerRespondDeadlineAt = order.PaidAt.Value.AddHours(24);
        _db.AuctionOrders.Add(order);
        await _db.SaveChangesAsync();

        var snapshot = await _service.GetNotificationSnapshotAsync(order.Id);

        snapshot.Should().NotBeNull();
        snapshot!.OrderNo.Should().Be("AO-20261117-00007");
        snapshot.TotalAmount.Should().Be(5_050_000m);
        snapshot.PaidAt.Should().Be(order.PaidAt!.Value);
        snapshot.SellerRespondDeadlineAt.Should().Be(order.SellerRespondDeadlineAt!.Value);
        snapshot.BuyerId.Should().Be(order.BuyerId);
        snapshot.SellerId.Should().Be(order.SellerId);
        snapshot.Status.Should().Be("Paid");
    }

    [Fact]
    public async Task AnUnpaidOrFictionalOrder_HasNoSnapshot()
    {
        var unpaid = NewOrder();
        _db.AuctionOrders.Add(unpaid);
        await _db.SaveChangesAsync();

        (await _service.GetNotificationSnapshotAsync(unpaid.Id)).Should().BeNull();
        (await _service.GetNotificationSnapshotAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task TheSnapshotEndpoint_NeedsTheInternalKey_AndAnswers404ForAnUnpaidOrder()
    {
        var paid = NewOrder(SubOrderStatus.Paid);
        paid.PaidAt = DateTimeOffset.UtcNow;
        paid.SellerRespondDeadlineAt = DateTimeOffset.UtcNow.AddHours(24);
        _db.AuctionOrders.Add(paid);
        await _db.SaveChangesAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new[] { new System.Collections.Generic.KeyValuePair<string, string?>("InternalSecurity:ServiceKey", Key) }).Build();

        AuctionOrdersController Controller(string? header)
        {
            var controller = new AuctionOrdersController(_service);
            var http = new DefaultHttpContext();
            if (header is not null) http.Request.Headers["X-Internal-Service-Key"] = header;
            controller.ControllerContext = new ControllerContext { HttpContext = http };
            return controller;
        }

        ((ObjectResult)await Controller(null).GetNotificationSnapshot(paid.Id, configuration, default)).StatusCode.Should().Be(401);
        ((ObjectResult)await Controller("wrong").GetNotificationSnapshot(paid.Id, configuration, default)).StatusCode.Should().Be(401);
        (await Controller(Key).GetNotificationSnapshot(paid.Id, configuration, default)).Should().BeOfType<OkObjectResult>();
        (await Controller(Key).GetNotificationSnapshot(Guid.NewGuid(), configuration, default)).Should().BeOfType<NotFoundObjectResult>();
    }
}
