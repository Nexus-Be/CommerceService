namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class OrderRequoteTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<OrderService>> _mockLogger = new();
    private readonly IDistributedCache _cache;
    private readonly OrderService _orderService;

    public OrderRequoteTests()
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
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<(Guid BuyerId, Guid QuoteId, CachedQuote CachedQuote)> SeedCachedQuoteAsync()
    {
        var buyerId = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        var shopQuote = new ShopOrderQuote(
            SellerId: sellerId,
            ShopName: "Shop A",
            Items: new List<ShopQuoteItem>
            {
                new(Guid.NewGuid(), Guid.NewGuid(), skuId, "Item 1", 1, 100000m, 100000m)
            },
            ItemsAmount: 100000m,
            ProductDiscountAmount: 0m,
            ShopDiscountAmount: 0m,
            SystemDiscountAmount: 0m,
            DiscountAmount: 0m,
            ShippingFeeAmount: 30000m,
            TotalAmount: 130000m
        );

        var quoteResponse = new CheckoutQuoteResponse(
            QuoteId: quoteId,
            GrandTotal: 130000m,
            WalletBalance: 500000m,
            Shops: new List<ShopOrderQuote> { shopQuote },
            AppliedVouchers: new List<string>(),
            Warnings: null,
            ExpiredAt: DateTimeOffset.UtcNow.AddMinutes(15)
        );

        var request = new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { Guid.NewGuid() },
            ShippingAddress: new ShippingAddressInput("Buyer", "0901234567", "Old Street", "Ward 1", "District 1", "TP.HCM", 1442, "20101"),
            ContactPhone: "0901234567"
        );

        var cachedQuote = new CachedQuote
        {
            QuoteId = quoteId,
            BuyerId = buyerId,
            Request = request,
            Response = quoteResponse,
            ExpiredAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var quoteKey = $"checkout_quote:{quoteId}";
        await _cache.SetStringAsync(quoteKey, JsonSerializer.Serialize(cachedQuote));

        return (buyerId, quoteId, cachedQuote);
    }

    [Fact]
    public async Task RecalculateQuoteAsync_WithNewAddress_UpdatesShippingFeeAndCache()
    {
        var (buyerId, quoteId, _) = await SeedCachedQuoteAsync();
        var newAddressId = Guid.NewGuid();
        var newAddressDto = new BuyerAddressDto(
            Id: newAddressId,
            RecipientName: "New Recipient",
            Phone: "0918888888",
            Street: "999 Ba Thang Hai",
            Ward: "Phuong 14",
            District: "Quan 10",
            Province: "TP.HCM",
            GhnDistrictId: 1450,
            GhnWardCode: "21001",
            FullAddressText: "999 Ba Thang Hai, Phuong 14, Quan 10, TP.HCM",
            IsDefault: false
        );

        _mockUserClient
            .Setup(u => u.GetBuyerAddressAsync(buyerId, newAddressId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAddressDto);

        // New shipping fee calculated as 40,000 VND
        _mockFulfillmentClient
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(40000m);

        var requoteRequest = new RecalculateQuoteRequest(AddressId: newAddressId);

        var response = await _orderService.RecalculateQuoteAsync(buyerId, quoteId, requoteRequest);

        response.Should().NotBeNull();
        response.Shops.Should().HaveCount(1);
        response.Shops[0].ShippingFeeAmount.Should().Be(40000m);
        response.GrandTotal.Should().Be(140000m);

        // Verify updated in cache
        var quoteKey = $"checkout_quote:{quoteId}";
        var updatedJson = await _cache.GetStringAsync(quoteKey);
        updatedJson.Should().NotBeNull();
        var updatedCache = JsonSerializer.Deserialize<CachedQuote>(updatedJson!);
        updatedCache!.Response.GrandTotal.Should().Be(140000m);
        updatedCache.Request.ShippingAddress?.Street.Should().Be("999 Ba Thang Hai");
    }

    [Fact]
    public async Task RecalculateQuoteAsync_WhenQuoteNotFound_ThrowsQuoteNotFound()
    {
        var buyerId = Guid.NewGuid();
        var nonExistentQuoteId = Guid.NewGuid();
        var requoteRequest = new RecalculateQuoteRequest(AddressId: Guid.NewGuid());

        var act = () => _orderService.RecalculateQuoteAsync(buyerId, nonExistentQuoteId, requoteRequest);

        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("QUOTE_NOT_FOUND");
        ex.Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task RecalculateQuoteAsync_WhenQuoteBelongsToAnotherBuyer_ThrowsForbidden()
    {
        var (buyerId, quoteId, _) = await SeedCachedQuoteAsync();
        var anotherBuyerId = Guid.NewGuid();
        var requoteRequest = new RecalculateQuoteRequest(AddressId: Guid.NewGuid());

        var act = () => _orderService.RecalculateQuoteAsync(anotherBuyerId, quoteId, requoteRequest);

        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("FORBIDDEN");
        ex.Which.StatusCode.Should().Be(403);
    }
}
