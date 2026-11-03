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
                            InventoryReleased = true
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
                _logger.LogInformation("[PaymentTimeoutWorker] Successfully persisted cancellations for {Count} expired orders to DB.", expiredOrders.Count);

                // 4. Batch nhả kho tại Fulfillment Service bằng 1 cuộc gọi duy nhất (Chống N+1 HTTP calls)
                await fulfillmentClient.BatchReleaseStockAsync(orderIds, ct);
                _logger.LogInformation("[PaymentTimeoutWorker] Batch released stock at Fulfillment for {Count} orders: [{OrderIds}].",
                    orderIds.Count, string.Join(", ", orderIds));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PaymentTimeoutWorker] Error during batch cancellation of expired orders.");
            }
        }

        // 2. Quét đơn đấu giá quá hạn 24 giờ
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
                        SubOrderId = auctionOrder.Id,
                        CancelledBy = null,
                        ActorRole = OrderActorRole.System,
                        ReasonCode = CancellationReasonCode.PaymentTimeout,
                        ReasonNote = "Auction payment deadline expired after 24 hours.",
                        WasPaid = false,
                        InventoryReleased = true
                    });
                }

                await dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("[PaymentTimeoutWorker] Auto-cancelled {Count} expired AuctionOrders in batch.", expiredAuctionOrders.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PaymentTimeoutWorker] Failed to cancel expired AuctionOrders batch.");
            }
        }
    }
}
