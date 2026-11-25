namespace Commerce.Application.Services.Delivery;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>The state of a withdrawal after a request: Confirmed is true once User has answered; RestoredPoints is what it gave back.</summary>
public sealed record PenaltyRevocationResult(Guid SubOrderId, BuyerRefusalPenaltyStatus Status, bool Confirmed, int? RestoredPoints, bool AlreadyRequested);

/// <summary>
/// Applies the reputation penalty owed by a buyer who refused a delivery, and withdraws it when an admin decides so. The decision is stored on
/// the sub-order first (Pending), so a stop of this service or of User loses nothing: each pass tries again. User keeps one penalty per
/// sub-order (unique index), so repeating is safe.
///
/// Every change of the penalty status is a conditional update that names the status it expects, so the worker that is sending a penalty and an
/// admin who withdraws it cannot overwrite each other. A withdrawal always goes to User, even for a penalty that is still Pending: User
/// records it for the sub-order, and a penalty that arrives later is then not applied. Nothing can be left taken without a way back.
/// </summary>
public sealed class BuyerRefusalPenaltyService
{
    public const int MaxAttemptsBeforeWarning = 10;
    public const int MinReasonLength = 10;

    private readonly ICommerceDbContext _db;
    private readonly IUserClient _users;
    private readonly IDeliveryFailurePolicy _policy;
    private readonly ILogger<BuyerRefusalPenaltyService> _logger;

    public BuyerRefusalPenaltyService(ICommerceDbContext db, IUserClient users, IDeliveryFailurePolicy policy, ILogger<BuyerRefusalPenaltyService> logger)
    {
        _db = db;
        _users = users;
        _policy = policy;
        _logger = logger;
    }

