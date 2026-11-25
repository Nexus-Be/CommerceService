namespace Commerce.Application.Services.Settlement;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public sealed class SettlementService : ISettlementService
{
    public const string OrderPayout = "OrderPayout";
    public const string AuctionPayout = "AuctionPayout";

    /// <summary>After this many failed transient attempts a payout needs a finance decision instead of retrying forever.</summary>
    public const int MaxAttempts = 20;

    private readonly ICommerceDbContext _db;
    private readonly IWalletClient _wallet;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SettlementService> _logger;

    public SettlementService(ICommerceDbContext db, IWalletClient wallet, IConfiguration configuration, ILogger<SettlementService> logger)
    {
        _db = db;
        _wallet = wallet;
        _configuration = configuration;
        _logger = logger;
    }

    public int NewOrderProtocolVersion =>
        int.TryParse(_configuration["Settlement:ProtocolVersion"], out var version) ? Math.Clamp(version, 1, 2) : 1;

    /// <summary>While true the legacy (V1) payout route is still used for orders that are on V1. Turn it off at the V2 cutover.</summary>
    private bool LegacyV1Enabled =>
        !bool.TryParse(_configuration["Settlement:LegacyV1Enabled"], out var enabled) || enabled;

    // ------------------------------------------------------------------ regular orders

    public async Task<bool> SettleSubOrderAsync(SubOrder subOrder, CancellationToken cancellationToken, bool respectBackoff = false)
    {
        if (subOrder.PaymentMethod == PaymentMethod.Cod)
        {
            return true; // Cash is reconciled with the courier, never through the wallet.
        }

        if (subOrder.SettlementProtocolVersion < 2)
        {
            if (LegacyV1Enabled)
            {
                return await SettleLegacyAsync(subOrder.Id, subOrder.SellerId, subOrder.SellerReceiveAmount, cancellationToken);
            }

            // The legacy route is closed: an unpaid V1 order can only continue through V2. Wallet refuses it if V1 already paid it,
            // and refuses a discount that has no reserved subsidy; both end in the finance queue, never in a V1 payout.
            subOrder.SettlementProtocolVersion = 2;
            _logger.LogWarning("[Settlement] Sub-order '{SubOrderId}' moved from protocol V1 to V2 because the legacy payout route is closed.", subOrder.Id);
        }

        if (subOrder.SellerReceiveAmount <= 0m && subOrder.TotalAmount <= 0m)
        {
            return true;
        }

        var snapshot = await FindOrCreateSnapshotAsync(
            OrderPayout, subOrder.OrderId, subOrder.Id, subOrder.SellerId,
            sellerReceive: subOrder.SellerReceiveAmount,
            platformFee: subOrder.PlatformFeeAmount,
            shipping: subOrder.ShippingFeeAmount,
            subsidy: subOrder.SystemDiscountAmount,
            orderEscrow: subOrder.TotalAmount,
            depositEscrow: 0m,
            cancellationToken);

        var completed = await SendAsync(snapshot, respectBackoff, cancellationToken);
        if (completed && snapshot.SubsidyAmount > 0m)
        {
            subOrder.SubsidyState = SubsidyState.Used;
        }

        return completed;
    }

    // ------------------------------------------------------------------ auction orders

    public async Task<bool> SettleAuctionOrderAsync(AuctionOrder order, CancellationToken cancellationToken, bool respectBackoff = false)
    {
        if (order.SettlementProtocolVersion < 2)
        {
            if (!LegacyV1Enabled)
            {
                order.SettlementProtocolVersion = 2;
                _logger.LogWarning("[Settlement] Auction order '{OrderId}' moved from protocol V1 to V2 because the legacy payout route is closed.", order.Id);
            }
            else
            {
                return await SettleLegacyAsync(order.Id, order.SellerId, order.SellerReceiveAmount, cancellationToken);
            }
        }

        if (order.TotalAmount is null)
        {
            // Nothing was ever paid for this order: there is no escrow to pay out from.
            var unpaid = await FindOrCreateSnapshotAsync(
                AuctionPayout, order.Id, order.Id, order.SellerId, order.SellerReceiveAmount, order.PlatformFeeAmount,
                order.ShippingFeeAmount ?? 0m, 0m, 0m, order.DepositAmount, cancellationToken, forcedError: ("ORDER_NOT_PAID", "The auction order has no paid total."));
            return await SendAsync(unpaid, respectBackoff, cancellationToken);
        }

        var snapshot = await FindOrCreateSnapshotAsync(
            AuctionPayout, order.Id, order.Id, order.SellerId,
            sellerReceive: order.SellerReceiveAmount,
            platformFee: order.PlatformFeeAmount,
            shipping: order.ShippingFeeAmount ?? 0m,
            subsidy: 0m,
            orderEscrow: order.TotalAmount.Value,
            depositEscrow: order.DepositAmount,
            cancellationToken);

        return await SendAsync(snapshot, respectBackoff, cancellationToken);
    }

    // ------------------------------------------------------------------ subsidy

    public async Task HoldSubsidyAsync(Guid orderId, Guid subOrderId, decimal amount, CancellationToken cancellationToken)
    {
        var outcome = await _wallet.HoldPlatformSubsidyAsync(orderId, subOrderId, amount, cancellationToken);
        if (outcome.Success)
        {
            return;
        }

        if (outcome.IsPermanentFailure)
        {
            // Out of budget, over a cap, or no approved budget: the voucher cannot be honoured. The buyer must see the new total.
            throw new CommerceDomainException(
                "VOUCHER_BUDGET_EXHAUSTED",
                "The platform voucher can no longer be funded. Please request a new quote to confirm the new total.",
                409);
        }

        throw new CommerceDomainException(
            "SUBSIDY_UNAVAILABLE",
            "The platform voucher budget could not be checked right now. Please try again.",
            503);
    }

    public async Task<bool> ReleaseSubsidyAsync(Guid subOrderId, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await _wallet.ReleasePlatformSubsidyAsync(subOrderId, reason, cancellationToken);
            return outcome.Success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[Settlement] Releasing the subsidy of sub-order '{SubOrderId}' failed; it will be retried.", subOrderId);
            return false;
        }
    }

    // ------------------------------------------------------------------ worker

    public async Task<int> RunPendingWorkAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var completed = 0;

        // Only payouts that are due: a payout that failed permanently, or is waiting for its next attempt, does not take a slot.
        var due = await _db.SettlementSnapshots
            .Where(item => item.Status == SettlementSnapshotStatus.Pending && (item.NextRetryAt == null || item.NextRetryAt <= now))
            .OrderBy(item => item.NextRetryAt ?? item.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var snapshot in due)
        {
            try
            {
                if (await SendAsync(snapshot, respectBackoff: true, cancellationToken))
                {
                    await FinishSourceOrderAsync(snapshot, cancellationToken);
                    completed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One broken payout must not stop the others.
                _logger.LogError(ex, "[Settlement] Retry of payout '{BusinessType}' '{SubOrderId}' failed unexpectedly.", snapshot.BusinessType, snapshot.SubOrderId);
            }
        }

        await ReleaseCancelledSubsidiesAsync(batchSize, cancellationToken);
        try
        {
            await ReleaseOrphanSubsidiesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A housekeeping check must never turn a pass that paid sellers into a failed pass.
            _logger.LogError(ex, "[Settlement] The check for orphan subsidy holds failed; it will run again on the next pass.");
        }

        return completed;
    }

    private async Task FinishSourceOrderAsync(SettlementSnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.BusinessType == OrderPayout)
        {
            var sub = await _db.SubOrders.FirstOrDefaultAsync(item => item.Id == snapshot.SubOrderId, ct);
            if (sub is not null)
            {
                sub.IsEscrowSettled = true;
                if (snapshot.SubsidyAmount > 0m) sub.SubsidyState = SubsidyState.Used;
                sub.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        else
        {
            var auction = await _db.AuctionOrders.FirstOrDefaultAsync(item => item.Id == snapshot.SubOrderId, ct);
            if (auction is not null && auction.PayoutCompletedAt is null)
            {
                // Only after Wallet confirmed the payout.
                auction.PayoutCompletedAt = DateTimeOffset.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task ReleaseCancelledSubsidiesAsync(int batchSize, CancellationToken ct)
    {
        var cancelled = await _db.SubOrders
            .Where(item => item.SubsidyState == SubsidyState.Held && (item.Status == SubOrderStatus.Cancelled || item.Status == SubOrderStatus.Refunded) && !item.IsEscrowSettled)
            .OrderBy(item => item.UpdatedAt)
            .Take(batchSize)
            .ToListAsync(ct);
        foreach (var sub in cancelled)
        {
            if (await ReleaseSubsidyAsync(sub.Id, $"Sub-order {sub.Status} before payout", ct))
            {
                sub.SubsidyState = SubsidyState.Released;
                sub.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        if (cancelled.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>How long a subsidy hold must have been held before it can be an orphan. The order is saved seconds after the hold, so this is generous.</summary>
    public static readonly TimeSpan OrphanSubsidyAge = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A hold whose sub-order was never saved (the process stopped after Wallet reserved the subsidy and before the order was stored) would
    /// keep budget reserved forever. Wallet lists the old holds; only a hold whose sub-order does not exist here is released. A hold that
    /// belongs to a saved sub-order is never touched by this method: the cancelled-order path and the payout own it.
    /// </summary>
    public async Task<int> ReleaseOrphanSubsidiesAsync(CancellationToken ct)
    {
        var stale = await _wallet.ListStaleSubsidyHoldsAsync((int)OrphanSubsidyAge.TotalMinutes, 50, ct);
        if (stale is null || stale.Count == 0)
        {
            return 0;
        }

        var released = 0;
        foreach (var hold in stale)
        {
            if (await _db.SubOrders.AnyAsync(item => item.Id == hold.SubOrderId, ct))
            {
                continue;
            }

            _logger.LogWarning("[Settlement] Subsidy hold of sub-order '{SubOrderId}' ({Amount}) has no sub-order after {Minutes} minutes; releasing it.",
                hold.SubOrderId, hold.Amount, (int)OrphanSubsidyAge.TotalMinutes);
            if (await ReleaseSubsidyAsync(hold.SubOrderId, "Orphan hold: no sub-order was saved for it", ct))
            {
                released++;
            }
        }

        return released;
    }

    // ------------------------------------------------------------------ internals

    private async Task<bool> SettleLegacyAsync(Guid subOrderId, Guid sellerId, decimal amount, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return true;
        }

        try
        {
            return await _wallet.SettleSellerSubOrderAsync(subOrderId, sellerId, amount, $"settle-{subOrderId}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[Settlement] Legacy payout of '{SubOrderId}' failed.", subOrderId);
            return false;
        }
    }

    /// <summary>
    /// Captures the payload once. The snapshot is saved (committed) before Wallet is called, so a crash between the two steps still
    /// resends the same payload. Two processes capturing the same sub-order race on the unique index and share the winner.
    /// </summary>
    private async Task<SettlementSnapshot> FindOrCreateSnapshotAsync(
        string businessType, Guid orderId, Guid subOrderId, Guid sellerId,
        decimal sellerReceive, decimal platformFee, decimal shipping, decimal subsidy, decimal orderEscrow, decimal depositEscrow,
        CancellationToken ct, (string Code, string Message)? forcedError = null)
    {
        var existing = await _db.SettlementSnapshots.FirstOrDefaultAsync(item => item.BusinessType == businessType && item.SubOrderId == subOrderId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var snapshot = new SettlementSnapshot
        {
            Id = Guid.NewGuid(),
            BusinessType = businessType,
            OrderId = orderId,
            SubOrderId = subOrderId,
            SellerId = sellerId,
            SellerReceiveAmount = sellerReceive,
            PlatformFeeAmount = platformFee,
            ShippingAmount = shipping,
            SubsidyAmount = subsidy,
            OrderEscrowAmount = orderEscrow,
            DepositEscrowAmount = depositEscrow,
            PayloadVersion = 1
        };

        // What leaves the escrows and the subsidy budget must equal what is paid out or allocated. A payload that does not balance
        // is never sent: it goes to finance with the reason.
        var sources = orderEscrow + depositEscrow + subsidy;
        var uses = sellerReceive + platformFee + shipping;
        if (forcedError is { } forced)
        {
            snapshot.Status = SettlementSnapshotStatus.NeedsFinance;
            snapshot.LastErrorCode = forced.Code;
            snapshot.LastError = forced.Message;
        }
        else if (sources != uses)
        {
            snapshot.Status = SettlementSnapshotStatus.NeedsFinance;
            snapshot.LastErrorCode = "SETTLEMENT_IDENTITY_INVALID";
            snapshot.LastError = $"Escrow and subsidy ({sources}) do not equal seller, fee and shipping ({uses}).";
        }

        _db.SettlementSnapshots.Add(snapshot);
        try
        {
            await _db.SaveChangesAsync(ct);
            return snapshot;
        }
        catch (DbUpdateException)
        {
            // Another process captured the same sub-order first: use its snapshot, never a second one.
            _db.SettlementSnapshots.Remove(snapshot); // detaches the unsaved entity
            return await _db.SettlementSnapshots.FirstAsync(item => item.BusinessType == businessType && item.SubOrderId == subOrderId, ct);
        }
    }

    /// <summary>Sends the stored payload to Wallet and records the result on the snapshot. Never throws for a payout problem.</summary>
    private async Task<bool> SendAsync(SettlementSnapshot snapshot, bool respectBackoff, CancellationToken ct)
    {
        if (snapshot.Status == SettlementSnapshotStatus.Completed)
        {
            return true;
        }

        if (snapshot.Status == SettlementSnapshotStatus.NeedsFinance)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (respectBackoff && snapshot.NextRetryAt is { } next && next > now)
        {
            return false;
        }

        var outcome = await _wallet.SettleOrderPayoutAsync(new WalletSettlementRequest(
            snapshot.BusinessType, snapshot.OrderId, snapshot.SubOrderId, snapshot.SellerId,
            snapshot.SellerReceiveAmount, snapshot.PlatformFeeAmount, snapshot.ShippingAmount, snapshot.SubsidyAmount,
            snapshot.OrderEscrowAmount, snapshot.DepositEscrowAmount, snapshot.PayloadVersion), ct);

        snapshot.UpdatedAt = DateTimeOffset.UtcNow;
        snapshot.Attempts++;
        if (outcome.Success)
        {
            snapshot.Status = SettlementSnapshotStatus.Completed;
            snapshot.CompletedAt = DateTimeOffset.UtcNow;
            snapshot.NextRetryAt = null;
            snapshot.LastErrorCode = null;
            snapshot.LastError = null;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        snapshot.LastErrorCode = Truncate(outcome.ErrorCode, 100);
        snapshot.LastError = Truncate(outcome.ErrorMessage, 500);
        if (outcome.IsPermanentFailure || snapshot.Attempts >= MaxAttempts)
        {
            snapshot.Status = SettlementSnapshotStatus.NeedsFinance;
            snapshot.NextRetryAt = null;
            _logger.LogError("[Settlement] Payout '{BusinessType}' '{SubOrderId}' needs a finance decision: {Code}", snapshot.BusinessType, snapshot.SubOrderId, snapshot.LastErrorCode);
        }
        else
        {
            // 1, 2, 4 ... minutes, at most one hour.
            var minutes = Math.Min(60, Math.Pow(2, Math.Min(snapshot.Attempts - 1, 6)));
            snapshot.NextRetryAt = DateTimeOffset.UtcNow.AddMinutes(minutes);
        }

        await _db.SaveChangesAsync(ct);
        return false;
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
