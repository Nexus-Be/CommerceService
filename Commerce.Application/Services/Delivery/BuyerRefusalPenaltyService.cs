namespace Commerce.Application.Services.Delivery;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;
using Commerce.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Applies the reputation penalty owed by a buyer who refused a delivery. The decision is stored on the sub-order first (Pending), so a stop
/// of this service or of User loses nothing: each pass tries again. User keeps one penalty per sub-order (unique index), so repeating is safe.
/// </summary>
public sealed class BuyerRefusalPenaltyService
{
    public const int MaxAttemptsBeforeWarning = 10;

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
                    sub.BuyerRefusalPenaltyStatus = BuyerRefusalPenaltyStatus.Applied;
                    sub.BuyerRefusalPenaltyAppliedAt = DateTimeOffset.UtcNow;
                    confirmed++;
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
}