    /// <summary>Tries every sub-order whose penalty is Pending. Returns how many User confirmed.</summary>
    public async Task<int> RetryPendingAsync(int batchSize, CancellationToken ct)
    {
        var pending = await _db.SubOrders
            .Where(item => item.BuyerRefusalPenaltyStatus == BuyerRefusalPenaltyStatus.Pending)
            .OrderBy(item => item.UpdatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        var confirmed = 0;
        foreach (var sub in pending)
        {
            if (!_policy.Enabled)
            {
                // The rules were switched off after the decision. Keep it Pending (visible) rather than silently dropping or applying it.
                continue;
            }

            sub.BuyerRefusalPenaltyAttempts++;
            try
            {
                var response = await _users.AdjustReputationAsync(new ReputationAdjustRequest(
                    UserId: sub.BuyerId,
                    TargetRole: "Buyer",
                    PartnerUserId: sub.SellerId,
                    ScoreDelta: _policy.BuyerRefusalScoreDelta,
                    Reason: "BuyerRefusedDelivery",
                    ReferenceType: "Order",
                    ReferenceId: sub.Id.ToString(),
                    Note: $"The buyer refused delivery of sub-order {sub.SubOrderNo} (carrier reason codes: {sub.DeliveryFailureCodes ?? "none"}; table {_policy.TableVersion})."), ct);

                if (response is not null)
                {
                    // Only a penalty that is still Pending becomes Applied. If an admin withdrew it while the call was on its way, the withdrawal
                    // stands and its own pass tells User, so the points that may just have been taken are given back.
                    if (await MarkAppliedIfPendingAsync(sub, ct))
                    {
                        confirmed++;
                    }
                    else
                    {
                        _logger.LogInformation("[BuyerRefusal] The penalty of sub-order '{SubOrderId}' was withdrawn while it was being sent; the withdrawal takes over.", sub.Id);
                    }
                }
                else if (sub.BuyerRefusalPenaltyAttempts >= MaxAttemptsBeforeWarning)
                {
                    _logger.LogWarning("[BuyerRefusal] The penalty of sub-order '{SubOrderId}' is still not confirmed after {Attempts} attempts.", sub.Id, sub.BuyerRefusalPenaltyAttempts);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[BuyerRefusal] Applying the penalty of sub-order '{SubOrderId}' failed; it stays Pending.", sub.Id);
            }
        }

        if (pending.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return confirmed;
    }

    /// <summary>Tries every withdrawal that User has not confirmed yet. Returns how many it confirmed. Does not depend on the penalty rules being enabled.</summary>
    public async Task<int> RetryRevocationsAsync(int batchSize, CancellationToken ct)
    {
        var ids = await _db.SubOrders.AsNoTracking()
            .Where(item => item.BuyerRefusalPenaltyStatus == BuyerRefusalPenaltyStatus.RevokePending)
            .OrderBy(item => item.BuyerRefusalPenaltyRevokeRequestedAt)
            .Select(item => item.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        var confirmed = 0;
        foreach (var id in ids)
        {
            try
            {
                if ((await FinishRevocationAsync(id, ct)).Confirmed)
                {
                    confirmed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[BuyerRefusal] Withdrawing the penalty of sub-order '{SubOrderId}' failed; it stays RevokePending.", id);
            }
        }

        return confirmed;
    }

    /// <summary>
    /// An admin withdraws the penalty of a sub-order. The first request stores who, why and when and moves the sub-order to RevokePending in one
    /// conditional update; every later request (a second approval, a double click) returns the state that is already there.
    /// </summary>
    public async Task<PenaltyRevocationResult> RequestRevocationAsync(Guid subOrderId, Guid adminId, string? reason, CancellationToken ct)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (adminId == Guid.Empty)
        {
            throw new CommerceDomainException("FORBIDDEN", "The approving admin could not be identified.", 403);
        }

        if (trimmed.Length < MinReasonLength || trimmed.Length > 500)
        {
            throw new CommerceDomainException("VAL_INVALID_INPUT", $"The reason must be between {MinReasonLength} and 500 characters long.");
        }

        var sub = await _db.SubOrders.AsNoTracking().FirstOrDefaultAsync(item => item.Id == subOrderId, ct)
            ?? throw new CommerceDomainException("SUB_ORDER_NOT_FOUND", "Sub-order not found.", 404);

        if (sub.BuyerRefusalPenaltyStatus is BuyerRefusalPenaltyStatus.Pending or BuyerRefusalPenaltyStatus.Applied)
        {
            var claimed = await TryClaimRevocationAsync(subOrderId, adminId, trimmed, ct);
            if (!claimed)
            {
                // Somebody else changed it between the read and the update (a second admin, or the worker). Look again.
                sub = await _db.SubOrders.AsNoTracking().FirstAsync(item => item.Id == subOrderId, ct);
            }
            else
            {
                var finished = await FinishRevocationAsync(subOrderId, ct);
                return finished with { AlreadyRequested = false };
            }
        }

        switch (sub.BuyerRefusalPenaltyStatus)
        {
            case BuyerRefusalPenaltyStatus.None:
                throw new CommerceDomainException("BUYER_REFUSAL_PENALTY_NOT_OWED", "This sub-order has no refusal penalty to withdraw.", 409);
            case BuyerRefusalPenaltyStatus.RevokePending:
                var again = await FinishRevocationAsync(subOrderId, ct);
                return again with { AlreadyRequested = true };
            case BuyerRefusalPenaltyStatus.Revoked:
            case BuyerRefusalPenaltyStatus.Cancelled:
                return new PenaltyRevocationResult(subOrderId, sub.BuyerRefusalPenaltyStatus, true, sub.BuyerRefusalPenaltyRestoredPoints, true);
            default:
                // Pending or Applied again: it came back to a state that can be claimed only if a claim just failed twice; report it plainly.
                throw new CommerceDomainException("BUYER_REFUSAL_PENALTY_BUSY", "The penalty is being processed. Try again in a moment.", 409);
        }
    }

    /// <summary>Calls User for a sub-order in RevokePending and closes the withdrawal when User answers.</summary>
    private async Task<PenaltyRevocationResult> FinishRevocationAsync(Guid subOrderId, CancellationToken ct)
    {
        var sub = await _db.SubOrders.AsNoTracking().FirstAsync(item => item.Id == subOrderId, ct);
        if (sub.BuyerRefusalPenaltyStatus != BuyerRefusalPenaltyStatus.RevokePending)
        {
            return new PenaltyRevocationResult(subOrderId, sub.BuyerRefusalPenaltyStatus, true, sub.BuyerRefusalPenaltyRestoredPoints, true);
        }

        var response = await _users.RevokeBuyerRefusalPenaltyAsync(new PenaltyRevokeRequest(
            UserId: sub.BuyerId,
            TargetRole: "Buyer",
            ReferenceId: sub.Id.ToString(),
            ApprovedBy: sub.BuyerRefusalPenaltyRevokedBy ?? Guid.Empty,
            Note: sub.BuyerRefusalPenaltyRevokeReason ?? string.Empty), ct);

        if (response is null)
        {
            return new PenaltyRevocationResult(subOrderId, BuyerRefusalPenaltyStatus.RevokePending, false, null, true);
        }

        var finalStatus = response.RestoredPoints > 0 ? BuyerRefusalPenaltyStatus.Revoked : BuyerRefusalPenaltyStatus.Cancelled;
        await CloseRevocationAsync(subOrderId, finalStatus, response.RestoredPoints, ct);
        return new PenaltyRevocationResult(subOrderId, finalStatus, true, response.RestoredPoints, true);
    }

    // ------------------------------------------------------------------ conditional status changes

    private async Task<bool> MarkAppliedIfPendingAsync(Domain.Entities.SubOrder sub, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (_db.Database.IsRelational())
        {
            var rows = await _db.SubOrders
                .Where(item => item.Id == sub.Id && item.BuyerRefusalPenaltyStatus == BuyerRefusalPenaltyStatus.Pending)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(item => item.BuyerRefusalPenaltyStatus, BuyerRefusalPenaltyStatus.Applied)
                    .SetProperty(item => item.BuyerRefusalPenaltyAppliedAt, now)
                    .SetProperty(item => item.UpdatedAt, now), ct);
            return rows == 1;
        }

        var current = await _db.SubOrders.AsNoTracking().Where(item => item.Id == sub.Id).Select(item => item.BuyerRefusalPenaltyStatus).FirstAsync(ct);
        if (current != BuyerRefusalPenaltyStatus.Pending)
        {
            return false;
        }

        sub.BuyerRefusalPenaltyStatus = BuyerRefusalPenaltyStatus.Applied;
        sub.BuyerRefusalPenaltyAppliedAt = now;
        return true;
    }

    private async Task<bool> TryClaimRevocationAsync(Guid subOrderId, Guid adminId, string reason, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (_db.Database.IsRelational())
        {
            var rows = await _db.SubOrders
                .Where(item => item.Id == subOrderId
                    && (item.BuyerRefusalPenaltyStatus == BuyerRefusalPenaltyStatus.Pending || item.BuyerRefusalPenaltyStatus == BuyerRefusalPenaltyStatus.Applied))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(item => item.BuyerRefusalPenaltyStatus, BuyerRefusalPenaltyStatus.RevokePending)
                    .SetProperty(item => item.BuyerRefusalPenaltyRevokedBy, adminId)
                    .SetProperty(item => item.BuyerRefusalPenaltyRevokeReason, reason)
                    .SetProperty(item => item.BuyerRefusalPenaltyRevokeRequestedAt, now)
                    .SetProperty(item => item.UpdatedAt, now), ct);
            return rows == 1;
        }

        var tracked = await _db.SubOrders.FirstAsync(item => item.Id == subOrderId, ct);
        if (tracked.BuyerRefusalPenaltyStatus is not (BuyerRefusalPenaltyStatus.Pending or BuyerRefusalPenaltyStatus.Applied))
        {
            return false;
        }

        tracked.BuyerRefusalPenaltyStatus = BuyerRefusalPenaltyStatus.RevokePending;
        tracked.BuyerRefusalPenaltyRevokedBy = adminId;
        tracked.BuyerRefusalPenaltyRevokeReason = reason;
        tracked.BuyerRefusalPenaltyRevokeRequestedAt = now;
        tracked.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task CloseRevocationAsync(Guid subOrderId, BuyerRefusalPenaltyStatus finalStatus, int restoredPoints, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (_db.Database.IsRelational())
        {
            await _db.SubOrders
                .Where(item => item.Id == subOrderId && item.BuyerRefusalPenaltyStatus == BuyerRefusalPenaltyStatus.RevokePending)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(item => item.BuyerRefusalPenaltyStatus, finalStatus)
                    .SetProperty(item => item.BuyerRefusalPenaltyRevokedAt, now)
                    .SetProperty(item => item.BuyerRefusalPenaltyRestoredPoints, restoredPoints)
                    .SetProperty(item => item.UpdatedAt, now), ct);
            return;
        }

        var tracked = await _db.SubOrders.FirstAsync(item => item.Id == subOrderId, ct);
        if (tracked.BuyerRefusalPenaltyStatus != BuyerRefusalPenaltyStatus.RevokePending)
        {
            return;
        }

        tracked.BuyerRefusalPenaltyStatus = finalStatus;
        tracked.BuyerRefusalPenaltyRevokedAt = now;
        tracked.BuyerRefusalPenaltyRestoredPoints = restoredPoints;
        tracked.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }
}
