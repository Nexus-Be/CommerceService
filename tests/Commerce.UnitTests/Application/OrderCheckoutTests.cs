namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class OrderCheckoutTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<ILogger<OrderService>> _mockLogger = new();
    private readonly IDistributedCache _cache;

    public OrderCheckoutTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        // Default mock behaviors
        _mockFulfillmentClient
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(30000m);

        _mockFulfillmentClient
            .Setup(f => f.ReserveStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.GetWalletBalanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(500000m);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletPaymentResult(true, TransactionId: Guid.NewGuid().ToString(), BalanceAfter: 500000m));

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid skuId, CancellationToken ct) => new CatalogProductCheckResult(
                SkuId: skuId,
                ProductId: Guid.NewGuid(),
                SellerId: Guid.NewGuid(),
                ProductName: "Premium Test Product",
                UnitPrice: 100000m,
                IsPurchasable: true,
                StockQuantity: 100));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private OrderService CreateOrderService()
    {
        return new OrderService(
            _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _cache,
            _mockLogger.Object);
    }

    private async Task<Cart> SeedCartWithItemAsync(Guid buyerId, Guid skuId, Guid sellerId, decimal price, int quantity)
    {
        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuId, Guid.NewGuid(), sellerId, "Premium Test Product", price, true, 100));

        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = CartStatus.Active,
            ItemCount = 1,
            SubtotalAmount = price * quantity,
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
            ProductNameSnapshot = "Premium Test Product",
            Quantity = quantity,
            UnitPriceSnapshot = price,
            AddedAt = DateTimeOffset.UtcNow
        };

        cart.Items.Add(item);
        _dbContext.Carts.Add(cart);
        await _dbContext.SaveChangesAsync();
        return cart;
    }

    // =========================================================================
    // ORD-CHK-01: HAPPY PATH CHECKOUT (QUOTE -> CREATE ORDER -> CLEANUP CART)
    // =========================================================================

    [Fact]
    public async Task CheckoutOrder_HappyPath_ShouldCreateOrderAndCleanupCart()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var cart = await SeedCartWithItemAsync(buyerId, skuId, sellerId, 100000m, 2);
        var cartItemId = cart.Items.First().Id;

        var orderService = CreateOrderService();

        // Act 1: Quote Checkout
        var quoteRequest = new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { cartItemId },
            ShippingAddress: new ShippingAddressInput("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM", 1442, "20101"),
            ContactPhone: "0901234567",
            PaymentMethod: PaymentMethod.Wallet
        );

        var quoteResponse = await orderService.QuoteCheckoutAsync(buyerId, quoteRequest, CancellationToken.None);

        // Assert Quote
        quoteResponse.Should().NotBeNull();
        quoteResponse.GrandTotal.Should().Be(230000m); // (2 * 100,000) + 30,000 ship
        quoteResponse.Shops.Should().HaveCount(1);

        // Act 2: Create Order from QuoteId
        var createRequest = new CreateOrderRequest(
            QuoteId: quoteResponse.QuoteId,
            PaymentMethod: PaymentMethod.Wallet,
            ShippingAddress: "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            ContactPhone: "0901234567",
            Note: "Giao gio hanh chinh"
        );

        var orderResponse = await orderService.CreateOrderAsync(buyerId, createRequest, CancellationToken.None);

        // Assert Order Response
        orderResponse.Should().NotBeNull();
        orderResponse.TotalAmount.Should().Be(230000m);
        orderResponse.Status.Should().Be(OrderStatus.Paid.ToString()); // Thanh toan vi thanh cong
        orderResponse.SubOrderIds.Should().HaveCount(1);

        // Verify Database: Order + SubOrder + SubOrderItem exist
        var dbOrder = await _dbContext.Orders
            .Include(o => o.SubOrders)
                .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(o => o.Id == orderResponse.OrderId);

        dbOrder.Should().NotBeNull();
        dbOrder!.Status.Should().Be(OrderStatus.Paid);
        dbOrder.SubOrders.Should().HaveCount(1);
        dbOrder.SubOrders.First().Items.Should().HaveCount(1);

        // Verify External Interactions
        _mockFulfillmentClient.Verify(f => f.ReserveStockAsync(orderResponse.OrderId, It.IsAny<List<StockReservationItem>>(), 15, It.IsAny<CancellationToken>()), Times.Once);
        _mockWalletClient.Verify(w => w.PayOrderAsync(orderResponse.OrderId, 230000m, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockFulfillmentClient.Verify(f => f.CommitStockAsync(orderResponse.OrderId, It.IsAny<CancellationToken>()), Times.Once);

        // Verify Cart Cleanup: Món đã mua bị xóa khỏi Cart
        var remainingCart = await _dbContext.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.BuyerId == buyerId);
        remainingCart!.Items.Should().BeEmpty();
        remainingCart.ItemCount.Should().Be(0);
        remainingCart.SubtotalAmount.Should().Be(0m);
    }

    // =========================================================================
    // ORD-CHK-02: PRICE SNAPSHOT PERSISTENCE
    // =========================================================================

    [Fact]
    public async Task CheckoutOrder_ShouldPersistExactPriceSnapshot_InSubOrderItem()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var cart = await SeedCartWithItemAsync(buyerId, skuId, sellerId, 75000m, 3);
        var cartItemId = cart.Items.First().Id;

        var orderService = CreateOrderService();

        var quoteResponse = await orderService.QuoteCheckoutAsync(buyerId, new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { cartItemId },
            ShippingAddress: new ShippingAddressInput("Tran Van B", "0909999999", "456 Nguyen Hue", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone: "0909999999",
            PaymentMethod: PaymentMethod.Cod
        ), CancellationToken.None);

        // Act
        var orderResponse = await orderService.CreateOrderAsync(buyerId, new CreateOrderRequest(
            QuoteId: quoteResponse.QuoteId,
            PaymentMethod: PaymentMethod.Cod,
            ShippingAddress: "456 Nguyen Hue, Ben Nghe, Quan 1, TP.HCM",
            ContactPhone: "0909999999"
        ), CancellationToken.None);

        // Assert
        var subOrderItem = await _dbContext.SubOrderItems.FirstOrDefaultAsync(i => i.SkuId == skuId);
        subOrderItem.Should().NotBeNull();
        subOrderItem!.UnitPrice.Should().Be(75000m);
        subOrderItem.Quantity.Should().Be(3);
        subOrderItem.LineTotal.Should().Be(225000m); // 3 * 75,000
    }

    // =========================================================================
    // ORD-CHK-03: VALIDATION - CART EMPTY / ITEMS NOT FOUND
    // =========================================================================

    [Fact]
    public async Task QuoteCheckoutAsync_ShouldThrowException_WhenCartIsEmpty()
    {
        // Arrange
        var service = CreateOrderService();
        var request = new CheckoutQuoteRequest(
            CartItemIds: new List<Guid>(),
            ShippingAddress: new ShippingAddressInput("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone: "0901234567"
        );

        // Act
        var act = () => service.QuoteCheckoutAsync(Guid.NewGuid(), request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "CART_EMPTY");
    }

    // =========================================================================
    // ORD-CHK-04: OUT OF STOCK AT RESERVATION (CHK-02)
    // =========================================================================

    [Fact]
    public async Task CreateOrderAsync_ShouldAbortAndThrow_WhenInventoryReservationFails()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var cart = await SeedCartWithItemAsync(buyerId, skuId, sellerId, 100000m, 1);

        var orderService = CreateOrderService();

        var quoteResponse = await orderService.QuoteCheckoutAsync(buyerId, new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { cart.Items.First().Id },
            ShippingAddress: new ShippingAddressInput("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone: "0901234567"
        ), CancellationToken.None);

        // Giả lập kho báo không đủ hàng khi bấm Đặt hàng
        _mockFulfillmentClient
            .Setup(f => f.ReserveStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var act = () => orderService.CreateOrderAsync(buyerId, new CreateOrderRequest(
            QuoteId: quoteResponse.QuoteId,
            PaymentMethod: PaymentMethod.Cod,
            ShippingAddress: "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            ContactPhone: "0901234567"
        ), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "INSUFFICIENT_STOCK");

        // Đảm bảo không có Order nào được lưu vào Database
        var orderCount = await _dbContext.Orders.CountAsync();
        orderCount.Should().Be(0);
    }

    // =========================================================================
    // ORD-CHK-06: PAYMENT CRASH / FAULT TOLERANCE
    // =========================================================================

    [Fact]
    public async Task CreateOrderAsync_ShouldRemainPendingPayment_WhenWalletPaymentFailsOrTimesOut()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var cart = await SeedCartWithItemAsync(buyerId, skuId, sellerId, 50000m, 1);

        var orderService = CreateOrderService();

        var quoteResponse = await orderService.QuoteCheckoutAsync(buyerId, new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { cart.Items.First().Id },
            ShippingAddress: new ShippingAddressInput("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone: "0901234567"
        ), CancellationToken.None);

        // Giả lập Wallet Service gặp lỗi Transient (Timeout / 500)
        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletPaymentResult(false, ErrorCode: "CONNECTION_ERROR", ErrorMessage: "Wallet service timed out", IsTransientFailure: true));

        // Act
        var orderResponse = await orderService.CreateOrderAsync(buyerId, new CreateOrderRequest(
            QuoteId: quoteResponse.QuoteId,
            PaymentMethod: PaymentMethod.Wallet,
            ShippingAddress: "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            ContactPhone: "0901234567"
        ), CancellationToken.None);

        // Assert: Không crash, giữ nguyên PendingPayment để đối soát
        orderResponse.Status.Should().Be(OrderStatus.PendingPayment.ToString());

        var dbOrder = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderResponse.OrderId);
        dbOrder!.Status.Should().Be(OrderStatus.PendingPayment);

        // Không được gọi commit kho vì chưa thanh toán
        _mockFulfillmentClient.Verify(f => f.CommitStockAsync(orderResponse.OrderId, It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================
    // ORD-CHK-07: IDEMPOTENCY / ANTI-DOUBLE SUBMIT
    // =========================================================================

    [Fact]
    public async Task CreateOrderAsync_ShouldPreventDoubleSubmit_WhenSameQuoteIdCalledTwice()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var cart = await SeedCartWithItemAsync(buyerId, skuId, sellerId, 100000m, 1);

        var orderService = CreateOrderService();

        var quoteResponse = await orderService.QuoteCheckoutAsync(buyerId, new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { cart.Items.First().Id },
            ShippingAddress: new ShippingAddressInput("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone: "0901234567"
        ), CancellationToken.None);

        var createRequest = new CreateOrderRequest(
            QuoteId: quoteResponse.QuoteId,
            PaymentMethod: PaymentMethod.Cod,
            ShippingAddress: "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            ContactPhone: "0901234567"
        );

        // Act 1: Lần 1 đặt hàng thành công
        var firstOrder = await orderService.CreateOrderAsync(buyerId, createRequest, CancellationToken.None);
        firstOrder.Should().NotBeNull();

        // Act 2: Lần 2 cố tình gửi lại cùng QuoteId (User double-click hoặc retry)
        var secondAct = () => orderService.CreateOrderAsync(buyerId, createRequest, CancellationToken.None);

        // Assert: Bị chặn ngay lập tức vì QuoteId đã bị consume khỏi Cache
        await secondAct.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "QUOTE_EXPIRED");

        // Database chỉ có DUY NHẤT 1 Order được tạo
        var totalOrders = await _dbContext.Orders.CountAsync();
        totalOrders.Should().Be(1);
    }

    // =========================================================================
    // ORD-CHK-08: WALLET INTERNAL CONTRACT & ERROR HANDLING
    // =========================================================================

    [Fact]
    public async Task CreateOrderAsync_ShouldCallPayOrderAsync_WithOrderIdAndServerCalculatedAmount()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var cart = await SeedCartWithItemAsync(buyerId, skuId, sellerId, 100000m, 1);

        var orderService = CreateOrderService();

        var quoteResponse = await orderService.QuoteCheckoutAsync(buyerId, new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { cart.Items.First().Id },
            ShippingAddress: new ShippingAddressInput("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone: "0901234567"
        ), CancellationToken.None);

        // Act
        var orderResponse = await orderService.CreateOrderAsync(buyerId, new CreateOrderRequest(
            QuoteId: quoteResponse.QuoteId,
            PaymentMethod: PaymentMethod.Wallet,
            ShippingAddress: "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            ContactPhone: "0901234567"
        ), CancellationToken.None);

        // Assert: Gọi PayOrderAsync với đúng orderId và tổng tiền
        _mockWalletClient.Verify(w => w.PayOrderAsync(
            orderResponse.OrderId,
            orderResponse.TotalAmount,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldRemainPendingPayment_WhenWalletHasInsufficientBalance()
    {
        // Arrange
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var cart = await SeedCartWithItemAsync(buyerId, skuId, Guid.NewGuid(), 100000m, 1);
        var orderService = CreateOrderService();

        var quoteResponse = await orderService.QuoteCheckoutAsync(buyerId, new CheckoutQuoteRequest(
            CartItemIds: new List<Guid> { cart.Items.First().Id },
            ShippingAddress: new ShippingAddressInput("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            ContactPhone: "0901234567"
        ), CancellationToken.None);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletPaymentResult(false, ErrorCode: "INSUFFICIENT_BALANCE", ErrorMessage: "Insufficient wallet balance"));

        // Act
        var orderResponse = await orderService.CreateOrderAsync(buyerId, new CreateOrderRequest(
            QuoteId: quoteResponse.QuoteId,
            PaymentMethod: PaymentMethod.Wallet,
            ShippingAddress: "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            ContactPhone: "0901234567"
        ), CancellationToken.None);

        // Assert
        orderResponse.Status.Should().Be(OrderStatus.PendingPayment.ToString());
        _mockFulfillmentClient.Verify(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
