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
