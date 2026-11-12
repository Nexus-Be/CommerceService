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
