namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Cart;
using Commerce.Application.Services.Discount;
using Commerce.Application.Services.Order;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// At checkout a shop with no usable warehouse is refused with a clear code. A real outage of the fulfillment service still falls back to the
/// default shipping rate, as before.
/// </summary>
public sealed class CheckoutWarehouseTests : IDisposable
{
    private readonly TestCommerceDbContext _db = TestDbContextFactory.CreateInMemoryDbContext();
    private readonly Mock<IFulfillmentClient> _fulfillment = FulfillmentMocks.WithConfirmedLocations();
    private readonly Mock<ICatalogClient> _catalog = new();
    private readonly Guid _buyer = Guid.NewGuid();
    private readonly Guid _seller = Guid.NewGuid();
    private readonly Guid _sku = Guid.NewGuid();

    public CheckoutWarehouseTests()
    {
        _catalog.Setup(c => c.GetSkuDetailsAsync(_sku, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(_sku, Guid.NewGuid(), _seller, "Item", 100000m, true, 50));
        _catalog.Setup(c => c.GetSkuBatchDetailsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CatalogProductCheckResult>
            {
                { _sku, new CatalogProductCheckResult(_sku, Guid.NewGuid(), _seller, "Item", 100000m, true, 50) }
            });
        _fulfillment.Setup(f => f.CheckStockAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _fulfillment.Setup(f => f.BatchCheckStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, SkuStockCheckResult> { { _sku, new SkuStockCheckResult(_sku, true, 50, null) } });
        var wallet = new Mock<IWalletClient>();
        wallet.Setup(w => w.GetWalletBalanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000m);
        _wallet = wallet;
    }

    private readonly Mock<IWalletClient> _wallet;

    public void Dispose() => _db.Dispose();

    private async Task<CheckoutQuoteRequest> RequestAsync()
    {
        var cartService = new CartService(_db, _catalog.Object, _fulfillment.Object);
        await cartService.AddItemAsync(_buyer, new AddCartItemRequest(_sku, 1, _seller), CancellationToken.None);
        var cart = await cartService.GetOrCreateActiveCartAsync(_buyer, CancellationToken.None);
        return new CheckoutQuoteRequest(
            cart.Items.Select(i => i.Id).ToList(),
            new ShippingAddressInput("Buyer", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM", 1442, "20101", ProvinceId: 202),
            "0901234567",
            PaymentMethod.Wallet);
    }

    private OrderService NewOrderService() => new(
        _db, _wallet.Object, _fulfillment.Object, _catalog.Object, new Mock<IUserClient>().Object,
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<OrderService>.Instance,
        new VoucherDiscountCalculator(_db, new VoucherCalculationEngine()), new VoucherLifecycleService(_db));

    [Theory]
    [InlineData("SELLER_WAREHOUSE_MISSING")]
    [InlineData("SELLER_WAREHOUSE_INCOMPLETE")]
    public async Task Quote_ForAShopWithoutAUsableWarehouse_IsRefusedWithThatCode(string code)
    {
        _fulfillment.Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CommerceDomainException(code, "The shop cannot ship.", 422));
        var request = await RequestAsync();

        var act = () => NewOrderService().QuoteCheckoutAsync(_buyer, request, CancellationToken.None);

        (await act.Should().ThrowAsync<CommerceDomainException>()).Which.ErrorCode.Should().Be(code);
    }

    [Fact]
    public async Task Quote_WhenFulfillmentIsReallyDown_StillFallsBackToTheDefaultShippingRate()
    {
        _fulfillment.Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CommerceDomainException("FULFILLMENT_SERVICE_UNAVAILABLE", "down", 503));
        var request = await RequestAsync();

        var quote = await NewOrderService().QuoteCheckoutAsync(_buyer, request, CancellationToken.None);

        quote.GrandTotal.Should().Be(130000m); // 100,000 goods + the 30,000 default rate
    }
}
