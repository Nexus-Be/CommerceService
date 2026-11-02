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
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
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

        _mockFulfillmentClient
            .Setup(f => f.CommitStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.ReleaseStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), 100000m, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WalletPaymentResult.Success("tx-success-123"));

        _mockWalletClient
            .Setup(w => w.CheckoutDirectPaymentAsync(It.IsAny<Guid>(), buyerId.ToString(), 100000m, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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
            .Setup(w => w.CheckoutDirectPaymentAsync(It.IsAny<Guid>(), buyerId.ToString(), 80000m, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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
}
