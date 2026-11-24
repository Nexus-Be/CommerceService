namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;
using Commerce.Application.Services.Order;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class OrderServiceTests
{
    private readonly Mock<ICommerceDbContext> _mockDbContext = new();
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IDistributedCache> _mockCache = new();
    private readonly Mock<ILogger<OrderService>> _mockLogger = new();

    private OrderService CreateService()
    {
        _mockFulfillmentClient
            .Setup(f => f.ReserveStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletPaymentResult.Success("tx-price-drop-123"));

        _mockFulfillmentClient
            .Setup(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.ReleaseStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.ReleaseSubOrderStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.GetWalletBalanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(500000m);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletPaymentResult.Success("tx-default-123"));

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletPaymentResult.Success("tx-default-123"));

        return new OrderService(
            _mockDbContext.Object,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task QuoteCheckoutAsync_ShouldThrowException_WhenCartItemIdsIsEmpty()
    {
        var service = CreateService();
        var request = new CheckoutQuoteRequest(
            new List<Guid>(),
            new ShippingAddressInput("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            "0901234567"
        );

        var act = () => service.QuoteCheckoutAsync(Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "CART_EMPTY");
    }

    [Fact]
    public async Task QuoteCheckoutAsync_ShouldThrowException_WhenShippingAddressIsInvalid()
    {
        var service = CreateService();
        var request = new CheckoutQuoteRequest(
            new List<Guid> { Guid.NewGuid() },
            new ShippingAddressInput("", "", "", "", "", ""),
            ""
        );

        var act = () => service.QuoteCheckoutAsync(Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "VAL_INVALID_INPUT");
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldThrowException_WhenQuoteIdExpiredOrNotFound()
    {
        var service = CreateService();
        var request = new CreateOrderRequest(
            Guid.NewGuid(),
            PaymentMethod.Wallet,
            "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            "0901234567"
        );

        var act = () => service.CreateOrderAsync(Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "QUOTE_EXPIRED");
    }

    [Fact]
    public async Task QuoteCheckoutAsync_ShouldThrowException_WhenCartItemIdsIsNull()
    {
        var service = CreateService();
        var request = new CheckoutQuoteRequest(
            null!,
            new ShippingAddressInput("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM"),
            "0901234567"
        );

        var act = () => service.QuoteCheckoutAsync(Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "CART_EMPTY");
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldRollbackStockAndRefundWallet_WhenDatabaseSaveFails()
    {
        // Arrange: Cấu hình DbContext ném lỗi khi gọi SaveChangesAsync
        var options = new DbContextOptionsBuilder<TestCommerceDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var failingDbContext = new FailingCommerceDbContext(options);

        var quoteId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var cachedQuote = new CachedQuote
        {
            QuoteId = quoteId,
            BuyerId = buyerId,
            Request = new CheckoutQuoteRequest(
                new List<Guid> { Guid.NewGuid() },
                new ShippingAddressInput("Receiver", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101"),
                "0901234567"
            ),
            Response = new CheckoutQuoteResponse(
                quoteId,
                100000m,
                500000m,
                new List<ShopOrderQuote>
                {
                    new ShopOrderQuote(
                        sellerId,
                        "Shop A",
                        new List<ShopQuoteItem>
                        {
                            new ShopQuoteItem(Guid.NewGuid(), Guid.NewGuid(), skuId, "Product 1", 1, 100000m, 100000m)
                        },
                        100000m,
                        0m, 0m, 0m, 0m,
                        0m,
                        100000m
                    )
                },
                new List<string>(),
                null,
                DateTimeOffset.UtcNow.AddMinutes(15)
            ),
            ExpiredAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var quoteJsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cachedQuote));
        _mockCache
            .Setup(c => c.GetAsync($"checkout_quote:{quoteId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(quoteJsonBytes);

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuId, Guid.NewGuid(), sellerId, "Product 1", 100000m, true, 50));

        _mockFulfillmentClient
            .Setup(f => f.ReserveStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletPaymentResult.Success("tx-price-drop-123"));

        _mockFulfillmentClient
            .Setup(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.ReleaseStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), 100000m, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletPaymentResult.Success("tx-success-123"));

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), 100000m, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletPaymentResult.Success("tx-success-123"));

        _mockWalletClient
            .Setup(w => w.RefundOrderPaymentAsync(It.IsAny<Guid>(), buyerId.ToString(), 100000m, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new OrderService(
            failingDbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var request = new CreateOrderRequest(
            quoteId,
            PaymentMethod.Wallet,
            "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            "0901234567"
        );

        // Act
        var act = () => service.CreateOrderAsync(buyerId, request, CancellationToken.None);

        // Assert: Ném lỗi ORDER_CREATION_FAILED (500)
        await act.Should().ThrowAsync<CommerceDomainException>()
            .Where(e => e.ErrorCode == "ORDER_CREATION_FAILED");

        // Verify: Kho được giải phóng (Rollback Inventory)
        _mockFulfillmentClient.Verify(
            f => f.ReleaseStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify: Ví được hoàn tiền 100% với Idempotency Key (Rollback Wallet)
        _mockWalletClient.Verify(
            w => w.RefundOrderPaymentAsync(
                It.IsAny<Guid>(),
                buyerId.ToString(),
                100000m,
                It.IsAny<string>(),
                It.Is<string>(k => k.StartsWith("compensation-db-")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldAutoApplyPriceDrop_WhenItemPriceDecreases()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var quoteId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var cachedQuote = new CachedQuote
        {
            QuoteId = quoteId,
            BuyerId = buyerId,
            Request = new CheckoutQuoteRequest(
                new List<Guid> { Guid.NewGuid() },
                new ShippingAddressInput("Receiver", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101"),
                "0901234567"
            ),
            Response = new CheckoutQuoteResponse(
                quoteId,
                100000m,
                500000m,
                new List<ShopOrderQuote>
                {
                    new ShopOrderQuote(
                        sellerId,
                        "Shop A",
                        new List<ShopQuoteItem>
                        {
                            new ShopQuoteItem(Guid.NewGuid(), Guid.NewGuid(), skuId, "Product 1", 1, 100000m, 100000m)
                        },
                        100000m,
                        0m, 0m, 0m, 0m,
                        0m,
                        100000m
                    )
                },
                new List<string>(),
                null,
                DateTimeOffset.UtcNow.AddMinutes(15)
            ),
            ExpiredAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var quoteJsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cachedQuote));
        _mockCache
            .Setup(c => c.GetAsync($"checkout_quote:{quoteId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(quoteJsonBytes);

        // Catalog giảm giá xuống 80.000 VNĐ
        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuId, Guid.NewGuid(), sellerId, "Product 1", 80000m, true, 50));

        _mockFulfillmentClient
            .Setup(f => f.ReserveStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletPaymentResult.Success("tx-price-drop-123"));

        _mockFulfillmentClient
            .Setup(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var request = new CreateOrderRequest(
            quoteId,
            PaymentMethod.Wallet,
            "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            "0901234567"
        );

        // Act
        var result = await service.CreateOrderAsync(buyerId, request, CancellationToken.None);

        // Assert: Tổng tiền tính theo giá rẻ hơn (80.000) và có danh sách appliedPriceDrops
        result.TotalAmount.Should().Be(80000m);
        result.AppliedPriceDrops.Should().NotBeNull();
        result.AppliedPriceDrops!.Should().HaveCount(1);
        result.AppliedPriceDrops![0].SavedAmount.Should().Be(20000m);
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldBlockAndUpdateSnapshot_WhenAnyItemPriceIncreases()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var quoteId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var cachedQuote = new CachedQuote
        {
            QuoteId = quoteId,
            BuyerId = buyerId,
            Request = new CheckoutQuoteRequest(
                new List<Guid> { Guid.NewGuid() },
                new ShippingAddressInput("Receiver", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101"),
                "0901234567"
            ),
            Response = new CheckoutQuoteResponse(
                quoteId,
                100000m,
                500000m,
                new List<ShopOrderQuote>
                {
                    new ShopOrderQuote(
                        sellerId,
                        "Shop A",
                        new List<ShopQuoteItem>
                        {
                            new ShopQuoteItem(Guid.NewGuid(), Guid.NewGuid(), skuId, "Product 1", 1, 100000m, 100000m)
                        },
                        100000m,
                        0m, 0m, 0m, 0m,
                        0m,
                        100000m
                    )
                },
                new List<string>(),
                null,
                DateTimeOffset.UtcNow.AddMinutes(15)
            ),
            ExpiredAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var quoteJsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cachedQuote));
        _mockCache
            .Setup(c => c.GetAsync($"checkout_quote:{quoteId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(quoteJsonBytes);

        // Catalog tăng giá lên 120.000 VNĐ
        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuId, Guid.NewGuid(), sellerId, "Product 1", 120000m, true, 50));

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var request = new CreateOrderRequest(
            quoteId,
            PaymentMethod.Wallet,
            "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            "0901234567"
        );

        // Act
        var act = () => service.CreateOrderAsync(buyerId, request, CancellationToken.None);

        // Assert: Chặn lại với lỗi PRICE_CHANGED (409)
        var exception = await act.Should().ThrowAsync<CommerceDomainException>();
        exception.Where(e => e.ErrorCode == "PRICE_CHANGED" && e.StatusCode == 409);
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldRetainAddressSnapshotFromCachedQuote_IgnoringFreeformRequestAddress()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var quoteId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        var cachedQuote = new CachedQuote
        {
            QuoteId = quoteId,
            BuyerId = buyerId,
            Request = new CheckoutQuoteRequest(
                new List<Guid> { Guid.NewGuid() },
                new ShippingAddressInput("Receiver", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM", 1442, "20101"),
                "0901234567"
            ),
            Response = new CheckoutQuoteResponse(
                quoteId,
                100000m,
                500000m,
                new List<ShopOrderQuote>
                {
                    new ShopOrderQuote(
                        sellerId,
                        "Shop A",
                        new List<ShopQuoteItem>
                        {
                            new ShopQuoteItem(Guid.NewGuid(), Guid.NewGuid(), skuId, "Product 1", 1, 100000m, 100000m)
                        },
                        100000m,
                        0m, 0m, 0m, 0m,
                        0m,
                        100000m
                    )
                },
                new List<string>(),
                null,
                DateTimeOffset.UtcNow.AddMinutes(15)
            ),
            ExpiredAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var quoteJsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cachedQuote));
        _mockCache
            .Setup(c => c.GetAsync($"checkout_quote:{quoteId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(quoteJsonBytes);

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuId, Guid.NewGuid(), sellerId, "Product 1", 100000m, true, 50));

        _mockFulfillmentClient
            .Setup(f => f.ReserveStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), 100000m, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletPaymentResult.Success("tx-success-123"));

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var request = new CreateOrderRequest(
            quoteId,
            PaymentMethod.Wallet,
            "999 Dang Thuy Tram, Ha Noi (Should Not Overwrite)",
            "0999999999"
        );

        // Act
        var result = await service.CreateOrderAsync(buyerId, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var subOrder = await dbContext.SubOrders.FirstOrDefaultAsync(s => s.OrderId == result.OrderId);
        subOrder.Should().NotBeNull();
        subOrder!.ShippingAddress.FullAddressText.Should().Be("123 Le Loi, Ben Nghe, Quan 1, TP.HCM");
        subOrder.ShippingAddress.GhnDistrictId.Should().Be(1442);
        subOrder.ShippingAddress.GhnWardCode.Should().Be("20101");
    }

    [Fact]
    public async Task GetOrderDetailAsync_ShouldCancelOrderLazily_WhenPaymentOverdue()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var buyerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var overdueOrder = new Commerce.Domain.Entities.Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.PendingPayment,
            TotalAmount = 150000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-10), // Đã quá hạn 10 phút
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-25)
        };

        var subOrder = new Commerce.Domain.Entities.SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SUB-TEST-01",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new Commerce.Domain.ValueObjects.ShippingAddressSnapshot("Buyer", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101", "Full Address"),
            ContactPhone = "0901234567",
            ItemsAmount = 150000m,
            TotalAmount = 150000m,
            SellerReceiveAmount = 150000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        overdueOrder.SubOrders.Add(subOrder);
        dbContext.Orders.Add(overdueOrder);
        await dbContext.SaveChangesAsync();

        _mockFulfillmentClient
            .Setup(f => f.ReleaseStockAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        // Act: Tra cứu đơn hàng
        var result = await service.GetOrderDetailAsync(buyerId, orderId, CancellationToken.None);

        // Assert: Trạng thái tự động đổi sang Cancelled
        result.Status.Should().Be("Cancelled");

        // Verify: Nhả kho Fulfillment
        _mockFulfillmentClient.Verify(
            f => f.ReleaseStockAsync(orderId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SellerRejectSubOrderAsync_ShouldReleaseStockUsingMasterOrderId_WithCorrectItems()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var buyerId = Guid.NewGuid();
        var masterOrderId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        var order = new Commerce.Domain.Entities.Order
        {
            Id = masterOrderId,
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 100000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var subOrder = new Commerce.Domain.Entities.SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-REJECT-01",
            OrderId = masterOrderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Confirmed,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaidAt = DateTimeOffset.UtcNow,
            ShippingAddress = new Commerce.Domain.ValueObjects.ShippingAddressSnapshot("Buyer", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101", "Full Address"),
            ContactPhone = "0901234567",
            ItemsAmount = 100000m,
            TotalAmount = 100000m,
            SellerReceiveAmount = 100000m,
            Items = new List<Commerce.Domain.Entities.SubOrderItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = subOrderId,
                    ProductId = Guid.NewGuid(),
                    SkuId = skuId,
                    SellerId = sellerId,
                    ProductNameSnapshot = "Product 1",
                    Quantity = 2,
                    UnitPrice = 50000m,
                    UnitPriceAfterDiscount = 50000m,
                    LineTotal = 100000m
                }
            }
        };

        order.SubOrders.Add(subOrder);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var mockFulfillment = new Mock<IFulfillmentClient>();
        mockFulfillment
            .Setup(f => f.ReleaseSubOrderStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            mockFulfillment.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        // Act: Shop từ chối đơn hàng con
        var request = new SellerRejectOrderRequest(subOrderId, CancellationReasonCode.SellerRejected, "Out of stock in shop");
        await service.SellerRejectSubOrderAsync(sellerId, masterOrderId, request, CancellationToken.None);

        // Assert: Trạng thái sub-order chuyển sang Cancelled
        var updatedSubOrder = await dbContext.SubOrders.FirstAsync(s => s.Id == subOrderId);
        updatedSubOrder.Status.Should().Be(SubOrderStatus.Cancelled);

        // VERIFY: Gọi ReleaseSubOrderStockAsync với Master OrderId (không phải SubOrderId)
        mockFulfillment.Verify(
            f => f.ReleaseSubOrderStockAsync(
                masterOrderId,
                It.Is<List<StockReservationItem>>(items => items.Count == 1 && items[0].SkuId == skuId && items[0].Quantity == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("ReadyToPick")]
    [InlineData("Picking")]
    [InlineData("Packed")]
    public async Task UpdateSubOrderDeliveryStatusAsync_ShouldKeepPreparationStatus_WhenReadyToPickOrPickingOrPacked(string webhookStatus)
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var subOrderId = Guid.NewGuid();
        var subOrder = new Commerce.Domain.Entities.SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-PREP-01",
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Confirmed,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            ContactPhone = "0901234567",
            ItemsAmount = 50000m,
            TotalAmount = 50000m,
            SellerReceiveAmount = 50000m,
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        dbContext.SubOrders.Add(subOrder);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var webhookTime = DateTimeOffset.UtcNow;

        // Act
        await service.UpdateSubOrderDeliveryStatusAsync(subOrderId, webhookStatus, webhookTime, CancellationToken.None);

        // Assert: Idempotent 200 OK - Status vẫn là Confirmed, UpdatedAt được cập nhật
        var updated = await dbContext.SubOrders.FirstAsync(s => s.Id == subOrderId);
        updated.Status.Should().Be(SubOrderStatus.Confirmed);
        updated.UpdatedAt.Should().BeCloseTo(webhookTime, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("HandedOver")]
    [InlineData("InTransit")]
    [InlineData("Shipped")]
    public async Task UpdateSubOrderDeliveryStatusAsync_ShouldTransitionToShipped_WhenHandedOverOrInTransitOrShipped(string webhookStatus)
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var subOrderId = Guid.NewGuid();
        var subOrder = new Commerce.Domain.Entities.SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-SHIP-01",
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Confirmed,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            ContactPhone = "0901234567",
            ItemsAmount = 50000m,
            TotalAmount = 50000m,
            SellerReceiveAmount = 50000m
        };
        dbContext.SubOrders.Add(subOrder);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var webhookTime = DateTimeOffset.UtcNow;

        // Act
        await service.UpdateSubOrderDeliveryStatusAsync(subOrderId, webhookStatus, webhookTime, CancellationToken.None);

        // Assert
        var updated = await dbContext.SubOrders.FirstAsync(s => s.Id == subOrderId);
        updated.Status.Should().Be(SubOrderStatus.Shipped);
        updated.ShippedAt.Should().BeCloseTo(webhookTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UpdateSubOrderDeliveryStatusAsync_ShouldTransitionToDelivered_WhenDelivered()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var subOrderId = Guid.NewGuid();
        var subOrder = new Commerce.Domain.Entities.SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-DELIV-01",
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Shipped,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            ContactPhone = "0901234567",
            ItemsAmount = 50000m,
            TotalAmount = 50000m,
            SellerReceiveAmount = 50000m
        };
        dbContext.SubOrders.Add(subOrder);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var webhookTime = DateTimeOffset.UtcNow;

        // Act
        await service.UpdateSubOrderDeliveryStatusAsync(subOrderId, "Delivered", webhookTime, CancellationToken.None);

        // Assert
        var updated = await dbContext.SubOrders.FirstAsync(s => s.Id == subOrderId);
        updated.Status.Should().Be(SubOrderStatus.Delivered);
        updated.DeliveredAt.Should().BeCloseTo(webhookTime, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("DeliveryFailed", CancellationReasonCode.DeliveryFailed, "DELIVERY_FAILED")]
    [InlineData("Returned", CancellationReasonCode.PackageReturned, "PACKAGE_RETURNED")]
    public async Task UpdateSubOrderDeliveryStatusAsync_ShouldTransitionToCancelledAndCreateRefund_WhenDeliveryFailedOrReturnedAndPaid(
        string webhookStatus,
        CancellationReasonCode expectedReasonCode,
        string expectedRefundReason)
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var masterOrderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        var paidAt = DateTimeOffset.UtcNow.AddHours(-2);

        var subOrder = new Commerce.Domain.Entities.SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-FAIL-01",
            OrderId = masterOrderId,
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Shipped,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaidAt = paidAt,
            ContactPhone = "0901234567",
            ItemsAmount = 85000m,
            TotalAmount = 85000m,
            SellerReceiveAmount = 85000m
        };
        dbContext.SubOrders.Add(subOrder);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var webhookTime = DateTimeOffset.UtcNow;

        // Act
        await service.UpdateSubOrderDeliveryStatusAsync(subOrderId, webhookStatus, webhookTime, CancellationToken.None);

        // Assert: Trạng thái SubOrder chuyển sang Cancelled
        var updated = await dbContext.SubOrders.FirstAsync(s => s.Id == subOrderId);
        updated.Status.Should().Be(SubOrderStatus.Cancelled);
        updated.CancelledAt.Should().BeCloseTo(webhookTime, TimeSpan.FromSeconds(1));

        // Assert: OrderCancellation record
        var cancellation = await dbContext.OrderCancellations.FirstOrDefaultAsync(c => c.SubOrderId == subOrderId);
        cancellation.Should().NotBeNull();
        cancellation!.ReasonCode.Should().Be(expectedReasonCode);
        cancellation.WasPaid.Should().BeTrue();
        cancellation.InventoryReleased.Should().Be(webhookStatus == "Returned");

        // Assert: OrderRefund record
        var refund = await dbContext.OrderRefunds.FirstOrDefaultAsync(r => r.SubOrderId == subOrderId);
        refund.Should().NotBeNull();
        refund!.Status.Should().Be(RefundStatus.WalletPending);
        refund.ReasonCode.Should().Be(expectedRefundReason);
        refund.Amount.Should().Be(85000m);
    }

    [Fact]
    public async Task UpdateSubOrderDeliveryStatusAsync_ShouldThrow400_WhenInvalidStatus()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var subOrderId = Guid.NewGuid();
        var subOrder = new Commerce.Domain.Entities.SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-INV-01",
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            Status = SubOrderStatus.Confirmed,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            ContactPhone = "0901234567",
            ItemsAmount = 50000m,
            TotalAmount = 50000m,
            SellerReceiveAmount = 50000m
        };
        dbContext.SubOrders.Add(subOrder);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        // Act & Assert
        var act = () => service.UpdateSubOrderDeliveryStatusAsync(subOrderId, "InvalidDeliveryStateXYZ", null, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("INVALID_DELIVERY_STATUS");
        ex.Which.StatusCode.Should().Be(400);
    }

    private class FailingCommerceDbContext : TestCommerceDbContext
    {
        public FailingCommerceDbContext(DbContextOptions<TestCommerceDbContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated PostgreSQL connection drop or constraint failure during save.");
        }
    }
    [Fact]
    public async Task ListSellerSubOrdersAsync_ShouldReturnPaginatedSubOrders_FilteredBySellerId()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var sellerId = Guid.NewGuid();
        var otherSellerId = Guid.NewGuid();

        var subOrder1 = new Commerce.Domain.Entities.SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SUB-SELLER-01",
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = sellerId,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            ItemsAmount = 100000m,
            TotalAmount = 100000m,
            SellerReceiveAmount = 100000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        var subOrder2 = new Commerce.Domain.Entities.SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SUB-SELLER-02",
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = sellerId,
            Status = SubOrderStatus.Confirmed,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            ItemsAmount = 200000m,
            TotalAmount = 200000m,
            SellerReceiveAmount = 200000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var otherSubOrder = new Commerce.Domain.Entities.SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SUB-OTHER-01",
            OrderId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = otherSellerId,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            ItemsAmount = 300000m,
            TotalAmount = 300000m,
            SellerReceiveAmount = 300000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.SubOrders.AddRange(subOrder1, subOrder2, otherSubOrder);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        // Act
        var result = await service.ListSellerSubOrdersAsync(sellerId, page: 1, pageSize: 10, status: null, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Pagination.TotalCount.Should().Be(2);
        result.Items.Select(x => x.SubOrderId).Should().Contain(new[] { subOrder1.Id, subOrder2.Id });
        result.Items.Select(x => x.SubOrderId).Should().NotContain(otherSubOrder.Id);
    }

    [Fact]
    public async Task UpdateSubOrderDeliveryStatusAsync_WhenIdIsAuctionOrder_ShouldUpdateAuctionOrderStatusAndTimestamps()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var auctionOrderId = Guid.NewGuid();
        var auctionOrder = new AuctionOrder
        {
            Id = auctionOrderId,
            OrderNo = "AO-20261111-00099",
            AuctionId = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Antique Vase",
            WinningAmount = 1500000m,
            SellerReceiveAmount = 1425000m,
            PlatformFeeAmount = 75000m,
            Status = SubOrderStatus.Shipped,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddDays(-1),
            PaidAt = DateTimeOffset.UtcNow.AddDays(-1),
            ShippedAt = DateTimeOffset.UtcNow.AddHours(-10)
        };

        dbContext.AuctionOrders.Add(auctionOrder);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var deliveryTime = DateTimeOffset.UtcNow;

        // Act
        await service.UpdateSubOrderDeliveryStatusAsync(auctionOrderId, "Delivered", deliveryTime, CancellationToken.None);

        // Assert
        var updated = await dbContext.AuctionOrders.FirstAsync(a => a.Id == auctionOrderId);
        updated.Status.Should().Be(SubOrderStatus.Delivered);
        updated.DeliveredAt.Should().BeCloseTo(deliveryTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SellerAcceptSubOrderAsync_ShouldPersistTrackingCodeCarrierCodeAndLabelPrintUrl()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 250000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-20261112-0001",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM", 1442, "20101"),
            ContactPhone = "0901234567",
            ItemsAmount = 220000m,
            ShippingFeeAmount = 30000m,
            TotalAmount = 250000m,
            SellerReceiveAmount = 220000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaidAt = DateTimeOffset.UtcNow
        };

        dbContext.Orders.Add(order);
        dbContext.SubOrders.Add(subOrder);
        await dbContext.SaveChangesAsync();

        _mockFulfillmentClient
            .Setup(f => f.CreateShipmentAsync(It.IsAny<ShipmentCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentCreationResult("GHN-SGN-1029384", "GHN", "https://api.ghn.vn/print/label-1029384.pdf"));

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var request = new SellerAcceptOrderRequest(subOrderId, "Ready for dispatch");

        // Act
        await service.SellerAcceptSubOrderAsync(sellerId, orderId, request, CancellationToken.None);

        // Assert
        var updated = await dbContext.SubOrders.Include(s => s.StatusHistories).FirstAsync(s => s.Id == subOrderId);
        updated.Status.Should().Be(SubOrderStatus.Confirmed);
        updated.CarrierCode.Should().Be("GHN");
        updated.TrackingCode.Should().Be("GHN-SGN-1029384");
        updated.LabelPrintUrl.Should().Be("https://api.ghn.vn/print/label-1029384.pdf");
        updated.ShipmentPendingCreation.Should().BeFalse();
        updated.SellerAcceptedAt.Should().NotBeNull();

        var history = updated.StatusHistories.FirstOrDefault(h => h.ToStatus == SubOrderStatus.Confirmed);
        history.Should().NotBeNull();
        history!.FromStatus.Should().Be(SubOrderStatus.Paid);
        history.ActorRole.Should().Be(OrderActorRole.Seller);
    }

    [Fact]
    public async Task GetShipmentTrackingAsync_ShouldReturnRealTrackingCode_WithoutSyntheticFormatting()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 250000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-20261112-9999",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Shipping,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM", 1442, "20101"),
            ContactPhone = "0901234567",
            ItemsAmount = 220000m,
            ShippingFeeAmount = 30000m,
            TotalAmount = 250000m,
            SellerReceiveAmount = 220000m,
            CarrierCode = "GHN",
            TrackingCode = "GHN-REAL-778899",
            LabelPrintUrl = "https://label.com/778899",
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            PaidAt = DateTimeOffset.UtcNow,
            ShippedAt = DateTimeOffset.UtcNow
        };

        dbContext.Orders.Add(order);
        dbContext.SubOrders.Add(subOrder);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        // Act
        var result = await service.GetShipmentTrackingAsync(buyerId, orderId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Shipments.Should().HaveCount(1);
        var shipment = result.Shipments.First();
        shipment.TrackingCode.Should().Be("GHN-REAL-778899");
        shipment.TrackingCode.Should().NotBe($"GHN-{subOrder.SubOrderNo}");
        shipment.CarrierCode.Should().Be("GHN");
        shipment.LabelPrintUrl.Should().Be("https://label.com/778899");
    }

    [Fact]
    public async Task RetryCreateShipmentAsync_WhenPendingCreation_ShouldPersistRealShipment()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-20261112-RETRY",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Confirmed,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("Nguyen Van A", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM", 1442, "20101"),
            ContactPhone = "0901234567",
            ItemsAmount = 220000m,
            ShippingFeeAmount = 30000m,
            TotalAmount = 250000m,
            SellerReceiveAmount = 220000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            ShipmentPendingCreation = true,
            TrackingCode = null
        };

        dbContext.SubOrders.Add(subOrder);
        await dbContext.SaveChangesAsync();

        _mockFulfillmentClient
            .Setup(f => f.CreateShipmentAsync(It.IsAny<ShipmentCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentCreationResult("GHN-RETRY-SUCCESS-1", "GHN", "https://label.pdf/retry"));

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        // Act
        var response = await service.RetryCreateShipmentAsync(sellerId, subOrderId, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.TrackingCode.Should().Be("GHN-RETRY-SUCCESS-1");
        response.CarrierCode.Should().Be("GHN");

        var updated = await dbContext.SubOrders.FirstAsync(s => s.Id == subOrderId);
        updated.TrackingCode.Should().Be("GHN-RETRY-SUCCESS-1");
        updated.ShipmentPendingCreation.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrderDetailAsync_WhenAdminOrStaff_ShouldAllowAccessToOtherUsersOrder()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var buyerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 500000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        // Act
        var result = await service.GetOrderDetailAsync(adminId, orderId, isAdminOrStaff: true);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(orderId);
        result.BuyerId.Should().Be(buyerId);
    }

    [Fact]
    public async Task GetOrderDetailAsync_WhenNotOwnerAndNotAdminOrStaff_ShouldThrowForbidden()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var buyerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 500000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        // Act
        var act = () => service.GetOrderDetailAsync(strangerId, orderId, isAdminOrStaff: false);

        // Assert
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Which.ErrorCode.Should().Be("FORBIDDEN");
        ex.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetSubOrderDetailAsync_WhenAdminOrStaff_ShouldAllowAccess()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 300000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-TEST-00003",
            CarrierCode = "GHN",
            TrackingCode = "GHN-TRACK-3",
            ShipmentPendingCreation = false,
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("Buyer", "0901234567", "123 Street", "Ward", "District", "Province", 1, "1"),
            ContactPhone = "0901234567",
            ItemsAmount = 300000m,
            TotalAmount = 300000m,
            SellerReceiveAmount = 285000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        dbContext.Orders.Add(order);
        dbContext.SubOrders.Add(subOrder);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        // Act
        var result = await service.GetSubOrderDetailAsync(adminId, subOrderId, isAdminOrStaff: true);

        // Assert
        result.Should().NotBeNull();
        result.SubOrderId.Should().Be(subOrderId);
        result.BuyerId.Should().Be(buyerId);
        result.CarrierCode.Should().Be("GHN");
        result.TrackingCode.Should().Be("GHN-TRACK-3");
        result.ShipmentPendingCreation.Should().BeFalse();
    }

    [Fact]
    public async Task GetShipmentTrackingAsync_WhenAdminOrStaff_ShouldAllowAccess()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var buyerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 200000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        // Act
        var result = await service.GetShipmentTrackingAsync(adminId, orderId, isAdminOrStaff: true);

        // Assert
        result.Should().NotBeNull();
        result.OrderId.Should().Be(orderId);
    }

    [Fact]
    public async Task GetShipmentTimelineAsync_WhenAdminOrStaff_ShouldAllowAccess()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-TEST-00005",
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = SubOrderStatus.Shipping,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("Buyer", "0901234567", "123 Street", "Ward", "District", "Province", 1, "1"),
            ContactPhone = "0901234567",
            ItemsAmount = 200000m,
            TotalAmount = 200000m,
            SellerReceiveAmount = 190000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        dbContext.SubOrders.Add(subOrder);
        await dbContext.SaveChangesAsync();

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        // Act
        var result = await service.GetShipmentTimelineAsync(adminId, subOrderId, isAdminOrStaff: true);

        // Assert
        result.Should().NotBeNull();
        result.SubOrderId.Should().Be(subOrderId);
    }

    [Fact]
    public async Task QuoteCheckoutAsync_ShouldCalculateGhnVolumetricWeight_AndPassToFulfillmentClient()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            BuyerId = buyerId,
            Status = CartStatus.Active,
            Items = new List<CartItem>
            {
                new CartItem
                {
                    Id = Guid.NewGuid(),
                    CartId = Guid.NewGuid(),
                    ProductId = productId,
                    SkuId = skuId,
                    SellerId = sellerId,
                    ProductNameSnapshot = "Helmet HJC",
                    UnitPriceSnapshot = 500000m,
                    Quantity = 2
                }
            }
        };
        dbContext.Carts.Add(cart);
        await dbContext.SaveChangesAsync();

        // Catalog returns packaging dimensions: 30x25x20 cm, 300g -> Volumetric = 30*25*20/5 = 3000g > 300g
        _mockCatalogClient
            .Setup(c => c.GetSkuBatchDetailsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, CatalogProductCheckResult>
            {
                [skuId] = new CatalogProductCheckResult(skuId, productId, sellerId, "Helmet HJC", 500000m, true, 100, null, 300, "Regular", 30, 25, 20)
            });

        ShopShippingQuoteRequest? capturedQuoteRequest = null;
        _mockFulfillmentClient
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ShopShippingQuoteRequest, CancellationToken>((req, _) => capturedQuoteRequest = req)
            .ReturnsAsync(45000m);

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var request = new CheckoutQuoteRequest(
            new List<Guid> { cart.Items.First().Id },
            new ShippingAddressInput("Receiver", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101"),
            "0901234567"
        );

        // Act
        var result = await service.QuoteCheckoutAsync(buyerId, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        capturedQuoteRequest.Should().NotBeNull();
        capturedQuoteRequest!.Items.Should().HaveCount(1);
        capturedQuoteRequest.Items[0].WeightGrams.Should().Be(3000); // 3000g per unit
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldCallMarkVariantsOrderedAsync_WhenOrderCreated()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var quoteId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var cachedQuote = new CachedQuote
        {
            QuoteId = quoteId,
            BuyerId = buyerId,
            Request = new CheckoutQuoteRequest(
                new List<Guid> { Guid.NewGuid() },
                new ShippingAddressInput("Receiver", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101"),
                "0901234567"
            ),
            Response = new CheckoutQuoteResponse(
                quoteId,
                100000m,
                500000m,
                new List<ShopOrderQuote>
                {
                    new ShopOrderQuote(
                        sellerId,
                        "Shop A",
                        new List<ShopQuoteItem>
                        {
                            new ShopQuoteItem(Guid.NewGuid(), Guid.NewGuid(), skuId, "Product 1", 1, 100000m, 100000m)
                        },
                        100000m,
                        0m, 0m, 0m, 0m,
                        0m,
                        100000m
                    )
                },
                new List<string>(),
                null,
                DateTimeOffset.UtcNow.AddMinutes(15)
            ),
            ExpiredAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var quoteJsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cachedQuote));
        _mockCache
            .Setup(c => c.GetAsync($"checkout_quote:{quoteId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(quoteJsonBytes);

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuId, Guid.NewGuid(), sellerId, "Product 1", 100000m, true, 50));

        _mockFulfillmentClient
            .Setup(f => f.ReserveStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletPaymentResult.Success("tx-success-123"));

        _mockFulfillmentClient
            .Setup(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockCatalogClient
            .Setup(c => c.MarkVariantsOrderedAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var request = new CreateOrderRequest(
            quoteId,
            PaymentMethod.Wallet,
            "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            "0901234567"
        );

        // Act
        var result = await service.CreateOrderAsync(buyerId, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        _mockCatalogClient.Verify(c => c.MarkVariantsOrderedAsync(
            It.Is<IEnumerable<Guid>>(ids => ids.Contains(skuId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldSucceed_EvenWhenMarkVariantsOrderedAsyncFails()
    {
        // Arrange
        using var dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        var quoteId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var cachedQuote = new CachedQuote
        {
            QuoteId = quoteId,
            BuyerId = buyerId,
            Request = new CheckoutQuoteRequest(
                new List<Guid> { Guid.NewGuid() },
                new ShippingAddressInput("Receiver", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101"),
                "0901234567"
            ),
            Response = new CheckoutQuoteResponse(
                quoteId,
                100000m,
                500000m,
                new List<ShopOrderQuote>
                {
                    new ShopOrderQuote(
                        sellerId,
                        "Shop A",
                        new List<ShopQuoteItem>
                        {
                            new ShopQuoteItem(Guid.NewGuid(), Guid.NewGuid(), skuId, "Product 1", 1, 100000m, 100000m)
                        },
                        100000m,
                        0m, 0m, 0m, 0m,
                        0m,
                        100000m
                    )
                },
                new List<string>(),
                null,
                DateTimeOffset.UtcNow.AddMinutes(15)
            ),
            ExpiredAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var quoteJsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cachedQuote));
        _mockCache
            .Setup(c => c.GetAsync($"checkout_quote:{quoteId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(quoteJsonBytes);

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(skuId, Guid.NewGuid(), sellerId, "Product 1", 100000m, true, 50));

        _mockFulfillmentClient
            .Setup(f => f.ReserveStockAsync(It.IsAny<Guid>(), It.IsAny<List<StockReservationItem>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletPaymentResult.Success("tx-success-123"));

        _mockFulfillmentClient
            .Setup(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Catalog mark-ordered throws exception
        _mockCatalogClient
            .Setup(c => c.MarkVariantsOrderedAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("Catalog service unavailable"));

        var service = new OrderService(
            dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _mockCache.Object,
            _mockLogger.Object);

        var request = new CreateOrderRequest(
            quoteId,
            PaymentMethod.Wallet,
            "123 Le Loi, Ben Nghe, Quan 1, TP.HCM",
            "0901234567"
        );

        // Act
        var result = await service.CreateOrderAsync(buyerId, request, CancellationToken.None);

        // Assert: Order creation succeeds despite Catalog error
        result.Should().NotBeNull();
        result.OrderId.Should().NotBeEmpty();
    }
}
