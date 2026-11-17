namespace Commerce.Application.Interfaces;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface IUserClient
{
    Task<UserPermissionCheckResult> CheckBuyerPermissionAsync(
        Guid buyerId,
        string action = "buy",
        CancellationToken ct = default);

    /// <summary>
    /// The shop of a seller: ShopId is the seller profile id, which is not the seller's user id. Found is false when the seller has no profile;
    /// throws when the User service cannot be reached (a temporary failure the caller retries).
    /// </summary>
    Task<SellerShopResult> GetSellerShopAsync(Guid sellerUserId, CancellationToken ct = default);

    Task<ReputationAdjustResponse?> AdjustReputationAsync(
        ReputationAdjustRequest request,
        CancellationToken ct = default);

    Task<UserPenaltyResponse?> CreatePenaltyAsync(
        UserPenaltyRequest request,
        CancellationToken ct = default);
}
