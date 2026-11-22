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
        var settlementService = scope.ServiceProvider.GetService<ISettlementService>();
        var userClient = scope.ServiceProvider.GetService<IUserClient>();
        var catalogClient = scope.ServiceProvider.GetService<ICatalogClient>();
        var configuration = scope.ServiceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
        var voucherLifecycleService = scope.ServiceProvider.GetService<Commerce.Application.Services.Discount.IVoucherLifecycleService>();

        var now = DateTimeOffset.UtcNow;

        // Reputation penalties owed by buyers who refused a delivery are retried here until User confirms them. A failure of this step
        // must never stop the payment timeouts below.
        var refusalPenalties = scope.ServiceProvider.GetService<Commerce.Application.Services.Delivery.BuyerRefusalPenaltyService>();
        if (refusalPenalties != null)
        {
            try
            {
                await refusalPenalties.RetryPendingAsync(50, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[PaymentTimeoutWorker] Retrying the pending buyer-refusal penalties failed.");
            }
        }

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
                        // A sub-order that was already cancelled (for example a COD sub-order the buyer or seller cancelled while the parent
                        // order stayed pending) already has its cancellation record. Adding a second one violates the unique index and used to
                        // make the whole batch fail, so one such order stopped every payment timeout.
                        if (so.Status == SubOrderStatus.Cancelled) continue;

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
                var callbacks = new System.Collections.Generic.Dictionary<Guid, OrderStatusHistory>();
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

                    var history = new OrderStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        AuctionOrderId = auctionOrder.Id,
                        FromStatus = SubOrderStatus.PendingPayment,
                        ToStatus = SubOrderStatus.Cancelled,
                        ActorRole = OrderActorRole.System,
                        Reason = "Auction payment deadline expired after 24 hours.",
                        AuctionCallbackStatus = "PaymentTimedOut"
                    };
                    dbContext.OrderStatusHistories.Add(history);
                    callbacks[auctionOrder.Id] = history;
                }

                await dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("[PaymentTimeoutWorker] Auto-cancelled {Count} expired AuctionOrders in batch.", expiredAuctionOrders.Count);

                if (auctionClient != null)
                {
                    foreach (var auctionOrder in expiredAuctionOrders)
                    {
                        try
                        {
                            if (await auctionClient.NotifyPaymentStatusAsync(
                                auctionOrder.AuctionId,
                                new AuctionPaymentStatusCallbackRequest(
                                    OrderId: auctionOrder.Id,
                                    WinnerId: auctionOrder.BuyerId,
                                    Status: "PaymentTimedOut",
                                    EventId: callbacks[auctionOrder.Id].Id,
                                    OrderSource: auctionOrder.OrderSource.ToWireValue(),
                                    OccurredAt: now
                                ),
                                ct))
                            {
                                callbacks[auctionOrder.Id].AuctionCallbackSentAt = DateTimeOffset.UtcNow;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[PaymentTimeoutWorker] Failed to dispatch PaymentTimedOut callback for AuctionOrder '{OrderId}'.", auctionOrder.Id);
                        }
                    }
                    await dbContext.SaveChangesAsync(ct);
                }

                // Lưu ý: Không tự gọi UserService trừ điểm uy tín Buyer tại đây.
                // Auction Service sẽ chủ động gọi API 20 (auction-violations) để tăng ViolationStreak và trừ uy tín Buyer khi nhận callback PaymentTimedOut.
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

                    var refundAmount = auctionOrder.TotalAmount ?? auctionOrder.RemainingAmount;
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
                                    EventId: Guid.NewGuid(),
                                    OrderSource: auctionOrder.OrderSource.ToString().ToUpperInvariant(),
                                    OccurredAt: now
                                ),
                                ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[PaymentTimeoutWorker] Failed to dispatch SellerExpired callback for AuctionOrder '{OrderId}'.", auctionOrder.Id);
                        }
                    }
                }

                if (userClient != null)
                {
                    foreach (var auctionOrder in expiredSellerOrders)
                    {
                        try
                        {
                            await userClient.CreatePenaltyAsync(new UserPenaltyRequest(
                                UserId: auctionOrder.SellerId,
                                TargetRole: "Seller",
                                ViolationType: "SellerResponseTimeout",
                                ReferenceType: "AuctionOrder",
                                ReferenceId: auctionOrder.OrderNo,
                                Reason: "Seller response deadline expired after 24 hours.",
                                CustomScoreDeduction: 5,
                                PolicyClauseCode: "POLICY-AUC-SELLER-TIMEOUT"
                            ), ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[PaymentTimeoutWorker] Fail-safe: Failed to apply penalty on Seller '{SellerId}' for expired AuctionOrder '{OrderNo}'.", auctionOrder.SellerId, auctionOrder.OrderNo);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PaymentTimeoutWorker] Failed to process seller-expired AuctionOrders batch.");
            }
        }

        // 4. Quét và giải phóng các voucher reservations mồ côi quá hạn 16 phút
        if (voucherLifecycleService != null)
        {
            try
            {
                await voucherLifecycleService.ReleaseExpiredReservationsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PaymentTimeoutWorker] Failed to sweep expired orphan voucher reservations.");
            }
        }

        // 5. Tự động hoàn tất các đơn con đã giao (Delivered) quá thời hạn khiếu nại (mặc định 3 ngày / 4320 phút)
        try
        {
            var autoCompleteMinutes = configuration?.GetValue<int>("OrderLifecycle:AutoCompleteDeliveredMinutes") ?? 4320;
            if (autoCompleteMinutes <= 0)
            {
                autoCompleteMinutes = 4320;
            }

            var deliveryCutoff = now.AddMinutes(-autoCompleteMinutes);

            var deliveredSubOrders = await dbContext.SubOrders
                .Include(s => s.Items)
                .Include(s => s.Order!)
                    .ThenInclude(o => o.SubOrders)
                .Where(s => s.Status == SubOrderStatus.Delivered && s.DeliveredAt.HasValue && s.DeliveredAt.Value <= deliveryCutoff)
                .OrderBy(s => s.DeliveredAt)
                .Take(50)
                .ToListAsync(ct);

            if (deliveredSubOrders.Count > 0)
            {
                _logger.LogInformation("[PaymentTimeoutWorker] Found {Count} delivered SubOrders past {Minutes}m inspection window to auto-complete.", deliveredSubOrders.Count, autoCompleteMinutes);

                foreach (var so in deliveredSubOrders)
                {
                    so.Status = SubOrderStatus.Completed;
                    so.CompletedAt = now;
                    so.UpdatedAt = now;

                    dbContext.OrderStatusHistories.Add(new OrderStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        SubOrderId = so.Id,
                        FromStatus = SubOrderStatus.Delivered,
                        ToStatus = SubOrderStatus.Completed,
                        ActorRole = OrderActorRole.System,
                        Reason = "Order automatically completed after delivery grace period.",
                        CreatedAt = now
                    });

                    // Giải ngân Escrow sang ví người bán (bỏ qua nếu COD)
                    bool autoSettled = so.PaymentMethod == PaymentMethod.Cod;
                    if ((walletClient != null || settlementService != null) && so.PaymentMethod != PaymentMethod.Cod
                        && (so.SellerReceiveAmount > 0m || (settlementService != null && so.SettlementProtocolVersion >= 2 && so.TotalAmount > 0m)))
                    {
                        try
                        {
                            var settled = settlementService != null
                                ? await settlementService.SettleSubOrderAsync(so, ct)
                                : await walletClient!.SettleSellerSubOrderAsync(
                                    so.Id,
                                    so.SellerId,
                                    so.SellerReceiveAmount,
                                    idempotencyKey: $"settle-auto-{so.Id}",
                                    cancellationToken: ct);

                            if (settled)
                            {
                                autoSettled = true;
                                _logger.LogInformation("[PaymentTimeoutWorker] Auto-completed SubOrder '{SubOrderId}' escrow settled successfully. Amount: {Amount}", so.Id, so.SellerReceiveAmount);
                            }
                            else
                            {
                                _logger.LogWarning("[PaymentTimeoutWorker] Auto-completed SubOrder '{SubOrderId}' settlement returned false.", so.Id);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[PaymentTimeoutWorker] Failed to settle seller escrow for auto-completed SubOrder '{SubOrderId}'.", so.Id);
                        }
                    }
                    so.IsEscrowSettled = autoSettled;

                    // Thưởng điểm uy tín Seller (+1)
                    if (userClient != null)
                    {
                        try
                        {
                            await userClient.AdjustReputationAsync(new ReputationAdjustRequest(
                                UserId: so.SellerId,
                                TargetRole: "Seller",
                                PartnerUserId: so.BuyerId,
                                ScoreDelta: 1,
                                Reason: "OrderCompleted",
                                ReferenceType: "Order",
                                ReferenceId: so.SubOrderNo,
                                Note: $"Auto-completion reward for sub-order {so.SubOrderNo}."
                            ), ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[PaymentTimeoutWorker] Failed to adjust reputation for Seller '{SellerId}' on auto-completed SubOrder '{SubOrderId}'.", so.SellerId, so.Id);
                        }
                    }

                    // Đồng bộ dữ liệu sang Catalog để mở quyền đánh giá sản phẩm
                    if (catalogClient != null && so.Items.Count > 0)
                    {
                        try
                        {
                            var productIds = so.Items.Select(i => i.ProductId);
                            await catalogClient.SyncPurchasesBatchAsync(so.BuyerId, so.Id, productIds, now, ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[PaymentTimeoutWorker] Failed to sync purchases to Catalog for auto-completed SubOrder '{SubOrderId}'.", so.Id);
                        }
                    }

                    // Đồng bộ trạng thái master order nếu tất cả đơn con đã Completed hoặc Cancelled
                    if (so.Order != null && so.Order.SubOrders.All(s => s.Id == so.Id || s.Status == SubOrderStatus.Completed || s.Status == SubOrderStatus.Cancelled))
                    {
                        so.Order.Status = OrderStatus.Completed;
                        so.Order.UpdatedAt = now;
                    }
                }

                await dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("[PaymentTimeoutWorker] Successfully processed auto-completion for {Count} delivered SubOrders.", deliveredSubOrders.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PaymentTimeoutWorker] Error during auto-completion sweep of delivered SubOrders.");
        }

        // 5. Quét bù đối soát hoàn tiền bị treo (Self-healing requested refunds)
        await ReconcilePendingRefundsAsync(dbContext, walletClient, ct);

        // 6. Quét bù đối soát giải ngân Escrow cho Shop (Self-healing unsettled seller escrow)
        await ReconcileUnsettledEscrowsAsync(dbContext, walletClient, ct, settlementService);
    }

    internal async Task ReconcilePendingRefundsAsync(
        ICommerceDbContext dbContext,
        IWalletClient? walletClient,
        CancellationToken ct)
    {
        if (walletClient == null) return;

        try
        {
            var pendingRefunds = await dbContext.OrderRefunds
                .Where(r => r.Status == RefundStatus.Requested)
                .OrderBy(r => r.RequestedAt)
                .Take(50)
                .ToListAsync(ct);

            if (pendingRefunds.Count == 0) return;

            _logger.LogInformation("[PaymentTimeoutWorker] Found {Count} pending requested refunds to reconcile.", pendingRefunds.Count);

            int reconciledCount = 0;
            var now = DateTimeOffset.UtcNow;

            foreach (var refund in pendingRefunds)
            {
                try
                {
                    bool succeeded = false;

                    if (refund.SubOrderId.HasValue)
                    {
                        var subOrder = await dbContext.SubOrders
                            .AsNoTracking()
                            .FirstOrDefaultAsync(s => s.Id == refund.SubOrderId.Value, ct);

                        if (subOrder != null)
                        {
                            succeeded = await walletClient.RefundSubOrderPaymentAsync(
                                subOrder.Id,
                                subOrder.BuyerId.ToString(),
                                refund.Amount,
                                refund.ReasonNote ?? "Reconciliation refund retry",
                                $"refund-retry-{refund.Id}",
                                ct);
                        }
                    }
                    else if (refund.OrderId.HasValue)
                    {
                        var order = await dbContext.Orders
                            .AsNoTracking()
                            .FirstOrDefaultAsync(o => o.Id == refund.OrderId.Value, ct);

                        if (order != null)
                        {
                            succeeded = await walletClient.RefundOrderPaymentAsync(
                                order.Id,
                                order.BuyerId.ToString(),
                                refund.Amount,
                                refund.ReasonNote ?? "Reconciliation refund retry",
                                $"refund-retry-{refund.Id}",
                                ct);
                        }
                    }

                    if (succeeded)
                    {
                        refund.Status = RefundStatus.Completed;
                        refund.CompletedAt = now;
                        reconciledCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[PaymentTimeoutWorker] Failed to reconcile refund '{RefundId}'. Will retry next cycle.", refund.Id);
                }
            }

            if (reconciledCount > 0)
            {
                await dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("[PaymentTimeoutWorker] Successfully reconciled {Count}/{Total} pending refunds.", reconciledCount, pendingRefunds.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PaymentTimeoutWorker] Error during pending refunds reconciliation sweep.");
        }
    }

    internal async Task ReconcileUnsettledEscrowsAsync(
        ICommerceDbContext dbContext,
        IWalletClient? walletClient,
        CancellationToken ct,
        ISettlementService? settlementService = null)
    {
        if (walletClient == null && settlementService == null) return;

        try
        {
            // A payout that needs a finance decision, or whose next attempt is not due, must not occupy one of the 50 slots of a scan:
            // otherwise a few stuck orders would starve every order behind them.
            var dueNow = DateTimeOffset.UtcNow;
            var unsettledSubOrders = await dbContext.SubOrders
                .Where(s => s.Status == SubOrderStatus.Completed &&
                            !s.IsEscrowSettled &&
                            s.PaymentMethod == PaymentMethod.Wallet &&
                            (s.SellerReceiveAmount > 0m || (s.SettlementProtocolVersion >= 2 && s.TotalAmount > 0m)) &&
                            !dbContext.SettlementSnapshots.Any(n => n.SubOrderId == s.Id &&
                                (n.Status == SettlementSnapshotStatus.NeedsFinance ||
                                 (n.Status == SettlementSnapshotStatus.Pending && n.NextRetryAt != null && n.NextRetryAt > dueNow))))
                .OrderBy(s => s.CompletedAt)
                .Take(50)
                .ToListAsync(ct);

            if (unsettledSubOrders.Count == 0) return;

            _logger.LogInformation("[PaymentTimeoutWorker] Found {Count} completed sub-orders with unsettled escrow to reconcile.", unsettledSubOrders.Count);

            int settledCount = 0;
            var now = DateTimeOffset.UtcNow;

            foreach (var so in unsettledSubOrders)
            {
                try
                {
                    var succeeded = settlementService != null
                        ? await settlementService.SettleSubOrderAsync(so, ct, respectBackoff: true)
                        : await walletClient!.SettleSellerSubOrderAsync(
                            so.Id,
                            so.SellerId,
                            so.SellerReceiveAmount,
                            idempotencyKey: $"settle-{so.Id}",
                            cancellationToken: ct);

                    if (succeeded)
                    {
                        so.IsEscrowSettled = true;
                        so.UpdatedAt = now;
                        settledCount++;
                    }
                    else
                    {
                        _logger.LogWarning("[PaymentTimeoutWorker] Settlement returned false for SubOrder '{SubOrderId}' during reconciliation.", so.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[PaymentTimeoutWorker] Failed to settle escrow for SubOrder '{SubOrderId}' during reconciliation. Will retry next cycle.", so.Id);
                }
            }

            if (settledCount > 0)
            {
                await dbContext.SaveChangesAsync(ct);
                _logger.LogInformation("[PaymentTimeoutWorker] Successfully reconciled {Count}/{Total} unsettled escrows.", settledCount, unsettledSubOrders.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PaymentTimeoutWorker] Error during unsettled escrow reconciliation sweep.");
        }
    }
}
