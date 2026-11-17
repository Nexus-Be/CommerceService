namespace Commerce.UnitTests.Application;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Commerce.Api.Workers;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Commerce.UnitTests.Common;

public class PaymentTimeoutWorkerTests : IDisposable
{
    private readonly TestCommerceDbContext _dbContext;
    private readonly Mock<IFulfillmentClient> _mockFulfillmentClient = new();
    private readonly Mock<IAuctionClient> _mockAuctionClient = new();
    private readonly Mock<IWalletClient> _mockWalletClient = new();
    private readonly Mock<IUserClient> _mockUserClient = new();
    private readonly Mock<ICatalogClient> _mockCatalogClient = new();
    private readonly Mock<IServiceProvider> _mockServiceProvider = new();
    private readonly Mock<IServiceScope> _mockServiceScope = new();
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory = new();

    public PaymentTimeoutWorkerTests()
    {
        _dbContext = TestDbContextFactory.CreateInMemoryDbContext();

        _mockFulfillmentClient
            .Setup(f => f.BatchReleaseStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid> ids, CancellationToken _) => new BatchReleaseStockResult(ids, new List<Guid>(), true));

        var scopedServices = new Mock<IServiceProvider>();
        scopedServices.Setup(s => s.GetService(typeof(ICommerceDbContext))).Returns(_dbContext);
        scopedServices.Setup(s => s.GetService(typeof(IFulfillmentClient))).Returns(_mockFulfillmentClient.Object);
        scopedServices.Setup(s => s.GetService(typeof(IAuctionClient))).Returns(_mockAuctionClient.Object);
        scopedServices.Setup(s => s.GetService(typeof(IWalletClient))).Returns(_mockWalletClient.Object);
        scopedServices.Setup(s => s.GetService(typeof(IUserClient))).Returns(_mockUserClient.Object);
        scopedServices.Setup(s => s.GetService(typeof(ICatalogClient))).Returns(_mockCatalogClient.Object);

        _mockServiceScope.Setup(s => s.ServiceProvider).Returns(scopedServices.Object);

        _mockScopeFactory.Setup(s => s.CreateScope()).Returns(_mockServiceScope.Object);
        _mockServiceProvider.Setup(s => s.GetService(typeof(IServiceScopeFactory))).Returns(_mockScopeFactory.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task ProcessExpiredOrdersAsync_ShouldActivelyCallBatchReleaseStock_AndSetInventoryReleasedTrue_WhenSuccessful()
    {
        // Arrange: Tạo 2 đơn hàng quá hạn thanh toán
        var now = DateTimeOffset.UtcNow;
        var orderId1 = Guid.NewGuid();
        var buyerId1 = Guid.NewGuid();
        var expiredOrder1 = new Order
        {
            Id = orderId1,
            BuyerId = buyerId1,
            Status = OrderStatus.PendingPayment,
            TotalAmount = 70000m,
            PaymentDeadlineAt = now.AddMinutes(-10),
            SubOrders = new List<SubOrder>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderNo = "SO-TEST-001",
                    OrderId = orderId1,
                    BuyerId = buyerId1,
                    SellerId = Guid.NewGuid(),
                    Status = SubOrderStatus.PendingPayment,
                    PaymentMethod = PaymentMethod.Wallet,
                    ContactPhone = "0901234567",
                    PaymentDeadlineAt = now.AddMinutes(-10)
                }
            }
        };

