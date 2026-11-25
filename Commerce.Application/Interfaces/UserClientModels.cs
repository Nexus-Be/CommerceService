namespace Commerce.Application.Interfaces;

using System;

public sealed record UserPermissionCheckResult(
    bool IsAllowed,
    string? ErrorCode,
    string? Message);

public sealed record ReputationAdjustRequest(
    Guid UserId,
    string TargetRole,
    Guid PartnerUserId,
    int ScoreDelta,
    string Reason,
    string ReferenceType,
    string ReferenceId,
    string Note);

public sealed record ReputationAdjustResponse(
    Guid UserId,
    string TargetRole,
    bool IsDuplicate,
    bool CooldownSuppressed,
    int PreviousScore,
    int NewScore,
    int AppliedDelta,
    string NewTrustLevel);

public sealed record PenaltyRevokeRequest(
    Guid UserId,
    string TargetRole,
    string ReferenceId,
    Guid ApprovedBy,
    string Note);

/// <summary>Outcome is "Restored" (points were given back) or "NothingToRestore" (the penalty had taken none).</summary>
public sealed record PenaltyRevokeResponse(
    Guid UserId,
    string TargetRole,
    bool IsDuplicate,
    string Outcome,
    int RestoredPoints,
    int PreviousScore,
    int NewScore,
    string NewTrustLevel);

public sealed record UserPenaltyRequest(
    Guid UserId,
    string TargetRole,
    string ViolationType,
    string ReferenceType,
    string ReferenceId,
    string Reason,
    int CustomScoreDeduction,
    string PolicyClauseCode);

public sealed record UserPenaltyResponse(
    Guid UserId,
    string TargetRole,
    bool IsDuplicate,
    Guid? PenaltyId,
    int PreviousScore,
    int NewScore,
    int AppliedDelta,
    string NewTrustLevel);

/// <summary>
/// ViolationType values UserService accepts for a penalty (its PenaltyViolationType enum). Anything else is rejected with 400,
/// so the penalty is silently lost. The specific cause (seller rejected, seller did not answer) goes in Reason and PolicyClauseCode.
/// </summary>
public static class UserPenaltyViolationTypes
{
    public const string SellerRejectedPaidOrder = "CancellationWithoutReason";
    public const string SellerResponseTimeout = "Other";
}

/// <summary>Answer of the shop lookup.</summary>
public sealed record SellerShopResult(bool Found, Guid? ShopId);

/// <summary>Represents buyer address retrieved from UserService.</summary>
public sealed record BuyerAddressDto(
    Guid Id,
    string RecipientName,
    string Phone,
    string Street,
    string Ward,
    string District,
    string Province,
    int GhnDistrictId,
    string GhnWardCode,
    string FullAddressText,
    bool IsDefault,
    int? ProvinceId = null
);
