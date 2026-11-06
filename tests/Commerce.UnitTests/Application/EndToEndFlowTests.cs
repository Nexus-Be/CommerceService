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
using Commerce.Domain.ValueObjects;
using Commerce.UnitTests.Common;
using FluentAssertions;
using Moq;
using Xunit;

public class EndToEndFlowTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<ILogger<CartService>> _mockCartLogger = new();
    private readonly Mock<ILogger<OrderService>> _mockOrderLogger = new();
    private readonly IDistributedCache _cache;

    private readonly Guid _buyerId = Guid.NewGuid();
    private readonly Guid _sellerA = Guid.NewGuid();
    private readonly Guid _sellerB = Guid.NewGuid();
    private readonly Guid _sku1 = Guid.NewGuid();
    private readonly Guid _sku2 = Guid.NewGuid();
    private readonly Guid _sku3 = Guid.NewGuid();

    public EndToEndFlowTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        // Catalog setup
        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(_sku1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(_sku1, Guid.NewGuid(), _sellerA, "Áo Thun Polo A", 100000m, true, 50));

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(_sku2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(_sku2, Guid.NewGuid(), _sellerA, "Quần Jean Slim", 200000m, true, 30));

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(_sku3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(_sku3, Guid.NewGuid(), _sellerB, "Giày Thể Thao B", 300000m, true, 20));

        _mockCatalogClient
            .Setup(c => c.GetSkuBatchDetailsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<Guid>, CancellationToken>(async (ids, ct) =>
            {
                var dict = new Dictionary<Guid, CatalogProductCheckResult>();
                foreach (var id in ids)
                {
                    var item = await _mockCatalogClient.Object.GetSkuDetailsAsync(id, ct);
                    if (item != null)
                    {
                        dict[id] = item;
                    }
                }
                return dict;
            });

        // Fulfillment setup
        _mockFulfillmentClient
            .Setup(f => f.CheckStockAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockFulfillmentClient
            .Setup(f => f.BatchCheckStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, SkuStockCheckResult>
            {
                { _sku1, new SkuStockCheckResult(_sku1, true, 50, null) },
                { _sku2, new SkuStockCheckResult(_sku2, true, 30, null) },
                { _sku3, new SkuStockCheckResult(_sku3, true, 20, null) }
            });

        _mockFulfillmentClient
            .Setup(f => f.CalculateShopShippingFeeAsync(It.IsAny<ShopShippingQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(30000m);

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

        _mockFulfillmentClient
            .Setup(f => f.CreateShipmentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShipmentCreationResult("GHN-E2E-999", "GHN", "https://shipping.nexus.internal/print/999"));

        // Wallet setup
        _mockWalletClient
            .Setup(w => w.GetWalletBalanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1500000m);

        _mockWalletClient
            .Setup(w => w.PayOrderAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletPaymentResult(true, TransactionId: Guid.NewGuid().ToString(), BalanceAfter: 1500000m));

        _mockWalletClient
            .Setup(w => w.CheckoutDirectPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.RefundOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockWalletClient
            .Setup(w => w.SettleSellerSubOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private CartService CreateCartService()
    {
        return new CartService(_dbContext, _mockCatalogClient.Object, _mockFulfillmentClient.Object);
    }

    private OrderService CreateOrderService(ICommerceDbContext? customDb = null)
    {
        return new OrderService(
            customDb ?? _dbContext,
            _mockWalletClient.Object,
            _mockFulfillmentClient.Object,
            _mockCatalogClient.Object,
            _cache,
            _mockOrderLogger.Object);
    }

    [Fact]
    public async Task E2E_Flow1_ShoppingCart_FullLifecycle_ShouldPass()
    {
        var cartService = CreateCartService();

        // 1. Thêm sản phẩm 1
        var cart = await cartService.AddItemAsync(_buyerId, new AddCartItemRequest(_sku1, 2, _sellerA), CancellationToken.None);
        cart.Items.Should().HaveCount(1);
        cart.SubtotalAmount.Should().Be(200000m);

        // 2. Thêm sản phẩm 2
        cart = await cartService.AddItemAsync(_buyerId, new AddCartItemRequest(_sku2, 1, _sellerA), CancellationToken.None);
        cart.Items.Should().HaveCount(2);
        cart.SubtotalAmount.Should().Be(400000m);

        // 3. Cập nhật số lượng
        var item1 = cart.Items.First(i => i.SkuId == _sku1);
        cart = await cartService.UpdateItemQuantityAsync(_buyerId, item1.Id, new UpdateCartItemQuantityRequest(item1.Id, 3), CancellationToken.None);
        cart.Items.First(i => i.SkuId == _sku1).Quantity.Should().Be(3);
        cart.SubtotalAmount.Should().Be(500000m);

        // 4. Lấy giỏ gom nhóm theo Shop
        var grouped = await cartService.GetGroupedCartAsync(_buyerId, CancellationToken.None);
        grouped.Shops.Should().HaveCount(1);
        grouped.Shops[0].Items.Should().HaveCount(2);
        grouped.Shops[0].Items.All(i => i.IsAvailable).Should().BeTrue();

        // 5. Tính tiền các món tick chọn (Chỉ chọn item 1)
        var calc = await cartService.CalculateSelectedItemsAsync(_buyerId, new CalculateSelectedCartItemsRequest(new List<Guid> { item1.Id }), CancellationToken.None);
        calc.SelectedItemCount.Should().Be(1);
        calc.SubtotalAmount.Should().Be(300000m);
    }

    [Fact]
    public async Task E2E_Flow2_CheckoutQuote_CalculatesShippingAndBalance_ShouldPass()
    {
        var cartService = CreateCartService();
        var orderService = CreateOrderService();

        await cartService.AddItemAsync(_buyerId, new AddCartItemRequest(_sku1, 1, _sellerA), CancellationToken.None);
        await cartService.AddItemAsync(_buyerId, new AddCartItemRequest(_sku3, 1, _sellerB), CancellationToken.None);

        var cart = await cartService.GetOrCreateActiveCartAsync(_buyerId, CancellationToken.None);
        var cartItemIds = cart.Items.Select(i => i.Id).ToList();

        var request = new CheckoutQuoteRequest(
            cartItemIds,
            new ShippingAddressInput("Nguyen Van Buyer", "0901234567", "123 Le Loi", "Ben Nghe", "Quan 1", "TP.HCM", 1442, "20101"),
            "0901234567",
            PaymentMethod.Wallet
        );

        var quote = await orderService.QuoteCheckoutAsync(_buyerId, request, CancellationToken.None);

        quote.QuoteId.Should().NotBeEmpty();
        quote.Shops.Should().HaveCount(2); // Shop A và Shop B
        quote.WalletBalance.Should().Be(1500000m); // Số dư thực
        // 100k (Shop A) + 30k ship + 300k (Shop B) + 30k ship = 460k
        quote.GrandTotal.Should().Be(460000m);
        quote.ExpiredAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task E2E_Flow3_AsymmetricPriceDrift_PriceIncreaseBlocks409_PriceDropAutoApplies_ShouldPass()
    {
        var cartService = CreateCartService();
        var orderService = CreateOrderService();

        await cartService.AddItemAsync(_buyerId, new AddCartItemRequest(_sku1, 1, _sellerA), CancellationToken.None);
        var cart = await cartService.GetOrCreateActiveCartAsync(_buyerId, CancellationToken.None);

        var quote = await orderService.QuoteCheckoutAsync(_buyerId, new CheckoutQuoteRequest(
            cart.Items.Select(i => i.Id).ToList(),
            new ShippingAddressInput("Buyer", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101"),
            "0901234567",
            PaymentMethod.Wallet
        ), CancellationToken.None);

        // Kịch bản A: Giá Catalog TĂNG từ 100.000 lên 120.000 VNĐ -> CHẶN ĐƠN (409)
        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(_sku1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(_sku1, Guid.NewGuid(), _sellerA, "Áo Thun Polo A", 120000m, true, 50));

        var actIncrease = () => orderService.CreateOrderAsync(_buyerId, new CreateOrderRequest(
            quote.QuoteId,
            PaymentMethod.Wallet,
            "123 Le Loi",
            "0901234567"
        ), CancellationToken.None);

        var ex = await actIncrease.Should().ThrowAsync<CommerceDomainException>();
        ex.Where(e => e.ErrorCode == "PRICE_CHANGED" && e.StatusCode == 409);

        // Kịch bản B: Khách lấy lại quote mới (với giá 120k). Sau đó Catalog GIẢM GIÁ xuống 90k -> TỰ ÁP DỤNG & TẠO ĐƠN THÀNH CÔNG
        var newQuote = await orderService.QuoteCheckoutAsync(_buyerId, new CheckoutQuoteRequest(
            cart.Items.Select(i => i.Id).ToList(),
            new ShippingAddressInput("Buyer", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101"),
            "0901234567",
            PaymentMethod.Wallet
        ), CancellationToken.None);

        _mockCatalogClient
            .Setup(c => c.GetSkuDetailsAsync(_sku1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogProductCheckResult(_sku1, Guid.NewGuid(), _sellerA, "Áo Thun Polo A", 90000m, true, 50));

        var orderResult = await orderService.CreateOrderAsync(_buyerId, new CreateOrderRequest(
            newQuote.QuoteId,
            PaymentMethod.Wallet,
            "123 Le Loi",
            "0901234567"
        ), CancellationToken.None);

        orderResult.Status.Should().Be("Paid");
        orderResult.AppliedPriceDrops.Should().NotBeNull();
        orderResult.AppliedPriceDrops!.First().SavedAmount.Should().Be(30000m); // Tiết kiệm 30.000đ (120k -> 90k)
    }

    [Fact]
    public async Task E2E_Flow4_OrderCreation_WalletPaymentAndStockCommit_ShouldPass()
    {
        var cartService = CreateCartService();
        var orderService = CreateOrderService();

        await cartService.AddItemAsync(_buyerId, new AddCartItemRequest(_sku1, 2, _sellerA), CancellationToken.None);
        var cart = await cartService.GetOrCreateActiveCartAsync(_buyerId, CancellationToken.None);

        var quote = await orderService.QuoteCheckoutAsync(_buyerId, new CheckoutQuoteRequest(
            cart.Items.Select(i => i.Id).ToList(),
            new ShippingAddressInput("Buyer", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101"),
            "0901234567",
            PaymentMethod.Wallet
        ), CancellationToken.None);

        var order = await orderService.CreateOrderAsync(_buyerId, new CreateOrderRequest(
            quote.QuoteId,
            PaymentMethod.Wallet,
            "123 Le Loi",
            "0901234567"
        ), CancellationToken.None);

        order.Status.Should().Be("Paid");

        // Verify: Khóa kho & Trừ kho vĩnh viễn đã được gọi
        _mockFulfillmentClient.Verify(f => f.ReserveStockAsync(order.OrderId, It.IsAny<List<StockReservationItem>>(), 15, It.IsAny<CancellationToken>()), Times.Once);
        _mockFulfillmentClient.Verify(f => f.CommitStockAsync(order.OrderId, It.IsAny<CancellationToken>()), Times.Once);

        // Verify: Tiền ví đã được trừ
        _mockWalletClient.Verify(w => w.PayOrderAsync(order.OrderId, order.TotalAmount, null, It.IsAny<CancellationToken>()), Times.Once);

        // Verify: Giỏ hàng đã dọn sạch món đã mua
        var remainingCart = await cartService.GetOrCreateActiveCartAsync(_buyerId, CancellationToken.None);
        remainingCart.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task E2E_Flow5_DatabaseFailure_CircuitBreakerRollbackStockAndRefundWallet_ShouldPass()
    {
        var options = new DbContextOptionsBuilder<TestCommerceDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var failingDb = new FailingDbCommerce(options);

        var orderService = CreateOrderService(failingDb);

        // Chuẩn bị quote giả lập
        var quoteId = Guid.NewGuid();
        var cachedQuote = new CachedQuote
        {
            QuoteId = quoteId,
            BuyerId = _buyerId,
            Request = new CheckoutQuoteRequest(
                new List<Guid> { Guid.NewGuid() },
                new ShippingAddressInput("Buyer", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101"),
                "0901234567"
            ),
            Response = new CheckoutQuoteResponse(
                quoteId,
                100000m,
                500000m,
                new List<ShopOrderQuote>
                {
                    new ShopOrderQuote(_sellerA, "Shop A", new List<ShopQuoteItem>
                    {
                        new ShopQuoteItem(Guid.NewGuid(), Guid.NewGuid(), _sku1, "Áo Polo", 1, 100000m, 100000m)
                    }, 100000m, 0m, 0m, 0m, 0m, 0m, 100000m)
                },
                new List<string>(),
                null,
                DateTimeOffset.UtcNow.AddMinutes(15)
            ),
            ExpiredAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var json = JsonSerializer.Serialize(cachedQuote);
        await _cache.SetStringAsync($"checkout_quote:{quoteId}", json);

        // Act
        var act = () => orderService.CreateOrderAsync(_buyerId, new CreateOrderRequest(
            quoteId,
            PaymentMethod.Wallet,
            "123 Le Loi",
            "0901234567"
        ), CancellationToken.None);

        // Assert: Ném ORDER_CREATION_FAILED (500)
        var ex = await act.Should().ThrowAsync<CommerceDomainException>();
        ex.Where(e => e.ErrorCode == "ORDER_CREATION_FAILED" && e.StatusCode == 500);

        // Verify: Kho được giải phóng (Rollback Inventory)
        _mockFulfillmentClient.Verify(f => f.ReleaseStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify: Ví được hoàn tiền 100% với Idempotency Key (Rollback Wallet)
        _mockWalletClient.Verify(w => w.RefundOrderPaymentAsync(
            It.IsAny<Guid>(),
            _buyerId.ToString(),
            100000m,
            It.IsAny<string>(),
            It.Is<string>(k => k.StartsWith("compensation-db-")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task E2E_Flow6_HybridPaymentExpiration_PassiveGuardAndActiveSweeper_ShouldPass()
    {
        var orderService = CreateOrderService();

        var overdueOrder = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = _buyerId,
            Status = OrderStatus.PendingPayment,
            TotalAmount = 250000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-5), // Quá hạn 5 phút
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };

        var subOrder = new SubOrder
        {
            Id = Guid.NewGuid(),
            SubOrderNo = "SUB-EXPIRE-01",
            OrderId = overdueOrder.Id,
            BuyerId = _buyerId,
            SellerId = _sellerA,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("Buyer", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101", "Full Address"),
            ContactPhone = "0901234567",
            ItemsAmount = 250000m,
            TotalAmount = 250000m,
            SellerReceiveAmount = 250000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        overdueOrder.SubOrders.Add(subOrder);
        _dbContext.Orders.Add(overdueOrder);
        await _dbContext.SaveChangesAsync();

        // 1. Passive Guard: Khi Buyer vào xem chi tiết đơn quá hạn, tự động hủy và nhả kho
        var detail = await orderService.GetOrderDetailAsync(_buyerId, overdueOrder.Id, CancellationToken.None);
        detail.Status.Should().Be("Cancelled");

        // Verify: Nhả kho Fulfillment
        _mockFulfillmentClient.Verify(f => f.ReleaseStockAsync(overdueOrder.Id, It.IsAny<CancellationToken>()), Times.Once);

        // 2. Active Sweeper: Kiểm tra bản ghi OrderCancellation đã được ghi vết
        var cancellations = await _dbContext.OrderCancellations.Where(c => c.SubOrderId == subOrder.Id).ToListAsync();
        cancellations.Should().HaveCount(1);
        cancellations[0].ReasonCode.Should().Be(CancellationReasonCode.PaymentTimeout);
        cancellations[0].ActorRole.Should().Be(OrderActorRole.System);
    }

    [Fact]
    public async Task E2E_Flow7_SellerAcceptSubOrder_ConfirmsAndCreatesGhnShipment_ShouldPass()
    {
        var orderService = CreateOrderService();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = _buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 200000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-ACCEPT-01",
            OrderId = orderId,
            BuyerId = _buyerId,
            SellerId = _sellerA,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("Buyer", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101", "Full Address"),
            ContactPhone = "0901234567",
            ItemsAmount = 200000m,
            TotalAmount = 200000m,
            SellerReceiveAmount = 200000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        order.SubOrders.Add(subOrder);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Seller A bấm tiếp nhận đơn
        await orderService.SellerAcceptSubOrderAsync(_sellerA, orderId, new SellerAcceptOrderRequest(subOrderId), CancellationToken.None);

        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrderId);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Confirmed);
        updatedSubOrder.SellerAcceptedAt.Should().NotBeNull();

        // Verify: Vận đơn GHN được tạo tự động
        _mockFulfillmentClient.Verify(f => f.CreateShipmentAsync(subOrderId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task E2E_Flow8_SellerRejectSubOrder_TargetedStockReleaseAndWalletRefund_ShouldPass()
    {
        var orderService = CreateOrderService();
        var orderId = Guid.NewGuid();
        var subOrderAId = Guid.NewGuid();
        var subOrderBId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = _buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 500000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var subOrderA = new SubOrder
        {
            Id = subOrderAId,
            SubOrderNo = "SUB-A-01",
            OrderId = orderId,
            BuyerId = _buyerId,
            SellerId = _sellerA,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            PaidAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ShippingAddress = new ShippingAddressSnapshot("Buyer", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101", "Full Address"),
            ContactPhone = "0901234567",
            ItemsAmount = 200000m,
            TotalAmount = 200000m,
            SellerReceiveAmount = 200000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        subOrderA.Items.Add(new SubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrderAId,
            ProductId = Guid.NewGuid(),
            SkuId = _sku1,
            SellerId = _sellerA,
            ProductNameSnapshot = "Áo Thun A",
            Quantity = 2,
            UnitPrice = 100000m,
            UnitPriceAfterDiscount = 100000m,
            LineTotal = 200000m
        });

        var subOrderB = new SubOrder
        {
            Id = subOrderBId,
            SubOrderNo = "SUB-B-01",
            OrderId = orderId,
            BuyerId = _buyerId,
            SellerId = _sellerB,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            PaidAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ShippingAddress = new ShippingAddressSnapshot("Buyer", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101", "Full Address"),
            ContactPhone = "0901234567",
            ItemsAmount = 300000m,
            TotalAmount = 300000m,
            SellerReceiveAmount = 300000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        subOrderB.Items.Add(new SubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrderBId,
            ProductId = Guid.NewGuid(),
            SkuId = _sku3,
            SellerId = _sellerB,
            ProductNameSnapshot = "Giày B",
            Quantity = 1,
            UnitPrice = 300000m,
            UnitPriceAfterDiscount = 300000m,
            LineTotal = 300000m
        });

        order.SubOrders.Add(subOrderA);
        order.SubOrders.Add(subOrderB);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Shop A từ chối đơn
        await orderService.SellerRejectSubOrderAsync(
            _sellerA,
            orderId,
            new SellerRejectOrderRequest(subOrderAId, CancellationReasonCode.OutOfStock, "Shop A hết áo"),
            CancellationToken.None);

        // Verify: SubOrder A bị hủy
        var reloadedA = await _dbContext.SubOrders.FindAsync(subOrderAId);
        reloadedA!.Status.Should().Be(SubOrderStatus.Cancelled);

        // Verify: SubOrder B vẫn nguyên vẹn (Status: Paid)
        var reloadedB = await _dbContext.SubOrders.FindAsync(subOrderBId);
        reloadedB!.Status.Should().Be(SubOrderStatus.Paid);

        // Verify: Bản ghi OrderRefund được tạo cho Buyer số tiền 200.000đ của SubOrder A
        var refund = await _dbContext.OrderRefunds.FirstOrDefaultAsync(r => r.SubOrderId == subOrderAId);
        refund.Should().NotBeNull();
        refund!.Amount.Should().Be(200000m);
        refund.Status.Should().Be(RefundStatus.Completed);
        refund.CompletedAt.Should().NotBeNull();
        refund.ReasonCode.Should().Be("SELLER_REJECTED");

        // Verify: CHỈ nhả kho của SubOrder A (không nhả kho của SubOrder B) bằng Master OrderId
        _mockFulfillmentClient.Verify(f => f.ReleaseSubOrderStockAsync(
            orderId,
            It.Is<List<StockReservationItem>>(items => items.Count == 1 && items[0].SkuId == _sku1),
            It.IsAny<CancellationToken>()),
            Times.Once);

        _mockFulfillmentClient.Verify(f => f.ReleaseStockAsync(orderId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task E2E_Flow9_BuyerConfirmReceiptAndEscrowSettlement_ShouldPass()
    {
        var orderService = CreateOrderService();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            BuyerId = _buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 250000m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };

        var subOrder = new SubOrder
        {
            Id = subOrderId,
            SubOrderNo = "SUB-E2E-CONFIRM-01",
            OrderId = orderId,
            BuyerId = _buyerId,
            SellerId = _sellerA,
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            ShippingAddress = new ShippingAddressSnapshot("Buyer", "0901234567", "Street", "Ward", "District", "Province", 1442, "20101", "Full Address"),
            ContactPhone = "0901234567",
            ItemsAmount = 250000m,
            TotalAmount = 250000m,
            SellerReceiveAmount = 237500m,
            PaymentDeadlineAt = DateTimeOffset.UtcNow.AddMinutes(15),
            DeliveredAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        order.SubOrders.Add(subOrder);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();

        // Buyer bấm xác nhận đã nhận hàng
        var confirmResult = await orderService.BuyerConfirmSubOrderReceiptAsync(_buyerId, subOrderId, CancellationToken.None);

        // Assert
        confirmResult.Status.Should().Be("Completed");
        confirmResult.EscrowSettled.Should().BeTrue();
        confirmResult.AmountSettled.Should().Be(237500m);

        var updatedSubOrder = await _dbContext.SubOrders.FindAsync(subOrderId);
        updatedSubOrder!.Status.Should().Be(SubOrderStatus.Completed);
        updatedSubOrder.CompletedAt.Should().NotBeNull();

        // Verify: Lệnh giải ngân sang ví Seller được kích hoạt
        _mockWalletClient.Verify(w => w.SettleSellerSubOrderAsync(
            subOrderId,
            _sellerA,
            237500m,
            $"settle-{subOrderId}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private class FailingDbCommerce : TestCommerceDbContext
    {
        public FailingDbCommerce(DbContextOptions<TestCommerceDbContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated PostgreSQL connection drop or constraint failure during save.");
        }
    }
}