        var orderId2 = Guid.NewGuid();
        var buyerId2 = Guid.NewGuid();
        var expiredOrder2 = new Order
        {
            Id = orderId2,
            BuyerId = buyerId2,
            Status = OrderStatus.PendingPayment,
            TotalAmount = 130000m,
            PaymentDeadlineAt = now.AddMinutes(-5),
            SubOrders = new List<SubOrder>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderNo = "SO-TEST-002",
                    OrderId = orderId2,
                    BuyerId = buyerId2,
                    SellerId = Guid.NewGuid(),
                    Status = SubOrderStatus.PendingPayment,
                    PaymentMethod = PaymentMethod.Cod,
                    ContactPhone = "0912345678",
                    PaymentDeadlineAt = now.AddMinutes(-5)
                }
            }
        };

        // Đơn chưa quá hạn (không được hủy)
        var validOrder = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            Status = OrderStatus.PendingPayment,
            TotalAmount = 50000m,
            PaymentDeadlineAt = now.AddMinutes(10)
        };

        _dbContext.Orders.AddRange(expiredOrder1, expiredOrder2, validOrder);
        await _dbContext.SaveChangesAsync();

        var worker = new PaymentTimeoutWorker(_mockServiceProvider.Object, NullLogger<PaymentTimeoutWorker>.Instance);

        // Act
        await worker.ProcessExpiredOrdersAsync(CancellationToken.None);

        // Assert
        var order1InDb = await _dbContext.Orders.Include(o => o.SubOrders).FirstAsync(o => o.Id == expiredOrder1.Id);
        var order2InDb = await _dbContext.Orders.Include(o => o.SubOrders).FirstAsync(o => o.Id == expiredOrder2.Id);
        var validOrderInDb = await _dbContext.Orders.FirstAsync(o => o.Id == validOrder.Id);

        order1InDb.Status.Should().Be(OrderStatus.Cancelled);
        order1InDb.SubOrders.First().Status.Should().Be(SubOrderStatus.Cancelled);

        order2InDb.Status.Should().Be(OrderStatus.Cancelled);
        order2InDb.SubOrders.First().Status.Should().Be(SubOrderStatus.Cancelled);

        validOrderInDb.Status.Should().Be(OrderStatus.PendingPayment);

        var cancellations = await _dbContext.OrderCancellations.ToListAsync();
        cancellations.Should().HaveCount(2);
        cancellations.Should().OnlyContain(c => c.InventoryReleased == true);

        // VERIFY: Chủ động gọi batch release kho tới FulfillmentService
        _mockFulfillmentClient.Verify(
            f => f.BatchReleaseStockAsync(
                It.Is<List<Guid>>(ids => ids.Count == 2 && ids.Contains(orderId1) && ids.Contains(orderId2)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // VERIFY: Không gọi đơn lẻ ReleaseStockAsync (tránh N+1)
        _mockFulfillmentClient.Verify(
            f => f.ReleaseStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // VERIFY: Tuyệt đối không gọi sang WalletClient vì đơn hết hạn thanh toán chưa từng bị trừ tiền
        _mockWalletClient.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessExpiredOrdersAsync_ShouldNeverInvokeWalletClient_WhenCancellingExpiredOrders()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var orderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var expiredOrder = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.PendingPayment,
            TotalAmount = 250000m,
            PaymentDeadlineAt = now.AddMinutes(-10),
            SubOrders = new List<SubOrder>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderNo = "SO-TIMEOUT-NOWALLET-001",
                    OrderId = orderId,
                    BuyerId = buyerId,
                    SellerId = Guid.NewGuid(),
                    Status = SubOrderStatus.PendingPayment,
                    PaymentMethod = PaymentMethod.Wallet,
                    ContactPhone = "0909999999",
                    PaymentDeadlineAt = now.AddMinutes(-10)
                }
            }
        };

        _dbContext.Orders.Add(expiredOrder);
        await _dbContext.SaveChangesAsync();

        var worker = new PaymentTimeoutWorker(_mockServiceProvider.Object, NullLogger<PaymentTimeoutWorker>.Instance);

        // Act
        await worker.ProcessExpiredOrdersAsync(CancellationToken.None);

        // Assert
        var cancelledOrder = await _dbContext.Orders.Include(o => o.SubOrders).FirstAsync(o => o.Id == orderId);
        cancelledOrder.Status.Should().Be(OrderStatus.Cancelled);
        cancelledOrder.SubOrders.First().Status.Should().Be(SubOrderStatus.Cancelled);

        // VERIFY: _mockWalletClient received 0 calls of any kind
        _mockWalletClient.Invocations.Should().BeEmpty();
        _mockWalletClient.Verify(w => w.RefundOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockWalletClient.Verify(w => w.RefundSubOrderPaymentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockWalletClient.Verify(w => w.SettleSellerSubOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredOrdersAsync_ShouldSetInventoryReleasedFalse_WhenBatchReleaseThrowsException()
    {
        // Arrange: Setup FulfillmentClient ném lỗi 503 khi nhả kho hàng loạt
        _mockFulfillmentClient
            .Setup(f => f.BatchReleaseStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CommerceDomainException("STOCK_RELEASE_UNAVAILABLE", "Warehouse inventory service is temporarily unreachable during batch release.", 503));

        var now = DateTimeOffset.UtcNow;
        var orderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var expiredOrder = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.PendingPayment,
            TotalAmount = 70000m,
            PaymentDeadlineAt = now.AddMinutes(-10),
            SubOrders = new List<SubOrder>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderNo = "SO-FAIL-001",
                    OrderId = orderId,
                    BuyerId = buyerId,
                    SellerId = Guid.NewGuid(),
                    Status = SubOrderStatus.PendingPayment,
                    PaymentMethod = PaymentMethod.Wallet,
                    ContactPhone = "0901234567",
                    PaymentDeadlineAt = now.AddMinutes(-10)
                }
            }
        };

        _dbContext.Orders.Add(expiredOrder);
        await _dbContext.SaveChangesAsync();

        var worker = new PaymentTimeoutWorker(_mockServiceProvider.Object, NullLogger<PaymentTimeoutWorker>.Instance);

        // Act: Chạy worker
        await worker.ProcessExpiredOrdersAsync(CancellationToken.None);

        // Assert: Đơn vẫn hủy thành công nhưng cờ InventoryReleased được ghi nhận là false (resilience flag)
        var orderInDb = await _dbContext.Orders.Include(o => o.SubOrders).FirstAsync(o => o.Id == orderId);
        orderInDb.Status.Should().Be(OrderStatus.Cancelled);
        orderInDb.SubOrders.First().Status.Should().Be(SubOrderStatus.Cancelled);

        var cancellations = await _dbContext.OrderCancellations.ToListAsync();
        cancellations.Should().HaveCount(1);
        cancellations.First().InventoryReleased.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessExpiredOrdersAsync_ShouldDoNothing_WhenNoOrdersAreExpired()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var validOrder = new Order
        {
            Id = Guid.NewGuid(),
            BuyerId = Guid.NewGuid(),
            Status = OrderStatus.PendingPayment,
            TotalAmount = 50000m,
            PaymentDeadlineAt = now.AddMinutes(10)
        };

        _dbContext.Orders.Add(validOrder);
        await _dbContext.SaveChangesAsync();

        var worker = new PaymentTimeoutWorker(_mockServiceProvider.Object, NullLogger<PaymentTimeoutWorker>.Instance);

        // Act
        await worker.ProcessExpiredOrdersAsync(CancellationToken.None);

        // Assert: Không gọi batch release khi không có đơn quá hạn
        _mockFulfillmentClient.Verify(
            f => f.BatchReleaseStockAsync(It.IsAny<List<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredOrdersAsync_WhenAuctionOrderPaymentExpired_SetsAuctionOrderIdAndNullSubOrderIdAndSendsPaymentTimedOutCallback()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var orderId = Guid.NewGuid();
        var auctionId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();

        var expiredAuctionOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00001",
            AuctionId = auctionId,
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Antique",
            WinningAmount = 1000000m,
            SellerReceiveAmount = 950000m,
            PlatformFeeAmount = 50000m,
            Status = SubOrderStatus.PendingPayment,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = now.AddMinutes(-10)
        };

        _dbContext.AuctionOrders.Add(expiredAuctionOrder);
        await _dbContext.SaveChangesAsync();

        _mockAuctionClient
            .Setup(a => a.NotifyPaymentStatusAsync(auctionId, It.Is<AuctionPaymentStatusCallbackRequest>(r =>
                r.OrderId == orderId &&
                r.WinnerId == buyerId &&
                r.Status == "PaymentTimedOut" &&
                r.OrderSource == "WINNER" &&
                r.EventId.HasValue), default))
            .ReturnsAsync(true);

        var worker = new PaymentTimeoutWorker(_mockServiceProvider.Object, NullLogger<PaymentTimeoutWorker>.Instance);

        // Act
        await worker.ProcessExpiredOrdersAsync(CancellationToken.None);

        // Assert
        var orderInDb = await _dbContext.AuctionOrders.FindAsync(orderId);
        orderInDb!.Status.Should().Be(SubOrderStatus.Cancelled);
        orderInDb.CancelledAt.Should().NotBeNull();

        var cancellations = await _dbContext.OrderCancellations.Where(c => c.AuctionOrderId == orderId).ToListAsync();
        cancellations.Should().HaveCount(1);
        cancellations[0].AuctionOrderId.Should().Be(orderId);
        cancellations[0].SubOrderId.Should().BeNull(); // REMEDIATION VERIFIED!
        cancellations[0].ActorRole.Should().Be(OrderActorRole.System);
        cancellations[0].ReasonCode.Should().Be(CancellationReasonCode.PaymentTimeout);

        _mockAuctionClient.Verify(a => a.NotifyPaymentStatusAsync(auctionId, It.IsAny<AuctionPaymentStatusCallbackRequest>(), default), Times.Once);
        // Verify: Commerce KHÔNG tự gọi UserService trừ điểm Buyer để tránh trừ trùng lặp (Auction Service gọi API 20 xử lý việc này)
        _mockUserClient.Verify(u => u.CreatePenaltyAsync(It.IsAny<UserPenaltyRequest>(), default), Times.Never);
    }

    [Fact]
    public async Task ProcessExpiredOrdersAsync_WhenSellerRespondTimeoutExpired_CancelsPaidOrderRefundsBuyerAndSendsSellerExpiredCallback()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var orderId = Guid.NewGuid();
        var auctionId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();

        var expiredPaidOrder = new AuctionOrder
        {
            Id = orderId,
            OrderNo = "AO-20261111-00002",
            AuctionId = auctionId,
            BuyerId = buyerId,
            SellerId = sellerId,
            WinningBidId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            ProductNameSnapshot = "Rare Antique",
            WinningAmount = 1000000m,
            TotalAmount = 1050000m,
            SellerReceiveAmount = 950000m,
            PlatformFeeAmount = 50000m,
            Status = SubOrderStatus.Paid,
            PaymentMethod = PaymentMethod.Wallet,
            PaymentDeadlineAt = now.AddHours(-10),
            PaidAt = now.AddHours(-25),
            SellerRespondDeadlineAt = now.AddMinutes(-5) // Expired 24h respond deadline
        };

        _dbContext.AuctionOrders.Add(expiredPaidOrder);
        await _dbContext.SaveChangesAsync();

        _mockWalletClient
            .Setup(w => w.RefundOrderPaymentAsync(orderId, buyerId.ToString(), 1050000m, It.IsAny<string>(), null, default))
            .ReturnsAsync(true);

        _mockAuctionClient
            .Setup(a => a.NotifyPaymentStatusAsync(auctionId, It.Is<AuctionPaymentStatusCallbackRequest>(r =>
                r.OrderId == orderId &&
                r.WinnerId == buyerId &&
                r.Status == "SellerExpired" &&
                r.OrderSource == "WINNER" &&
                r.EventId.HasValue), default))
            .ReturnsAsync(true);

        var worker = new PaymentTimeoutWorker(_mockServiceProvider.Object, NullLogger<PaymentTimeoutWorker>.Instance);

        // Act
        await worker.ProcessExpiredOrdersAsync(CancellationToken.None);

        // Assert
        var orderInDb = await _dbContext.AuctionOrders.FindAsync(orderId);
        orderInDb!.Status.Should().Be(SubOrderStatus.Cancelled);
        orderInDb.CancelledAt.Should().NotBeNull();

        var cancellations = await _dbContext.OrderCancellations.Where(c => c.AuctionOrderId == orderId).ToListAsync();
        cancellations.Should().HaveCount(1);
        cancellations[0].AuctionOrderId.Should().Be(orderId);
        cancellations[0].SubOrderId.Should().BeNull();
        cancellations[0].ActorRole.Should().Be(OrderActorRole.System);
        cancellations[0].ReasonCode.Should().Be(CancellationReasonCode.SellerRejected);
        cancellations[0].WasPaid.Should().BeTrue();

        var refunds = await _dbContext.OrderRefunds.Where(r => r.AuctionOrderId == orderId).ToListAsync();
        refunds.Should().HaveCount(1);
        refunds[0].Amount.Should().Be(1050000m);
        refunds[0].Status.Should().Be(RefundStatus.Completed);

        _mockWalletClient.Verify(w => w.RefundOrderPaymentAsync(orderId, buyerId.ToString(), 1050000m, It.IsAny<string>(), null, default), Times.Once);
        _mockAuctionClient.Verify(a => a.NotifyPaymentStatusAsync(auctionId, It.IsAny<AuctionPaymentStatusCallbackRequest>(), default), Times.Once);
        _mockUserClient.Verify(u => u.CreatePenaltyAsync(It.Is<UserPenaltyRequest>(p =>
            p.UserId == sellerId &&
            p.TargetRole == "Seller" &&
            p.ViolationType == "SellerResponseTimeout" &&
            p.ReferenceType == "AuctionOrder" &&
            p.ReferenceId == "AO-20261111-00002" &&
            p.CustomScoreDeduction == 5 &&
            p.PolicyClauseCode == "POLICY-AUC-SELLER-TIMEOUT"), default), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredOrdersAsync_ShouldAutoCompleteDeliveredSubOrders_WhenPastInspectionWindow()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        var recentSubOrderId = Guid.NewGuid();

        var masterOrder = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 500000m,
            PaymentDeadlineAt = now.AddMinutes(15),
            SubOrders = new List<SubOrder>()
        };

        var expiredDeliveredSubOrder = new SubOrder
        {
            Id = subOrderId,
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            SubOrderNo = "SO-DELIV-001",
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            PaymentDeadlineAt = now.AddMinutes(15),
            DeliveredAt = now.AddMinutes(-4325), // Older than 4320 minutes (3 days)
            TotalAmount = 250000m,
            SellerReceiveAmount = 230000m,
            Items = new List<SubOrderItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = subOrderId,
                    SellerId = sellerId,
                    ProductId = Guid.NewGuid(),
                    SkuId = Guid.NewGuid(),
                    ProductNameSnapshot = "Product 1",
                    Quantity = 1,
                    UnitPrice = 250000m,
                    UnitPriceAfterDiscount = 250000m,
                    LineTotal = 250000m
                }
            }
        };

        var recentDeliveredSubOrder = new SubOrder
        {
            Id = recentSubOrderId,
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            SubOrderNo = "SO-DELIV-002",
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Wallet,
            ContactPhone = "0901234567",
            PaymentDeadlineAt = now.AddMinutes(15),
            DeliveredAt = now.AddMinutes(-60), // Delivered 1 hour ago (within inspection window)
            TotalAmount = 250000m,
            SellerReceiveAmount = 230000m,
            Items = new List<SubOrderItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = recentSubOrderId,
                    SellerId = sellerId,
                    ProductId = Guid.NewGuid(),
                    SkuId = Guid.NewGuid(),
                    ProductNameSnapshot = "Product 2",
                    Quantity = 1,
                    UnitPrice = 250000m,
                    UnitPriceAfterDiscount = 250000m,
                    LineTotal = 250000m
                }
            }
        };

        masterOrder.SubOrders.Add(expiredDeliveredSubOrder);
        masterOrder.SubOrders.Add(recentDeliveredSubOrder);

        _dbContext.Orders.Add(masterOrder);
        _dbContext.SubOrders.AddRange(expiredDeliveredSubOrder, recentDeliveredSubOrder);
        await _dbContext.SaveChangesAsync();

        _mockWalletClient
            .Setup(w => w.SettleSellerSubOrderAsync(subOrderId, sellerId, 230000m, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockUserClient
            .Setup(u => u.AdjustReputationAsync(It.IsAny<ReputationAdjustRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReputationAdjustResponse(Guid.NewGuid(), "Seller", false, false, 50, 51, 1, "Standard"));

        var worker = new PaymentTimeoutWorker(_mockServiceProvider.Object, NullLogger<PaymentTimeoutWorker>.Instance);

        // Act
        await worker.ProcessExpiredOrdersAsync(CancellationToken.None);

        // Assert: Expired suborder should be Completed, recent suborder should stay Delivered
        var dbExpiredSubOrder = await _dbContext.SubOrders.FindAsync(subOrderId);
        dbExpiredSubOrder!.Status.Should().Be(SubOrderStatus.Completed);
        dbExpiredSubOrder.CompletedAt.Should().NotBeNull();

        var dbRecentSubOrder = await _dbContext.SubOrders.FindAsync(recentSubOrderId);
        dbRecentSubOrder!.Status.Should().Be(SubOrderStatus.Delivered);

        var dbMasterOrder = await _dbContext.Orders.FindAsync(orderId);
        dbMasterOrder!.Status.Should().Be(OrderStatus.Paid); // Still Paid because recentSubOrder is not completed yet

        _mockWalletClient.Verify(w => w.SettleSellerSubOrderAsync(subOrderId, sellerId, 230000m, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockUserClient.Verify(u => u.AdjustReputationAsync(It.Is<ReputationAdjustRequest>(r => r.UserId == sellerId && r.ScoreDelta == 1), It.IsAny<CancellationToken>()), Times.Once);
        _mockCatalogClient.Verify(c => c.SyncPurchasesBatchAsync(buyerId, subOrderId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessExpiredOrdersAsync_ShouldSkipWalletSettlement_WhenDeliveredSubOrderIsCod()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        var masterOrder = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Paid,
            TotalAmount = 250000m,
            PaymentDeadlineAt = now.AddMinutes(15),
            SubOrders = new List<SubOrder>()
        };

        var expiredCodSubOrder = new SubOrder
        {
            Id = subOrderId,
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = sellerId,
            SubOrderNo = "SO-COD-001",
            Status = SubOrderStatus.Delivered,
            PaymentMethod = PaymentMethod.Cod, // COD
            ContactPhone = "0901234567",
            PaymentDeadlineAt = now.AddMinutes(15),
            DeliveredAt = now.AddMinutes(-5000), // Past 4320 minutes
            TotalAmount = 250000m,
            SellerReceiveAmount = 230000m,
            Items = new List<SubOrderItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    SubOrderId = subOrderId,
                    SellerId = sellerId,
                    ProductId = Guid.NewGuid(),
                    SkuId = Guid.NewGuid(),
                    ProductNameSnapshot = "Product 1",
                    Quantity = 1,
                    UnitPrice = 250000m,
                    UnitPriceAfterDiscount = 250000m,
                    LineTotal = 250000m
                }
            }
        };

        masterOrder.SubOrders.Add(expiredCodSubOrder);

        _dbContext.Orders.Add(masterOrder);
        _dbContext.SubOrders.Add(expiredCodSubOrder);
        await _dbContext.SaveChangesAsync();

        var worker = new PaymentTimeoutWorker(_mockServiceProvider.Object, NullLogger<PaymentTimeoutWorker>.Instance);

        // Act
        await worker.ProcessExpiredOrdersAsync(CancellationToken.None);

        // Assert
        var dbSubOrder = await _dbContext.SubOrders.FindAsync(subOrderId);
        dbSubOrder!.Status.Should().Be(SubOrderStatus.Completed);

        var dbMasterOrder = await _dbContext.Orders.FindAsync(orderId);
        dbMasterOrder!.Status.Should().Be(OrderStatus.Completed); // Sole sub-order is completed

        // Wallet settlement MUST be skipped for COD
        _mockWalletClient.Verify(w => w.SettleSellerSubOrderAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
