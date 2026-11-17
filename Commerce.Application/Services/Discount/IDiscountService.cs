namespace Commerce.Application.Services.Discount;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Common.Models;
using Commerce.Domain.Enums;

public interface IDiscountService
{
    Task<PagedData<BuyerAvailableVoucherDto>> GetAvailableVouchersAsync(
        Guid buyerId,
        Guid? sellerId,
        IReadOnlyList<Guid>? cartItemIds,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<ValidateVoucherResponse> ValidateVoucherAsync(
        Guid buyerId,
        ValidateVoucherRequest request,
        CancellationToken ct = default);

    Task<RecommendVouchersResponse> RecommendVouchersAsync(
        Guid buyerId,
        RecommendVouchersRequest request,
        CancellationToken ct = default);

    Task<DiscountDetailResponse> CreateDiscountAsync(
        Guid currentUserId,
        bool isAdmin,
        CreateDiscountRequest request,
        CancellationToken ct = default);

    Task<PagedData<DiscountSummaryDto>> ListDiscountsAsync(
        Guid currentUserId,
        bool isAdmin,
        int page,
        int pageSize,
        DiscountScopeType? scopeType = null,
        DiscountStatus? status = null,
        string? search = null,
        CancellationToken ct = default);

    Task<DiscountDetailResponse> GetDiscountByIdAsync(
        Guid currentUserId,
        bool isAdmin,
        Guid discountId,
        CancellationToken ct = default);

    Task<DiscountDetailResponse> UpdateDiscountAsync(
        Guid currentUserId,
        bool isAdmin,
        Guid discountId,
        UpdateDiscountRequest request,
        CancellationToken ct = default);

    Task<DiscountDetailResponse> UpdateDiscountStatusAsync(
        Guid currentUserId,
        bool isAdmin,
        Guid discountId,
        UpdateDiscountStatusRequest request,
        CancellationToken ct = default);

    Task DeleteDiscountAsync(
        Guid currentUserId,
        bool isAdmin,
        Guid discountId,
        CancellationToken ct = default);
}
