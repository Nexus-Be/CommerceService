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

    /// <summary>Asks User to withdraw the refusal penalty of a sub-order. Null when User could not be reached or refused the call.</summary>
    Task<PenaltyRevokeResponse?> RevokeBuyerRefusalPenaltyAsync(
        PenaltyRevokeRequest request,
        CancellationToken ct = default);

    Task<UserPenaltyResponse?> CreatePenaltyAsync(
        UserPenaltyRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific buyer address from UserService by addressId. Returns null if 404.
    /// </summary>
    Task<BuyerAddressDto?> GetBuyerAddressAsync(Guid buyerId, Guid addressId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the default shipping address for a buyer from UserService. Returns null if none found or 404.
    /// </summary>
    Task<BuyerAddressDto?> GetBuyerDefaultAddressAsync(Guid buyerId, CancellationToken ct = default);
}
