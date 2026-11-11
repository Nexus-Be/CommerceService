namespace Commerce.Api.Workers;

using System;
using System.Collections.Generic;
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

public sealed class VnPayPaymentPollingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VnPayPaymentPollingWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    public VnPayPaymentPollingWorker(
        IServiceProvider serviceProvider,
        ILogger<VnPayPaymentPollingWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[VnPayPaymentPollingWorker] Background service started. Interval: {Interval}", _checkInterval);

        using var timer = new PeriodicTimer(_checkInterval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessPendingVnPayOrdersAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "[VnPayPaymentPollingWorker] Unhandled error during VNPay payment polling cycle.");
            }
        }

        _logger.LogInformation("[VnPayPaymentPollingWorker] Background service is stopping.");
    }

    internal async Task ProcessPendingVnPayOrdersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ICommerceDbContext>();
        var walletClient = scope.ServiceProvider.GetRequiredService<IWalletClient>();
        var fulfillmentClient = scope.ServiceProvider.GetRequiredService<IFulfillmentClient>();

        var now = DateTimeOffset.UtcNow;

        var pendingOrders = await dbContext.Orders
            .Include(o => o.SubOrders)
            .Where(o => o.Status == OrderStatus.PendingPayment &&
                        o.SubOrders.Any(so => so.PaymentMethod == PaymentMethod.VnPay) &&
                        o.PaymentDeadlineAt > now)
            .OrderBy(o => o.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        if (pendingOrders.Count == 0)
        {
            return;
        }

        _logger.LogDebug("[VnPayPaymentPollingWorker] Polling status for {Count} pending VNPay orders.", pendingOrders.Count);

        foreach (var order in pendingOrders)
        {
            try
            {
                var statusResult = await walletClient.GetDirectCheckoutStatusAsync(order.Id, order.BuyerId, ct);
                if (statusResult == null)
                {
                    continue;
                }

                if (string.Equals(statusResult.Status, "Success", StringComparison.OrdinalIgnoreCase))
                {
                    order.Status = OrderStatus.Paid;
                    if (statusResult.TransactionId.HasValue)
                    {
                        order.WalletTransactionId = statusResult.TransactionId;
                    }

                    var paidAt = statusResult.PaidAt ?? DateTimeOffset.UtcNow;
                    foreach (var so in order.SubOrders)
                    {
                        so.Status = SubOrderStatus.Paid;
                        so.PaidAt = paidAt;
                        if (order.WalletTransactionId.HasValue)
                        {
                            so.WalletTransactionId = order.WalletTransactionId;
                        }
                    }

                    await dbContext.SaveChangesAsync(ct);

                    bool committed = await fulfillmentClient.CommitStockAsync(order.Id, ct);
                    if (!committed)
                    {
                        _logger.LogError("[VnPayPaymentPollingWorker] Stock commit failed at Fulfillment for paid Order '{OrderId}'.", order.Id);
                    }

                    _logger.LogInformation("[VnPayPaymentPollingWorker] Order '{OrderId}' settled successfully via VNPay.", order.Id);
                }
                else if (string.Equals(statusResult.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("[VnPayPaymentPollingWorker] Order '{OrderId}' marked as failed by VNPay gateway. Cancelling order.", order.Id);

                    order.Status = OrderStatus.Cancelled;
                    var cancelledAt = DateTimeOffset.UtcNow;

                    foreach (var so in order.SubOrders)
                    {
                        so.Status = SubOrderStatus.Cancelled;
                        so.CancelledAt = cancelledAt;

                        dbContext.OrderCancellations.Add(new OrderCancellation
                        {
                            Id = Guid.NewGuid(),
                            SubOrderId = so.Id,
                            CancelledBy = null,
                            ActorRole = OrderActorRole.System,
                            ReasonCode = CancellationReasonCode.Other,
                            ReasonNote = "Payment failed on VNPay gateway.",
                            CreatedAt = cancelledAt
                        });
                    }

                    await dbContext.SaveChangesAsync(ct);

                    try
                    {
                        await fulfillmentClient.BatchReleaseStockAsync(new List<Guid> { order.Id }, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[VnPayPaymentPollingWorker] Stock release failed for cancelled Order '{OrderId}'.", order.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[VnPayPaymentPollingWorker] Error checking payment status for Order '{OrderId}'.", order.Id);
            }
        }
    }
}
