namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.AuctionOrder;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// The delivery address a quote caches, an order keeps and an auction order stores carries the names the GHN lists gave for the confirmed codes,
/// not the text the buyer typed. The auction timeline reads the stored address back field by field, and a settlement never carries an address.
/// </summary>
public class ConfirmedAddressNamesTests : IDisposable
{
    private readonly TestCommerceDbContext _db = TestDbContextFactory.CreateInMemoryDbContext();
    private readonly Mock<IFulfillmentClient> _fulfillment = FulfillmentMocks.WithConfirmedLocations();
    private readonly Mock<IUserClient> _user = new();
    private readonly Mock<IWalletClient> _wallet = new();
    private readonly Mock<ICatalogClient> _catalog = new();
    private readonly Mock<IAuctionClient> _auction = new();
    private readonly IDistributedCache _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly OrderService _orders;
    private readonly AuctionOrderService _auctions;

    public ConfirmedAddressNamesTests()
    {
        _fulfillment
            .Setup(f => f.ValidateLocationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LocationCheckResult.Confirmed("Hồ Chí Minh", "Quận 1", "Phường Bến Nghé"));
        _fulfillment
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(25000m);
        _wallet.Setup(w => w.GetWalletBalanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(1000000m);
        _orders = new OrderService(_db, _wallet.Object, _fulfillment.Object, _catalog.Object, _user.Object, _cache, new Mock<ILogger<OrderService>>().Object);
        _auctions = new AuctionOrderService(_db, _wallet.Object, _fulfillment.Object, _auction.Object, _catalog.Object, _user.Object, new Mock<ILogger<AuctionOrderService>>().Object);
    }

    public void Dispose() => _db.Dispose();

    private static ShippingAddressInput Typed() => new("Buyer", "0901234567", "12 Nguyen Hue", "Typed ward", "Typed district", "Typed province", 1442, "20101", ProvinceId: 202);

    private async Task<(Guid BuyerId, Guid CartItemId)> SeedCartAsync()
    {
        var buyerId = Guid.NewGuid();
        var cart = new Cart { Id = Guid.NewGuid(), BuyerId = buyerId, Status = CartStatus.Active, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var item = new CartItem
        {
            Id = Guid.NewGuid(), CartId = cart.Id, SellerId = Guid.NewGuid(), ProductId = Guid.NewGuid(), SkuId = Guid.NewGuid(),
            ProductNameSnapshot = "P", UnitPriceSnapshot = 50000m, Quantity = 1, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        cart.Items.Add(item);
        _db.Carts.Add(cart);
        await _db.SaveChangesAsync();
        return (buyerId, item.Id);
    }

    private async Task<ShippingAddressInput> CachedAddressAsync(Guid quoteId)
    {
        var json = await _cache.GetStringAsync($"checkout_quote:{quoteId}");
        json.Should().NotBeNull();
        return JsonSerializer.Deserialize<CachedQuote>(json!)!.Request.ShippingAddress!;
    }

    [Fact]
    public async Task A_quote_caches_the_GHN_names_so_the_order_made_from_it_keeps_them()
    {
        var (buyerId, cartItemId) = await SeedCartAsync();

        var quote = await _orders.QuoteCheckoutAsync(buyerId, new CheckoutQuoteRequest(CartItemIds: new List<Guid> { cartItemId }, ShippingAddress: Typed()));

        var cached = await CachedAddressAsync(quote.QuoteId);
        cached.Province.Should().Be("Hồ Chí Minh");
        cached.District.Should().Be("Quận 1");
        cached.Ward.Should().Be("Phường Bến Nghé");
        cached.ProvinceId.Should().Be(202);
        cached.FullAddressText.Should().Contain("Phường Bến Nghé").And.NotContain("Typed");
    }

    [Fact]
    public async Task A_re_quote_replaces_the_cached_address_with_the_GHN_names_of_the_new_address()
    {
        var (buyerId, cartItemId) = await SeedCartAsync();
        var quote = await _orders.QuoteCheckoutAsync(buyerId, new CheckoutQuoteRequest(CartItemIds: new List<Guid> { cartItemId }, ShippingAddress: Typed()));
        _fulfillment
            .Setup(f => f.ValidateLocationAsync(202, 1444, "20301", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LocationCheckResult.Confirmed("Hồ Chí Minh", "Quận 3", "Phường 1"));

        await _orders.RecalculateQuoteAsync(buyerId, quote.QuoteId, new RecalculateQuoteRequest(
            ShippingAddress: new ShippingAddressInput("Buyer", "0901234567", "5 Vo Van Tan", "Typed ward 2", "Typed district 2", "Typed province 2", 1444, "20301", ProvinceId: 202)));

        var cached = await CachedAddressAsync(quote.QuoteId);
        cached.District.Should().Be("Quận 3");
        cached.Ward.Should().Be("Phường 1");
        cached.FullAddressText.Should().NotContain("Typed");
    }

    // ---- auction orders
    private static AuctionSettlementRequest Settlement(Guid? auctionId = null, Guid? winner = null, ShippingAddressDto? address = null) => new(
        AuctionId: auctionId ?? Guid.NewGuid(), WinnerId: winner ?? Guid.NewGuid(), SellerId: Guid.NewGuid(), WinningBidId: Guid.NewGuid(), ProductId: Guid.NewGuid(),
        SkuId: null, ProductName: "Item", ProductImageUrl: "https://example.com/x.png", SkuAttributes: "{}", WinningAmount: 1000000m,
        PaymentDeadlineAt: DateTimeOffset.UtcNow.AddHours(24), ShippingAddress: address);

    private static readonly ShippingAddressDto SomeAddress = new("A", "0901234567", "1 Le Loi", "P", "D", "W", 1442, "20101", 202);

    [Fact]
    public async Task A_settlement_with_an_address_is_refused_the_first_time_and_when_it_is_sent_again()
    {
        var auctionId = Guid.NewGuid();
        var winner = Guid.NewGuid();

        var first = () => _auctions.ReceiveSettlementAsync(Settlement(auctionId, winner, SomeAddress));
        (await first.Should().ThrowAsync<CommerceDomainException>()).Which.StatusCode.Should().Be(400);

        // an order is made without an address, then the same settlement is sent again with one: refused, and the order is not touched
        var created = await _auctions.ReceiveSettlementAsync(Settlement(auctionId, winner));
        var again = () => _auctions.ReceiveSettlementAsync(Settlement(auctionId, winner, SomeAddress));
        var ex = (await again.Should().ThrowAsync<CommerceDomainException>()).Which;
        ex.ErrorCode.Should().Be("SETTLEMENT_SHIPPING_ADDRESS_NOT_ACCEPTED");
        ex.StatusCode.Should().Be(400);
        (await _db.AuctionOrders.FindAsync(created.Response.OrderId))!.ShippingAddress.Should().BeNull();
    }

    [Fact]
    public async Task A_settlement_without_an_address_still_works_and_is_idempotent()
    {
        var auctionId = Guid.NewGuid();
        var winner = Guid.NewGuid();

        var request = Settlement(auctionId, winner);
        var first = await _auctions.ReceiveSettlementAsync(request);
        var second = await _auctions.ReceiveSettlementAsync(request);

        first.IsNewlyCreated.Should().BeTrue();
        second.IsNewlyCreated.Should().BeFalse();
        second.Response.OrderId.Should().Be(first.Response.OrderId);
    }

    private async Task<AuctionOrder> SeedAuctionOrderAsync(string? addressJson, Guid? buyer = null)
    {
        var order = new AuctionOrder
        {
            Id = Guid.NewGuid(), OrderNo = "AO-20261125-00001", AuctionId = Guid.NewGuid(), BuyerId = buyer ?? Guid.NewGuid(), SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductNameSnapshot = "Item", WinningAmount = 1000000m, DepositAmount = 0m,
            RemainingAmount = 1000000m, SellerReceiveAmount = 950000m, PlatformFeeAmount = 50000m, Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet, PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(20), ShippingAddress = addressJson, ContactPhone = "0901234567",
        };
        _db.AuctionOrders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    private async Task<Commerce.Domain.ValueObjects.ShippingAddressSnapshot> TimelineFromAuctionApi(AuctionOrder order) =>
        (await _auctions.GetAuctionShipmentTimelineAsync(order.BuyerId, order.Id)).ShippingAddress;

    private async Task<Commerce.Domain.ValueObjects.ShippingAddressSnapshot> TimelineFromOrderApi(AuctionOrder order) =>
        (await _orders.GetShipmentTimelineAsync(order.BuyerId, order.Id)).ShippingAddress;

    [Fact]
    public async Task Both_timeline_APIs_read_the_stored_auction_address_field_by_field()
    {
        var stored = JsonSerializer.Serialize(
            new ShippingAddressDto("Nguyen Van A", "0912345678", "12 Nguyen Hue", "Hồ Chí Minh", "Quận 1", "Phường Bến Nghé", 1442, "20101", 202),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var order = await SeedAuctionOrderAsync(stored);

        foreach (var address in new[] { await TimelineFromAuctionApi(order), await TimelineFromOrderApi(order) })
        {
            address.ReceiverName.Should().Be("Nguyen Van A");
            address.ContactPhone.Should().Be("0912345678");
            address.Street.Should().Be("12 Nguyen Hue");
            address.Ward.Should().Be("Phường Bến Nghé");
            address.District.Should().Be("Quận 1");
            address.Province.Should().Be("Hồ Chí Minh");
            address.GhnDistrictId.Should().Be(1442);
            address.GhnWardCode.Should().Be("20101");
            address.ProvinceId.Should().Be(202);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"unrelated\":1}")]
    [InlineData("not json at all")]
    [InlineData("  ")]
    [InlineData(null)]
    public async Task An_address_that_is_empty_broken_or_old_text_uses_the_fallback_on_both_timeline_APIs(string? stored)
    {
        var order = await SeedAuctionOrderAsync(stored);

        foreach (var address in new[] { await TimelineFromAuctionApi(order), await TimelineFromOrderApi(order) })
        {
            address.ReceiverName.Should().Be("Auction Winner");
            address.ContactPhone.Should().Be("0901234567");
            address.Ward.Should().BeEmpty();
            address.Province.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Old_text_that_is_not_json_is_kept_as_the_street_of_the_fallback()
    {
        var order = await SeedAuctionOrderAsync("12 Nguyen Hue, Q1");

        (await TimelineFromAuctionApi(order)).Street.Should().Be("12 Nguyen Hue, Q1");
        (await TimelineFromOrderApi(order)).Street.Should().Be("12 Nguyen Hue, Q1");
    }

    [Fact]
    public async Task An_address_stored_in_the_snapshot_shape_is_still_read()
    {
        var order = await SeedAuctionOrderAsync("{\"receiverName\":\"Old Shape\",\"contactPhone\":\"0901\",\"street\":\"S\",\"ward\":\"W\",\"district\":\"D\",\"province\":\"P\",\"ghnDistrictId\":1442,\"ghnWardCode\":\"20101\"}");

        var address = await TimelineFromAuctionApi(order);

        address.ReceiverName.Should().Be("Old Shape");
        address.Street.Should().Be("S");
        address.Province.Should().Be("P");
    }

    [Fact]
    public async Task The_auction_address_submit_stores_the_GHN_names()
    {
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var order = await SeedAuctionOrderAsync(null, buyerId);
        order.SkuId = skuId;
        await _db.SaveChangesAsync();
        _catalog.Setup(c => c.GetSkuDetailsAsync(skuId, default))
            .ReturnsAsync(new CatalogProductCheckResult(skuId, order.ProductId, order.SellerId, "Item", 1000000m, true, StockQuantity: 1, ErrorReason: null, WeightGrams: 750));

        await _auctions.SubmitShippingAsync(order.Id, buyerId, new SubmitAuctionShippingRequest(
            ShippingAddress: new ShippingAddressDto("A", "0901234567", "1 Le Loi", "Typed province", "Typed district", "Typed ward", 1442, "20101", 202),
            ContactPhone: "0901234567"));

        var stored = JsonSerializer.Deserialize<ShippingAddressDto>((await _db.AuctionOrders.FindAsync(order.Id))!.ShippingAddress!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        stored.ProvinceName.Should().Be("Hồ Chí Minh");
        stored.DistrictName.Should().Be("Quận 1");
        stored.WardName.Should().Be("Phường Bến Nghé");
    }
}
