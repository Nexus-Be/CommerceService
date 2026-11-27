namespace Commerce.Application.Services.Discount;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Commerce.Domain.Enums;

public sealed record ValidateVoucherRequest(
    [Required] string Code,
    [Required] IReadOnlyList<Guid> CartItemIds
);

public sealed record ValidateVoucherResponse(
    bool IsValid,
    Guid? DiscountId,
    string? Code,
    DiscountScopeType? ScopeType,
    decimal EstimatedDiscountAmount,
    string? IneligibleReason = null,
    string? Message = null,
    Guid? OwnerSellerId = null,
    DiscountType? DiscountType = null,
    decimal? DiscountValue = null,
    decimal? MinOrderAmount = null,
    decimal? MaxDiscountAmount = null
)
{
    public ValidateVoucherResponse(
        bool isValid,
        Guid discountId,
        string code,
        DiscountScopeType scopeType,
        decimal estimatedDiscountAmount)
        : this(isValid, discountId, code, scopeType, estimatedDiscountAmount, null, null)
    {
    }

    public ValidateVoucherResponse(
        bool isValid,
        string ineligibleReason,
        string message,
        decimal estimatedDiscountAmount = 0m)
        : this(isValid, null, null, null, estimatedDiscountAmount, ineligibleReason, message)
    {
    }
}

public sealed record RecommendVouchersRequest(
    [Required] IReadOnlyList<Guid> CartItemIds,
    PaymentMethod? PaymentMethod = null
);

public sealed record TierSavingsDto(
    decimal ProductDiscount,
    decimal ShopDiscount,
    decimal SystemDiscount,
    decimal TotalSavings
);

public sealed record RecommendVouchersResponse(
    IReadOnlyList<string> RecommendedVoucherCodes,
    TierSavingsDto SavingsBreakdown
);

public sealed record CreateDiscountScopeInput(
    DiscountScopeRefType ScopeRefType,
    Guid? ScopeRefId
);

public sealed record CreateDiscountRequest(
    [Required] string Code,
    [Required] string Name,
    DiscountScopeType ScopeType,
    Guid? OwnerSellerId,
    DiscountType DiscountType,
    [Range(0.01, double.MaxValue, ErrorMessage = "Discount value must be greater than 0.")] decimal DiscountValue,
    decimal MinOrderAmount = 0m,
    decimal? MaxDiscountAmount = null,
    int? MaxUsesTotal = null,
    int MaxUsesPerUser = 1,
    bool IsVisible = true,
    DateTimeOffset? StartsAt = null,
    DateTimeOffset? EndsAt = null,
    IReadOnlyList<CreateDiscountScopeInput>? Scopes = null
);

public sealed record UpdateDiscountRequest(
    [Required] string Name,
    bool IsVisible,
    int MaxUsesPerUser,
    int? MaxUsesTotal = null,
    DateTimeOffset? EndsAt = null
);

public sealed record UpdateDiscountStatusRequest(
    DiscountStatus Status
);

public sealed record DiscountScopeDto(
    Guid Id,
    DiscountScopeRefType ScopeRefType,
    Guid? ScopeRefId
);

public sealed record DiscountDetailResponse(
    Guid Id,
    string Code,
    string Name,
    DiscountScopeType ScopeType,
    Guid? OwnerSellerId,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal MinOrderAmount,
    decimal? MaxDiscountAmount,
    int? MaxUsesTotal,
    int MaxUsesPerUser,
    int UsedCount,
    int ReservedCount,
    bool IsVisible,
    DiscountStatus Status,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<DiscountScopeDto> Scopes
);

public sealed record DiscountSummaryDto(
    Guid Id,
    string Code,
    string Name,
    DiscountScopeType ScopeType,
    Guid? OwnerSellerId,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal MinOrderAmount,
    decimal? MaxDiscountAmount,
    int? MaxUsesTotal,
    int UsedCount,
    int ReservedCount,
    bool IsVisible,
    DiscountStatus Status,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool IsExpired = false,
    int? RemainingUses = null
);

public sealed record BuyerAvailableVoucherDto(
    Guid Id,
    string Code,
    string Name,
    DiscountScopeType ScopeType,
    Guid? OwnerSellerId,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal MinOrderAmount,
    decimal? MaxDiscountAmount,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool IsEligible,
    string? IneligibleReason,
    decimal EstimatedDiscountAmount
);
