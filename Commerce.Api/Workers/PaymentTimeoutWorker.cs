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

    private async Task ProcessExpiredOrdersAsync(CancellationToken ct)
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

        if (expiredOrders.Count == 0)
        {
            return;
        }

        _logger.LogInformation("[PaymentTimeoutWorker] Found {Count} expired pending-payment orders to cancel.", expiredOrders.Count);

        foreach (var order in expiredOrders)
        {
            try
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

                // Giải phóng voucher đã giữ chỗ nếu có
                var usages = await dbContext.DiscountCodeUsages
                    .Where(u => u.OrderId == order.Id && u.Status == DiscountUsageStatus.Reserved)
                    .ToListAsync(ct);

                foreach (var usage in usages)
                {
                    usage.Status = DiscountUsageStatus.Released;
                    usage.ReleasedAt = now;

                    var code = await dbContext.DiscountCodes.FindAsync(new object[] { usage.DiscountCodeId }, ct);
                    if (code != null)
                    {
                        code.ReservedCount = Math.Max(0, code.ReservedCount - 1);
                    }
                }

                await dbContext.SaveChangesAsync(ct);

                // Nhả kho tại Fulfillment Service
                await fulfillmentClient.ReleaseStockAsync(order.Id, ct);

                _logger.LogInformation("[PaymentTimeoutWorker] Auto-cancelled Order '{OrderId}' and released reserved stock.", order.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PaymentTimeoutWorker] Failed to cancel expired Order '{OrderId}'.", order.Id);
            }
        }

        // 2. Quét đơn đấu giá quá hạn 24 giờ
        var expiredAuctionOrders = await dbContext.AuctionOrders
            .Where(a => a.Status == SubOrderStatus.PendingPayment && a.PaymentDeadlineAt < now)
            .OrderBy(a => a.PaymentDeadlineAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var auctionOrder in expiredAuctionOrders)
        {
            try
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

                await dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("[PaymentTimeoutWorker] Auto-cancelled expired AuctionOrder '{AuctionOrderId}'.", auctionOrder.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PaymentTimeoutWorker] Failed to cancel expired AuctionOrder '{AuctionOrderId}'.", auctionOrder.Id);
            }
        }
    }
}
