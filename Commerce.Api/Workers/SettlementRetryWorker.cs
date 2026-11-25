namespace Commerce.Api.Workers;

using Commerce.Application.Interfaces;

/// <summary>
/// Retries seller payouts (protocol V2) whose next attempt is due and gives back the platform voucher subsidy of sub-orders that were
/// cancelled or refunded before payout. Each payout is isolated: one failing order never blocks the others.
/// </summary>
public sealed class SettlementRetryWorker(
    IServiceScopeFactory scopes, ILogger<SettlementRetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var settlement = scope.ServiceProvider.GetService<ISettlementService>();
                if (settlement is null) continue;
                var completed = await settlement.RunPendingWorkAsync(50, stoppingToken);
                if (completed > 0)
                {
                    logger.LogInformation("[SettlementRetryWorker] Completed {Count} pending payouts.", completed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "[SettlementRetryWorker] Settlement retry pass failed; the next pass will retry.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
