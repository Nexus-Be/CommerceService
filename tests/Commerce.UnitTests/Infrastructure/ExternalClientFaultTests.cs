namespace Commerce.UnitTests.Infrastructure;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Cart;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class ExternalClientFaultTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<ILogger<OrderService>> _mockOrderLogger = new();
    private readonly IDistributedCache _cache;

    public ExternalClientFaultTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    // =========================================================================
    // FAULT-01: CATALOG CLIENT TIMEOUT / 500 ON ADD ITEM
    // =========================================================================

    [Fact]
    public async Task AddItemAsync_ShouldNotCreateCartOrItems_WhenCatalogClientThrows()
    {
        // Arrange: Catalog bi sập hoặc timeout
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Catalog Service is down (503 Service Unavailable)"));

        var cartService = new CartService(_dbContext, _mockCatalogClient.Object, _mockFulfillmentClient.Object);

        // Act
        var act = () => cartService.AddItemAsync(buyerId, new AddCartItemRequest(skuId, 1), CancellationToken.None);

        // Assert: Ném lỗi và tuyệt đối không tạo bản ghi rác trong DB
        await act.Should().ThrowAsync<HttpRequestException>();

        var dbCart = await _dbContext.Carts.FirstOrDefaultAsync(c => c.BuyerId == buyerId);
        dbCart.Should().BeNull();
    }

    // =========================================================================
    // FAULT-02: FULFILLMENT CLIENT ERROR ON STOCK CHECK
    // =========================================================================

    [Fact]
    public async Task AddItemAsync_ShouldNotMutateCart_WhenFulfillmentStockCheckThrows()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuId, Guid.NewGuid(), Guid.NewGuid(), "Test Item", 50000m, true, 100));

        _mockFulfillmentClient
            .Setup(f => f.CheckStockAvailabilityAsync(skuId, 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Fulfillment inventory check timed out"));

        var cartService = new CartService(_dbContext, _mockCatalogClient.Object, _mockFulfillmentClient.Object);

        // Act
        var act = () => cartService.AddItemAsync(buyerId, new AddCartItemRequest(skuId, 1), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<TimeoutException>();

        var dbCart = await _dbContext.Carts.FirstOrDefaultAsync(c => c.BuyerId == buyerId);
        dbCart.Should().BeNull();
    }

    // =========================================================================
    // FAULT-03: FULFILLMENT SHIPPING QUOTE FAULT FALLBACK
    // =========================================================================

    [Fact]
    public async Task QuoteCheckoutAsync_ShouldFallbackToStandardShippingFee_WhenFulfillmentShippingThrows()
    {
        // Arrange: Fulfillment tính ship GHN bị lỗi
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = CartStatus.Active,
            ItemCount = 1,
            SubtotalAmount = 100000m,
            LastActivityAt = DateTimeOffset.UtcNow,
            RowVersion = 1
        };

        var item = new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            ProductId = Guid.NewGuid(),
            SkuId = skuId,
            SellerId = sellerId,
            ProductNameSnapshot = "Product 1",
            Quantity = 1,
            UnitPriceSnapshot = 100000m,
            AddedAt = DateTimeOffset.UtcNow
        };

        cart.Items.Add(item);
        _dbContext.Carts.Add(cart);
        await _dbContext.SaveChangesAsync();

        _mockFulfillmentClient
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("GHN API connection timeout"));

        var orderService = new OrderService(
            _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _cache,
            _mockOrderLogger.Object);

        var quoteRequest = new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { item.Id },
            ShippingAddress: new ShippingAddressInput("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM", 1442, "20101"),
            ContactPhone: "0901234567"
        );

        // Act
        var quoteResponse = await orderService.QuoteCheckoutAsync(buyerId, quoteRequest, CancellationToken.None);

        // Assert: Không crash, fallback về cước chuẩn 30,000 VND
        quoteResponse.Should().NotBeNull();
        quoteResponse.Shops.Should().HaveCount(1);
        quoteResponse.Shops[0].ShippingFeeAmount.Should().Be(30000m);
        quoteResponse.GrandTotal.Should().Be(130000m); // 100,000 + 30,000
    }

    // =========================================================================
    // FAULT-04: WALLET GET BALANCE ERROR FALLBACK
    // =========================================================================

    [Fact]
    public async Task QuoteCheckoutAsync_ShouldFallbackToZeroBalance_WhenWalletServiceThrows()
    {
        // Arrange: Wallet service get balance bi loi
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = CartStatus.Active,
            ItemCount = 1,
            SubtotalAmount = 50000m,
            LastActivityAt = DateTimeOffset.UtcNow,
            RowVersion = 1
        };

        var item = new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            ProductId = Guid.NewGuid(),
            SkuId = skuId,
            SellerId = Guid.NewGuid(),
            ProductNameSnapshot = "Product 1",
            Quantity = 1,
            UnitPriceSnapshot = 50000m,
            AddedAt = DateTimeOffset.UtcNow
        };

        cart.Items.Add(item);
        _dbContext.Carts.Add(cart);
        await _dbContext.SaveChangesAsync();

        _mockWalletClient
            .Setup(w => w.GetWalletBalanceAsync(buyerId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Wallet service unreachable"));

        var orderService = new OrderService(
            _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _cache,
            _mockOrderLogger.Object);

        var quoteRequest = new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { item.Id },
            ShippingAddress: new ShippingAddressInput("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone: "0901234567"
        );

        // Act
        var quoteResponse = await orderService.QuoteCheckoutAsync(buyerId, quoteRequest, CancellationToken.None);

        // Assert: Khong lam dut gay luong quote, fallback ve 0m
        quoteResponse.Should().NotBeNull();
        quoteResponse.WalletBalance.Should().Be(0m);
    }

    // =========================================================================
    // FAULT-06: SHIPMENT CREATION RESILIENCE ON SELLER ACCEPT
    // =========================================================================

    [Fact]
    public async Task SellerAcceptSubOrderAsync_ShouldSucceed_EvenWhenFulfillmentShipmentCreationThrows()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 100000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaymentAttemptCount = 1
        };

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = $"SUB-{DateTime.UtcNow:yyyyMMdd}-001",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone = "0901234567",
            ItemsAmount = 100000m,
            TotalAmount = 100000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaidAt = DateTimeOffset.UtcNow
        };

        order.SubOrders.Add(subOrder);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Fulfillment bi timeout khi tao van don GHN
        _mockFulfillmentClient
            .Setup(f => f.CreateShipmentAsync(subOrderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("GHN create shipment gateway timed out"));

        var orderService = new OrderService(
            _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _cache,
            _mockOrderLogger.Object);

        // Act
        await orderService.SellerAcceptSubOrderAsync(sellerId, orderId, new SellerAcceptOrderRequest(subOrderId), CancellationToken.None);

        // Assert: Nghiệp vụ xác nhận đơn con của Seller vẫn thành công (Confirmed), không bị gián đoạn vì lỗi đối tác thứ 3
        var updatedSubOrder = await _dbContext.SubOrders.FirstOrDefaultAsync(s => s.Id == subOrderId);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Confirmed);
        updatedSubOrder.SellerAcceptedAt.Should().NotBeNull();
    }
}
