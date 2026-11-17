namespace Commerce.Api.Workers;

using Commerce.Application.Interfaces;
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
                var refunds = await db.OrderRefunds.Include(r => r.AuctionOrder)
                    .Where(r => r.AuctionOrderId != null && r.Status == RefundStatus.Requested && !r.IsDeleted)
                    .OrderBy(r => r.RequestedAt).Take(50).ToListAsync(stoppingToken);
                foreach (var refund in refunds)
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

                var payouts = await db.AuctionOrders
                    .Where(o => o.Status == SubOrderStatus.Completed && o.PayoutCompletedAt == null && !o.IsDeleted)
                    .OrderBy(o => o.CompletedAt).Take(50).ToListAsync(stoppingToken);
                foreach (var order in payouts)
                {
                    if (!await wallet.SettleSellerSubOrderAsync(order.Id, order.SellerId,
                        order.SellerReceiveAmount, order.Id.ToString(), stoppingToken)) continue;
                    order.PayoutCompletedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(stoppingToken);
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
