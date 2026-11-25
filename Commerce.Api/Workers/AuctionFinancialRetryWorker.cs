namespace Commerce.Api.Workers;

using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Microsoft.EntityFrameworkCore;

public sealed class AuctionFinancialRetryWorker(
    IServiceScopeFactory scopes, ILogger<AuctionFinancialRetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ICommerceDbContext>();
                var wallet = scope.ServiceProvider.GetRequiredService<IWalletClient>();
                var settlement = scope.ServiceProvider.GetService<ISettlementService>();
                var refunds = await db.OrderRefunds.Include(r => r.AuctionOrder)
                    .Where(r => r.AuctionOrderId != null && r.Status == RefundStatus.WalletPending && !r.IsDeleted)
                    .OrderBy(r => r.RequestedAt).Take(50).ToListAsync(stoppingToken);
                foreach (var refund in refunds)
                {
                    try
                    {
                        var order = refund.AuctionOrder;
                        if (order is null) continue;
                        var success = refund.Amount == 0m || await wallet.RefundOrderPaymentAsync(
                            order.Id, order.BuyerId.ToString(), refund.Amount,
                            refund.ReasonNote ?? refund.ReasonCode, refund.Id.ToString(), stoppingToken);
                        if (!success) continue;
                        refund.Status = RefundStatus.Completed;
                        refund.CompletedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Auction refund retry failed for refund '{RefundId}'; the next scan will retry.", refund.Id);
                    }
                }

                // One failing payout must not stop the others, and a payout that needs a finance decision (or is waiting for its next
                // attempt) must not occupy one of the 50 slots of a scan.
                var dueNow = DateTimeOffset.UtcNow;
                var payouts = await db.AuctionOrders
                    .Where(o => o.Status == SubOrderStatus.Completed && o.PayoutCompletedAt == null && !o.IsDeleted &&
                                !db.SettlementSnapshots.Any(n => n.SubOrderId == o.Id &&
                                    (n.Status == SettlementSnapshotStatus.NeedsFinance ||
                                     (n.Status == SettlementSnapshotStatus.Pending && n.NextRetryAt != null && n.NextRetryAt > dueNow))))
                    .OrderBy(o => o.CompletedAt).Take(50).ToListAsync(stoppingToken);
                foreach (var order in payouts)
                {
                    try
                    {
                        var paid = settlement != null
                            ? await settlement.SettleAuctionOrderAsync(order, stoppingToken, respectBackoff: true)
                            : await wallet.SettleSellerSubOrderAsync(order.Id, order.SellerId,
                                order.SellerReceiveAmount, order.Id.ToString(), stoppingToken);
                        if (!paid) continue;
                        // Only after Wallet confirmed the payout.
                        order.PayoutCompletedAt = DateTimeOffset.UtcNow;
                        await db.SaveChangesAsync(stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Auction payout retry failed for order '{OrderId}'; the next scan will retry.", order.Id);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auction refund or payout retry failed; the next scan will retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
