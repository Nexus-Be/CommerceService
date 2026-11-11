namespace Commerce.Api.Workers;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;

public sealed class PaymentTimeoutWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentTimeoutWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public PaymentTimeoutWorker(
        IServiceProvider serviceProvider,
        ILogger<PaymentTimeoutWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[PaymentTimeoutWorker] Background service started. Interval: {Interval}", _checkInterval);

        using var timer = new PeriodicTimer(_checkInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessExpiredOrdersAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "[PaymentTimeoutWorker] Unhandled error during expired orders processing cycle.");
            }
        }

        _logger.LogInformation("[PaymentTimeoutWorker] Background service is stopping.");
    }

    internal async Task ProcessExpiredOrdersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ICommerceDbContext>();
        var fulfillmentClient = scope.ServiceProvider.GetRequiredService<IFulfillmentClient>();
        var auctionClient = scope.ServiceProvider.GetService<IAuctionClient>();
        var walletClient = scope.ServiceProvider.GetService<IWalletClient>();

        var now = DateTimeOffset.UtcNow;

        var expiredOrders = await dbContext.Orders
            .Include(o => o.SubOrders)
            .Where(o => o.Status == OrderStatus.PendingPayment && o.PaymentDeadlineAt < now)
            .OrderBy(o => o.PaymentDeadlineAt)
            .Take(50)
            .ToListAsync(ct);

        if (expiredOrders.Count > 0)
        {
            _logger.LogInformation("[PaymentTimeoutWorker] Found {Count} expired pending-payment orders to cancel.", expiredOrders.Count);
            var orderIds = expiredOrders.Select(o => o.Id).ToList();

            bool stockReleased = false;
            try
            {
                var releaseResult = await fulfillmentClient.BatchReleaseStockAsync(orderIds, ct);
                stockReleased = releaseResult.IsSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PaymentTimeoutWorker] Outbound stock release failed for {Count} expired orders; setting InventoryReleased = false.", orderIds.Count);
            }

            try
            {
                // 1. Cập nhật trạng thái Order và SubOrder trong bộ nhớ
                foreach (var order in expiredOrders)
                {
                    order.Status = OrderStatus.Cancelled;

                    foreach (var so in order.SubOrders)
                    {
                        so.Status = SubOrderStatus.Cancelled;
                        so.CancelledAt = now;

                        dbContext.OrderCancellations.Add(new OrderCancellation
                        {
                            Id = Guid.NewGuid(),
                            SubOrderId = so.Id,
                            CancelledBy = null,
                            ActorRole = OrderActorRole.System,
                            ReasonCode = CancellationReasonCode.PaymentTimeout,
                            ReasonNote = "Payment timeout expired after 15 minutes.",
                            WasPaid = false,
                            InventoryReleased = stockReleased
                        });
                    }
                }

                // 2. Batch giải phóng voucher đã giữ chỗ cho tất cả các đơn trong đợt quét
                var usages = await dbContext.DiscountCodeUsages
                    .Where(u => u.OrderId.HasValue && orderIds.Contains(u.OrderId.Value) && u.Status == DiscountUsageStatus.Reserved)
                    .ToListAsync(ct);

                if (usages.Count > 0)
                {
                    var discountCodeIds = usages.Select(u => u.DiscountCodeId).Distinct().ToList();
                    var discountCodes = await dbContext.DiscountCodes
                        .Where(d => discountCodeIds.Contains(d.Id))
                        .ToDictionaryAsync(d => d.Id, ct);

                    foreach (var usage in usages)
                    {
                        usage.Status = DiscountUsageStatus.Released;
                        usage.ReleasedAt = now;

                        if (discountCodes.TryGetValue(usage.DiscountCodeId, out var code))
                        {
                            code.ReservedCount = Math.Max(0, code.ReservedCount - 1);
                        }
                    }
                }

                // 3. Batch commit toàn bộ thay đổi database bằng 1 lần SaveChangesAsync duy nhất
                await dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("[PaymentTimeoutWorker] Successfully processed cancellations for {Count} expired orders (InventoryReleased: {InventoryReleased}).", expiredOrders.Count, stockReleased);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PaymentTimeoutWorker] Error during batch cancellation of expired orders.");
            }
        }

        // 2. Quét đơn đấu giá quá hạn thanh toán 24 giờ
        var expiredAuctionOrders = await dbContext.AuctionOrders
            .Where(a => a.Status == SubOrderStatus.PendingPayment && a.PaymentDeadlineAt < now)
            .OrderBy(a => a.PaymentDeadlineAt)
            .Take(50)
            .ToListAsync(ct);

        if (expiredAuctionOrders.Count > 0)
        {
            try
            {
                foreach (var auctionOrder in expiredAuctionOrders)
                {
                    auctionOrder.Status = SubOrderStatus.Cancelled;
                    auctionOrder.CancelledAt = now;

                    dbContext.OrderCancellations.Add(new OrderCancellation
                    {
                        Id = Guid.NewGuid(),
                        AuctionOrderId = auctionOrder.Id,
                        SubOrderId = null,
                        CancelledBy = null,
                        ActorRole = OrderActorRole.System,
                        ReasonCode = CancellationReasonCode.PaymentTimeout,
                        ReasonNote = "Auction payment deadline expired after 24 hours.",
                        WasPaid = false,
                        InventoryReleased = true
                    });

                    dbContext.OrderStatusHistories.Add(new OrderStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        AuctionOrderId = auctionOrder.Id,
                        FromStatus = SubOrderStatus.PendingPayment,
                        ToStatus = SubOrderStatus.Cancelled,
                        ActorRole = OrderActorRole.System,
                        Reason = "Auction payment deadline expired after 24 hours."
                    });
                }

                await dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("[PaymentTimeoutWorker] Auto-cancelled {Count} expired AuctionOrders in batch.", expiredAuctionOrders.Count);

                if (auctionClient != null)
                {
                    foreach (var auctionOrder in expiredAuctionOrders)
                    {
                        try
                        {
                            await auctionClient.NotifyPaymentStatusAsync(
                                auctionOrder.AuctionId,
                                new AuctionPaymentStatusCallbackRequest(
                                    OrderId: auctionOrder.Id,
                                    WinnerId: auctionOrder.BuyerId,
                                    Status: "PaymentTimedOut",
                                    EventId: Guid.NewGuid()
                                ),
                                ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[PaymentTimeoutWorker] Failed to dispatch PaymentTimedOut callback for AuctionOrder '{OrderId}'.", auctionOrder.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PaymentTimeoutWorker] Failed to cancel expired AuctionOrders batch.");
            }
        }

        // 3. Quét đơn đấu giá đã thanh toán nhưng Seller không duyệt sau 24 giờ
        var expiredSellerOrders = await dbContext.AuctionOrders
            .Where(a => a.Status == SubOrderStatus.Paid && a.SellerRespondDeadlineAt.HasValue && a.SellerRespondDeadlineAt < now)
            .OrderBy(a => a.SellerRespondDeadlineAt)
            .Take(50)
            .ToListAsync(ct);

        if (expiredSellerOrders.Count > 0)
        {
            try
            {
                foreach (var auctionOrder in expiredSellerOrders)
                {
                    auctionOrder.Status = SubOrderStatus.Cancelled;
                    auctionOrder.CancelledAt = now;

                    var cancellationId = Guid.NewGuid();
                    dbContext.OrderCancellations.Add(new OrderCancellation
                    {
                        Id = cancellationId,
                        AuctionOrderId = auctionOrder.Id,
                        SubOrderId = null,
                        CancelledBy = null,
                        ActorRole = OrderActorRole.System,
                        ReasonCode = CancellationReasonCode.SellerRejected,
                        ReasonNote = "Seller response deadline expired after 24 hours.",
                        WasPaid = true,
                        InventoryReleased = true
                    });

                    var refundAmount = auctionOrder.TotalAmount ?? auctionOrder.WinningAmount;
                    bool refundSuccess = false;
                    if (walletClient != null)
                    {
                        try
                        {
                            refundSuccess = await walletClient.RefundOrderPaymentAsync(
                                auctionOrder.Id,
                                auctionOrder.BuyerId.ToString(),
                                refundAmount,
                                "Seller response deadline expired after 24 hours.",
                                null,
                                ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[PaymentTimeoutWorker] Failed to refund wallet for expired seller AuctionOrder '{OrderId}'.", auctionOrder.Id);
                        }
                    }

                    dbContext.OrderRefunds.Add(new OrderRefund
                    {
                        Id = Guid.NewGuid(),
                        AuctionOrderId = auctionOrder.Id,
                        CancellationId = cancellationId,
                        RefundType = RefundType.Full,
                        Amount = refundAmount,
                        Status = refundSuccess ? RefundStatus.Completed : RefundStatus.Requested,
                        ReasonCode = "SellerExpired",
                        ReasonNote = "Seller response deadline expired after 24 hours.",
                        ActorRole = OrderActorRole.System,
                        RequestedBy = null,
                        CompletedAt = refundSuccess ? now : null
                    });

                    dbContext.OrderStatusHistories.Add(new OrderStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        AuctionOrderId = auctionOrder.Id,
                        FromStatus = SubOrderStatus.Paid,
                        ToStatus = SubOrderStatus.Cancelled,
                        ActorRole = OrderActorRole.System,
                        Reason = "Seller response deadline expired after 24 hours."
                    });
                }

                await dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("[PaymentTimeoutWorker] Auto-cancelled and refunded {Count} seller-expired AuctionOrders.", expiredSellerOrders.Count);

                if (auctionClient != null)
                {
                    foreach (var auctionOrder in expiredSellerOrders)
                    {
                        try
                        {
                            await auctionClient.NotifyPaymentStatusAsync(
                                auctionOrder.AuctionId,
                                new AuctionPaymentStatusCallbackRequest(
                                    OrderId: auctionOrder.Id,
                                    WinnerId: auctionOrder.BuyerId,
                                    Status: "SellerExpired",
                                    EventId: Guid.NewGuid()
                                ),
                                ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[PaymentTimeoutWorker] Failed to dispatch SellerExpired callback for AuctionOrder '{OrderId}'.", auctionOrder.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PaymentTimeoutWorker] Failed to process seller-expired AuctionOrders batch.");
            }
        }
    }
}
