namespace Commerce.Api.Workers;

using Commerce.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class AuctionCallbackRetryWorker(
    IServiceScopeFactory scopes, ILogger<AuctionCallbackRetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ICommerceDbContext>();
                var auction = scope.ServiceProvider.GetRequiredService<IAuctionClient>();
                var pending = await db.OrderStatusHistories
                    .Include(h => h.AuctionOrder)
                    .Where(h => h.AuctionOrderId != null && h.AuctionCallbackStatus != null &&
                                h.AuctionCallbackSentAt == null && !h.IsDeleted)
                    .OrderBy(h => h.CreatedAt).ThenBy(h => h.Id)
                    .Take(50).ToListAsync(stoppingToken);
                foreach (var history in pending)
                {
                    var order = history.AuctionOrder;
                    if (order is null) continue;
                    // Same builder as the first send: a retry never carries less than the original.
                    var callback = Commerce.Application.Services.AuctionOrder.AuctionPaymentCallbackFactory.Build(order, history);
                    if (!await auction.NotifyPaymentStatusAsync(order.AuctionId, callback, stoppingToken))
                        break;
                    history.AuctionCallbackSentAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auction callback retry scan failed; it will run again.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
