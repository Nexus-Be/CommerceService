namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Cart;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// The "order paid" notification of a regular order: one row per sub-order in the notification outbox, saved by the same SaveChanges as the
/// payment (never before it, never separately), and only when the order really is paid.
/// </summary>
public sealed class OrderPaidNotificationTests : IDisposable
{
    private readonly TestCommerceDbContext _db = TestDbContextFactory.CreateInMemoryDbContext();
    private readonly Mock<IWalletClient> _wallet = new();
    private readonly Mock<IFulfillmentClient> _fulfillment = FulfillmentMocks.WithConfirmedLocations();
    private readonly Mock<ICatalogClient> _catalog = new();
    private readonly Mock<IUserClient> _users = new();
    private readonly IDistributedCache _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private sealed class Settings(bool enabled) : INotificationSettings
    {
        public bool Enabled { get; } = enabled;
    }

    public OrderPaidNotificationTests()
    {
        _users.Setup(u => u.CheckBuyerPermissionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserPermissionCheckResult(true, null, null));
        _fulfillment.Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(30000m);
        _fulfillment.Setup(f => f.ReserveStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _fulfillment.Setup(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _wallet.Setup(w => w.GetWalletBalanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(5_000_000m);
        PayWith(new WalletPaymentResult(true, TransactionId: Guid.NewGuid().ToString(), BalanceAfter: 1m));
    }

    public void Dispose() => _db.Dispose();

    private void PayWith(WalletPaymentResult result)
    {
        _wallet.Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);
        _wallet.Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);
    }

    private OrderService Service(bool notificationsOn = true) => new(
        _db, _wallet.Object, _fulfillment.Object, _catalog.Object, _users.Object, _cache, Mock.Of<ILogger<OrderService>>(),
        null, null, new Settings(notificationsOn));

    /// <summary>One cart with one item per seller; returns the buyer, the cart item ids and the sellers.</summary>
    private async Task<(Guid Buyer, List<Guid> Items, List<Guid> Sellers)> Cart(int sellers = 1)
    {
        var buyer = Guid.NewGuid();
        var cart = new Cart { Id = Guid.NewGuid(), BuyerId = buyer, Status = CartStatus.Active, ItemCount = sellers, LastActivityAt = DateTimeOffset.UtcNow, RowVersion = 1 };
        var sellerIds = new List<Guid>();
        for (var i = 0; i < sellers; i++)
        {
            var seller = Guid.NewGuid();
            var sku = Guid.NewGuid();
            sellerIds.Add(seller);
            _catalog.Setup(c => c.GetSkuDetailsAsync(sku, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CatalogProductCheckResult(sku, Guid.NewGuid(), seller, "Item", 100000m, true, 100));
            cart.Items.Add(new CartItem
            {
                Id = Guid.NewGuid(), CartId = cart.Id, ProductId = Guid.NewGuid(), SkuId = sku, SellerId = seller, ProductNameSnapshot = "Item",
                Quantity = 1, UnitPriceSnapshot = 100000m, AddedAt = DateTimeOffset.UtcNow
            });
        }

        cart.SubtotalAmount = 100000m * sellers;
        _db.Carts.Add(cart);
        await _db.SaveChangesAsync();
        return (buyer, cart.Items.Select(i => i.Id).ToList(), sellerIds);
    }

    private async Task<CreateOrderResponse> Checkout(OrderService service, Guid buyer, List<Guid> items, PaymentMethod method = PaymentMethod.Wallet)
    {
        var quote = await service.QuoteCheckoutAsync(buyer, new CheckoutQuoteRequest(
            items, new ShippingAddressInput("A", "0901234567", "1 Street", "Ward", "District", "City", 1442, "20101", ProvinceId: 202), "0901234567", method), CancellationToken.None);
        return await service.CreateOrderAsync(buyer,
            new CreateOrderRequest(quote.QuoteId, method, "1 Street, Ward", "0901234567"), CancellationToken.None);
    }

    [Fact]
    public async Task APaidOrder_QueuesOneNoticePerSubOrder_ThatAreOnlyWaitingForTheShopId()
    {
        var (buyer, items, sellers) = await Cart(sellers: 2);

        var response = await Checkout(Service(), buyer, items);

        response.Status.Should().Be(OrderStatus.Paid.ToString());
        var subOrders = await _db.SubOrders.AsNoTracking().Where(s => s.OrderId == response.OrderId).ToListAsync();
        subOrders.Should().HaveCount(2);
        var rows = await _db.NotificationOutbox.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.SubOrderId).Should().BeEquivalentTo(subOrders.Select(s => s.Id));
        rows.Should().OnlyContain(r => r.Status == NotificationOutboxStatus.AwaitingData && r.PayloadJson == null && r.Kind == "order-payments");
        rows.Select(r => r.SellerUserId).Should().BeEquivalentTo(sellers);
        foreach (var row in rows)
            row.EventId.Should().Be(OrderPaidNotices.EventId(row.SubOrderId));
        subOrders.Should().OnlyContain(s => s.ShopId == null, "the shop id is looked up later, never guessed from the seller user id");
    }

    [Fact]
    public async Task TheSellerGetsAReminderDeadlineOfOneDayAfterPayment()
    {
        var (buyer, items, _) = await Cart();

        var response = await Checkout(Service(), buyer, items);

        var sub = await _db.SubOrders.AsNoTracking().SingleAsync(s => s.OrderId == response.OrderId);
        sub.PaidAt.Should().NotBeNull();
        sub.SellerRespondDeadlineAt.Should().Be(sub.PaidAt!.Value.AddHours(24));
    }

    [Fact]
    public async Task TheNoticeIsSavedByTheSameSaveAsThePayment_NotByAnotherOne()
    {
        // If the save of the payment fails, no notice may exist; if it succeeds, both exist. A second context on the same database shows it.
        var name = $"order-paid-{Guid.NewGuid():N}";
        using var db = TestDbContextFactory.CreateInMemoryDbContext(name);
        using var observer = TestDbContextFactory.CreateInMemoryDbContext(name);
        var sub = new SubOrder
        {
            Id = Guid.NewGuid(), SubOrderNo = "SUB-1", OrderId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Paid, PaymentMethod = PaymentMethod.Wallet, PaidAt = DateTimeOffset.UtcNow,
            ContactPhone = "0901234567", PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        OrderPaidNotices.Enqueue(db, new Settings(true), sub);

        (await observer.NotificationOutbox.CountAsync()).Should().Be(0, "queueing alone writes nothing");
        db.ChangeTracker.Entries<NotificationOutboxItem>().Should().ContainSingle(e => e.State == EntityState.Added);
        await db.SaveChangesAsync();
        (await observer.NotificationOutbox.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task EnqueueingTheSameSubOrderTwice_IsOneEvent()
    {
        var sub = new SubOrder
        {
            Id = Guid.NewGuid(), SubOrderNo = "SUB-1", OrderId = Guid.NewGuid(), BuyerId = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Paid, PaymentMethod = PaymentMethod.Wallet, PaidAt = DateTimeOffset.UtcNow,
            ContactPhone = "0901234567", PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        OrderPaidNotices.Enqueue(_db, new Settings(true), sub);
        OrderPaidNotices.Enqueue(_db, new Settings(true), sub);
        await _db.SaveChangesAsync();

        (await _db.NotificationOutbox.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ADefinitiveWalletFailure_QueuesNothing()
    {
        var (buyer, items, _) = await Cart();
        PayWith(new WalletPaymentResult(false, ErrorCode: "INSUFFICIENT_BALANCE", ErrorMessage: "no"));

        var act = () => Checkout(Service(), buyer, items);

        await act.Should().ThrowAsync<Exception>();
        (await _db.NotificationOutbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ATransientWalletFailure_LeavesTheOrderPendingAndQueuesNothing()
    {
        var (buyer, items, _) = await Cart();
        PayWith(new WalletPaymentResult(false, ErrorCode: "CONNECTION_ERROR", ErrorMessage: "timeout", IsTransientFailure: true));

        var response = await Checkout(Service(), buyer, items);

        response.Status.Should().Be(OrderStatus.PendingPayment.ToString());
        (await _db.NotificationOutbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ACashOnDeliveryOrder_IsNotPaid_SoItQueuesNothing()
    {
        var (buyer, items, _) = await Cart();

        await Checkout(Service(), buyer, items, PaymentMethod.Cod);

        (await _db.NotificationOutbox.CountAsync()).Should().Be(0);
        (await _db.SubOrders.AsNoTracking().ToListAsync()).Should().OnlyContain(s => s.PaidAt == null);
    }

    [Fact]
    public async Task WithNotificationsOff_NothingIsQueued_ButThePaymentStillWorks()
    {
        var (buyer, items, _) = await Cart();

        var response = await Checkout(Service(notificationsOn: false), buyer, items);

        response.Status.Should().Be(OrderStatus.Paid.ToString());
        (await _db.NotificationOutbox.CountAsync()).Should().Be(0);
    }
}
