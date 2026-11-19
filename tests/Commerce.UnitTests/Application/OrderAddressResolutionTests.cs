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
using Commerce.Application.Services.AuctionOrder;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class OrderAddressResolutionTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IAuctionClient> _mockAuctionClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ILogger<OrderService>> _mockOrderLogger = new();
    private readonly Mock<ILogger<AuctionOrderService>> _mockAuctionLogger = new();
    private readonly IDistributedCache _cache;
    private readonly OrderService _orderService;
    private readonly AuctionOrderService _auctionOrderService;

    public OrderAddressResolutionTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        _mockFulfillmentClient
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(25000m);

        _mockWalletClient
            .Setup(w => w.GetWalletBalanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1000000m);

        _orderService = new OrderService(
            _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockUserClient.Object,
            _cache,
            _mockOrderLogger.Object
        );

        _auctionOrderService = new AuctionOrderService(
            _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockAuctionClient.Object,
            _mockCatalogClient.Object,
            _mockUserClient.Object,
            _mockAuctionLogger.Object
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<(Guid BuyerId, Guid CartItemId)> SeedCartAsync()
    {
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = CartStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var cartItem = new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            SellerId = sellerId,
            ProductId = Guid.NewGuid(),
            SkuId = Guid.NewGuid(),
            ProductNameSnapshot = "Test Product",
            UnitPriceSnapshot = 50000m,
            Quantity = 2,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        cart.Items.Add(cartItem);
        _dbContext.Carts.Add(cart);
        await _dbContext.SaveChangesAsync();
        return (buyerId, cartItem.Id);
    }

    [Fact]
    public async Task QuoteCheckoutAsync_WithAddressId_ResolvesAddressFromUserService()
    {
        var (buyerId, cartItemId) = await SeedCartAsync();
        var addressId = Guid.NewGuid();
        var addressDto = new BuyerAddressDto(
            Id: addressId,
            RecipientName: "Nguyen Van A",
            Phone: "0901234567",
            Street: "123 Le Loi",
            Ward: "Ben Nghe",
            District: "Quan 1",
            Province: "TP.HCM",
            GhnDistrictId: 1442,
            GhnWardCode: "20101",
            FullAddressText: "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            IsDefault: true
        );

        _mockUserClient
            .Setup(u => u.GetBuyerAddressAsync(buyerId, addressId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(addressDto);

        var request = new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { cartItemId },
            AddressId: addressId
        );

        var response = await _orderService.QuoteCheckoutAsync(buyerId, request);

        response.Should().NotBeNull();
        response.Shops.Should().HaveCount(1);
        response.Shops[0].ShippingFeeAmount.Should().Be(25000m);
        _mockUserClient.Verify(u => u.GetBuyerAddressAsync(buyerId, addressId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QuoteCheckoutAsync_WithNullAddressAndNoAddressId_FallsBackToDefaultAddress()
    {
        var (buyerId, cartItemId) = await SeedCartAsync();
        var defaultAddressDto = new BuyerAddressDto(
            Id: Guid.NewGuid(),
            RecipientName: "Default Recipient",
            Phone: "0987654321",
            Street: "456 Tran Hung Dao",
            Ward: "Ward 1",
            District: "District 5",
            Province: "TP.HCM",
            GhnDistrictId: 1445,
            GhnWardCode: "20501",
            FullAddressText: "456 Tran Hung Dao, Ward 1, District 5, TP.HCM",
            IsDefault: true
        );

        _mockUserClient
            .Setup(u => u.GetBuyerDefaultAddressAsync(buyerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultAddressDto);

        var request = new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { cartItemId }
        );

        var response = await _orderService.QuoteCheckoutAsync(buyerId, request);

        response.Should().NotBeNull();
        _mockUserClient.Verify(u => u.GetBuyerDefaultAddressAsync(buyerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QuoteCheckoutAsync_WithNullAddressAndNoDefault_ThrowsAddressRequired()
    {
        var (buyerId, cartItemId) = await SeedCartAsync();

        _mockUserClient
            .Setup(u => u.GetBuyerDefaultAddressAsync(buyerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BuyerAddressDto?)null);

        var request = new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { cartItemId }
        );

        var act = () => _orderService.QuoteCheckoutAsync(buyerId, request);

        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("ADDRESS_REQUIRED");
    }

    [Fact]
    public async Task QuoteCheckoutAsync_WithNonExistentAddressId_ThrowsAddressNotFound()
    {
        var (buyerId, cartItemId) = await SeedCartAsync();
        var addressId = Guid.NewGuid();

        _mockUserClient
            .Setup(u => u.GetBuyerAddressAsync(buyerId, addressId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BuyerAddressDto?)null);

        var request = new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { cartItemId },
            AddressId: addressId
        );

        var act = () => _orderService.QuoteCheckoutAsync(buyerId, request);

        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("ADDRESS_NOT_FOUND");
        ex.Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task QuoteCheckoutAsync_WithMissingGhnCodes_ThrowsInvalidShippingAddress()
    {
        var (buyerId, cartItemId) = await SeedCartAsync();
        var invalidAddress = new ShippingAddressInput(
            ReceiverName: "Test",
            ContactPhone: "0900000000",
            Street: "Street",
            Ward: "Ward",
            District: "District",
            Province: "Province",
            GhnDistrictId: null,
            GhnWardCode: null
        );

        var request = new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { cartItemId },
            ShippingAddress: invalidAddress
        );

        var act = () => _orderService.QuoteCheckoutAsync(buyerId, request);

        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_SHIPPING_ADDRESS");
    }

    [Fact]
    public async Task SubmitShippingAsync_WithAddressId_ResolvesAddressAndCalculatesFee()
    {
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var auctionOrder = new AuctionOrder
        {
            Id = Guid.NewGuid(),
            OrderNo = "AO-20261119-001",
            AuctionId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = sellerId,
            ProductId = productId,
            SkuId = skuId,
            ProductNameSnapshot = "Vintage Camera",
            WinningAmount = 1000000m,
            DepositAmount = 100000m,
            RemainingAmount = 900000m,
            SellerReceiveAmount = 855000m,
            PlatformFeeAmount = 45000m,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddHours(2),
            Status = SubOrderStatus.PendingPayment
        };
        _dbContext.AuctionOrders.Add(auctionOrder);
        await _dbContext.SaveChangesAsync();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(
                skuId,
                productId,
                sellerId,
                "Vintage Camera",
                1000000m,
                true,
                StockQuantity: 1,
                ErrorReason: null,
                WeightGrams: 500
            ));

        var addressId = Guid.NewGuid();
        var addressDto = new BuyerAddressDto(
            Id: addressId,
            RecipientName: "Auction Buyer",
            Phone: "0912345678",
            Street: "789 Nguyen Trai",
            Ward: "Phuong 3",
            District: "Quan 5",
            Province: "TP.HCM",
            GhnDistrictId: 1446,
            GhnWardCode: "20601",
            FullAddressText: "789 Nguyen Trai, Phuong 3, Quan 5, TP.HCM",
            IsDefault: false
        );

        _mockUserClient
            .Setup(u => u.GetBuyerAddressAsync(buyerId, addressId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(addressDto);

        var request = new SubmitAuctionShippingRequest(AddressId: addressId);

        var response = await _auctionOrderService.SubmitShippingAsync(auctionOrder.Id, buyerId, request);

        response.Should().NotBeNull();
        response.ShippingFeeAmount.Should().Be(25000m);
        response.TotalAmount.Should().Be(925000m);
        _mockUserClient.Verify(u => u.GetBuyerAddressAsync(buyerId, addressId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
