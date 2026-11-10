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

    Task<ReputationAdjustResponse?> AdjustReputationAsync(
        ReputationAdjustRequest request,
        CancellationToken ct = default);

    Task<UserPenaltyResponse?> CreatePenaltyAsync(
        UserPenaltyRequest request,
        CancellationToken ct = default);
}
