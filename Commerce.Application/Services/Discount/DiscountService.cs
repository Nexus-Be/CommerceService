namespace Commerce.Application.Services.Discount;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Commerce.Application.Common.Models;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;
using Commerce.Domain.Exceptions;

public sealed class DiscountService : IDiscountService
{
    private readonly ICommerceDbContext _db;
    private readonly IUserClient _userClient;
    private readonly ICatalogClient _catalogClient;
    private readonly IVoucherCalculationEngine _calculationEngine;

    public DiscountService(
        ICommerceDbContext db,
        IUserClient userClient,
        ICatalogClient catalogClient,
        IVoucherCalculationEngine calculationEngine)
    {
        _db = db;
        _userClient = userClient;
        _catalogClient = catalogClient;
        _calculationEngine = calculationEngine;
    }

    private async Task EnforceSellerManagePermissionAsync(Guid sellerId, CancellationToken ct)
    {
        var perm = await _userClient.CheckBuyerPermissionAsync(sellerId, "manage_voucher", ct);
        if (!perm.IsAllowed)
        {
            throw new CommerceDomainException(
                perm.ErrorCode ?? "SELLER_ACTION_RESTRICTED",
                perm.Message ?? "Your seller account is restricted from managing promotion vouchers.",
                403);
        }
    }

    public async Task<DiscountDetailResponse> CreateDiscountAsync(
        Guid currentUserId,
        bool isAdmin,
        CreateDiscountRequest request,
        CancellationToken ct = default)
    {
        if (!isAdmin)
        {
            await EnforceSellerManagePermissionAsync(currentUserId, ct);
        }

        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var exists = await _db.DiscountCodes.AnyAsync(
            d => d.Code.ToUpper() == normalizedCode && !d.IsDeleted,
            ct);
        if (exists)
        {
            throw new CommerceDomainException("DISCOUNT_CODE_ALREADY_EXISTS", $"Voucher code '{request.Code}' already exists.", 400);
        }

        Guid? ownerSellerId;
        if (request.ScopeType == DiscountScopeType.System)
        {
            if (!isAdmin)
            {
                throw new CommerceDomainException("FORBIDDEN", "Only administrators can create system-wide vouchers.", 403);
            }
            ownerSellerId = null;
        }
        else
        {
            ownerSellerId = isAdmin ? (request.OwnerSellerId ?? currentUserId) : currentUserId;
        }

        if (request.DiscountType == DiscountType.Percentage && (request.DiscountValue <= 0 || request.DiscountValue > 100))
        {
            throw new CommerceDomainException("VAL_INVALID_INPUT", "Percentage discount value must be between 1 and 100.", 400);
        }

        if (request.StartsAt.HasValue && request.EndsAt.HasValue && request.EndsAt.Value <= request.StartsAt.Value)
        {
            throw new CommerceDomainException("VAL_INVALID_INPUT", "End date must be strictly after start date.", 400);
        }

        // Validate SKU scopes if Product scope
        var scopesToCreate = new List<DiscountCodeScope>();
        if (request.ScopeType == DiscountScopeType.Product)
        {
            var skuScopes = (request.Scopes ?? Array.Empty<CreateDiscountScopeInput>())
                .Where(s => s.ScopeRefType == DiscountScopeRefType.Sku && s.ScopeRefId.HasValue)
                .ToList();

            if (skuScopes.Count > 0)
            {
                var skuIds = skuScopes.Select(s => s.ScopeRefId!.Value).ToList();
                var catalogBatch = await _catalogClient.GetSkuBatchDetailsAsync(skuIds, ct);

                foreach (var s in skuScopes)
                {
                    if (!catalogBatch.TryGetValue(s.ScopeRefId!.Value, out var skuInfo) || skuInfo == null)
                    {
                        throw new CommerceDomainException("CATALOG_SKU_NOT_FOUND", $"Product SKU '{s.ScopeRefId}' was not found in catalog.", 404);
                    }

                    if (!skuInfo.IsPurchasable)
                    {
                        throw new CommerceDomainException("CATALOG_SKU_INACTIVE", $"Product '{skuInfo.ProductName}' is no longer available for purchase.", 400);
                    }

                    if (string.Equals(skuInfo.SaleType, "Auction", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CommerceDomainException("VOUCHER_SKU_AUCTION_NOT_ALLOWED", "Voucher cannot be applied to auction products.", 400);
                    }

                    if (!isAdmin && skuInfo.SellerId != currentUserId)
                    {
                        throw new CommerceDomainException("VOUCHER_SELLER_SKU_FORBIDDEN", $"Product SKU '{s.ScopeRefId}' does not belong to your shop.", 403);
                    }
                }
            }
        }

        var discount = new DiscountCode
        {
            Id = Guid.NewGuid(),
            Code = normalizedCode,
            Name = request.Name.Trim(),
            ScopeType = request.ScopeType,
            OwnerSellerId = ownerSellerId,
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            MinOrderAmount = request.MinOrderAmount,
            MaxDiscountAmount = request.MaxDiscountAmount,
            MaxUsesTotal = request.MaxUsesTotal,
            MaxUsesPerUser = request.MaxUsesPerUser > 0 ? request.MaxUsesPerUser : 1,
            UsedCount = 0,
            ReservedCount = 0,
            IsVisible = request.IsVisible,
            Status = DiscountStatus.Active,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            CreatedBy = currentUserId
        };

        if (request.Scopes != null)
        {
            foreach (var s in request.Scopes)
            {
                discount.Scopes.Add(new DiscountCodeScope
                {
                    Id = Guid.NewGuid(),
                    DiscountCodeId = discount.Id,
                    ScopeRefType = s.ScopeRefType,
                    ScopeRefId = s.ScopeRefId
                });
            }
        }

        _db.DiscountCodes.Add(discount);
        await _db.SaveChangesAsync(ct);

        return MapToDetailResponse(discount);
    }

    public async Task<PagedData<DiscountSummaryDto>> ListDiscountsAsync(
        Guid currentUserId,
        bool isAdmin,
        int page,
        int pageSize,
        DiscountScopeType? scopeType = null,
        DiscountStatus? status = null,
        string? search = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize is > 0 and <= 100 ? pageSize : 20;

        var query = _db.DiscountCodes.AsNoTracking().Where(d => !d.IsDeleted);

        if (!isAdmin)
        {
            query = query.Where(d => d.OwnerSellerId == currentUserId);
        }

        if (scopeType.HasValue)
        {
            query = query.Where(d => d.ScopeType == scopeType.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToUpperInvariant();
            query = query.Where(d => d.Code.ToUpper().Contains(s) || d.Name.ToUpper().Contains(s));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DiscountSummaryDto(
                d.Id,
                d.Code,
                d.Name,
                d.ScopeType,
                d.OwnerSellerId,
                d.DiscountType,
                d.DiscountValue,
                d.MinOrderAmount,
                d.MaxDiscountAmount,
                d.MaxUsesTotal,
                d.UsedCount,
                d.ReservedCount,
                d.IsVisible,
                d.Status,
                d.StartsAt,
                d.EndsAt
            ))
            .ToListAsync(ct);

        return new PagedData<DiscountSummaryDto>(items, PaginationMetadata.Create(page, pageSize, totalCount));
    }

    public async Task<DiscountDetailResponse> GetDiscountByIdAsync(
        Guid currentUserId,
        bool isAdmin,
        Guid discountId,
        CancellationToken ct = default)
    {
        var voucher = await _db.DiscountCodes
            .AsNoTracking()
            .Include(d => d.Scopes)
            .FirstOrDefaultAsync(d => d.Id == discountId && !d.IsDeleted, ct);

        if (voucher == null)
        {
            throw new CommerceDomainException("VOUCHER_NOT_FOUND", "Voucher was not found.", 404);
        }

        if (!isAdmin && voucher.OwnerSellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to view this voucher.", 403);
        }

        return MapToDetailResponse(voucher);
    }

    public async Task<DiscountDetailResponse> UpdateDiscountAsync(
        Guid currentUserId,
        bool isAdmin,
        Guid discountId,
        UpdateDiscountRequest request,
        CancellationToken ct = default)
    {
        if (!isAdmin)
        {
            await EnforceSellerManagePermissionAsync(currentUserId, ct);
        }

        var voucher = await _db.DiscountCodes
            .Include(d => d.Scopes)
            .FirstOrDefaultAsync(d => d.Id == discountId && !d.IsDeleted, ct);

        if (voucher == null)
        {
            throw new CommerceDomainException("VOUCHER_NOT_FOUND", "Voucher was not found.", 404);
        }

        if (!isAdmin && voucher.OwnerSellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to edit this voucher.", 403);
        }

        // Financial freeze guard: check max uses total
        if (request.MaxUsesTotal.HasValue && request.MaxUsesTotal.Value < (voucher.UsedCount + voucher.ReservedCount))
        {
            throw new CommerceDomainException("VOUCHER_USAGE_LIMIT_CONFLICT", "Total usage limit cannot be lower than the number of used and reserved vouchers.", 400);
        }

        voucher.Name = request.Name.Trim();
        voucher.IsVisible = request.IsVisible;
        voucher.MaxUsesPerUser = request.MaxUsesPerUser > 0 ? request.MaxUsesPerUser : 1;
        voucher.MaxUsesTotal = request.MaxUsesTotal;
        voucher.EndsAt = request.EndsAt;
        voucher.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MapToDetailResponse(voucher);
    }

    public async Task<DiscountDetailResponse> UpdateDiscountStatusAsync(
        Guid currentUserId,
        bool isAdmin,
        Guid discountId,
        UpdateDiscountStatusRequest request,
        CancellationToken ct = default)
    {
        if (!isAdmin)
        {
            await EnforceSellerManagePermissionAsync(currentUserId, ct);
        }

        var voucher = await _db.DiscountCodes
            .Include(d => d.Scopes)
            .FirstOrDefaultAsync(d => d.Id == discountId && !d.IsDeleted, ct);

        if (voucher == null)
        {
            throw new CommerceDomainException("VOUCHER_NOT_FOUND", "Voucher was not found.", 404);
        }

        if (!isAdmin && voucher.OwnerSellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to change status of this voucher.", 403);
        }

        if (request.Status == DiscountStatus.Active && voucher.EndsAt.HasValue && voucher.EndsAt.Value <= DateTimeOffset.UtcNow)
        {
            throw new CommerceDomainException("VOUCHER_EXPIRED", "Cannot activate an expired voucher.", 400);
        }

        voucher.Status = request.Status;
        voucher.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MapToDetailResponse(voucher);
    }

    public async Task DeleteDiscountAsync(
        Guid currentUserId,
        bool isAdmin,
        Guid discountId,
        CancellationToken ct = default)
    {
        if (!isAdmin)
        {
            await EnforceSellerManagePermissionAsync(currentUserId, ct);
        }

        var voucher = await _db.DiscountCodes
            .Include(d => d.Scopes)
            .FirstOrDefaultAsync(d => d.Id == discountId && !d.IsDeleted, ct);

        if (voucher == null)
        {
            throw new CommerceDomainException("VOUCHER_NOT_FOUND", "Voucher was not found.", 404);
        }

        if (!isAdmin && voucher.OwnerSellerId != currentUserId)
        {
            throw new CommerceDomainException("FORBIDDEN", "You do not have permission to delete this voucher.", 403);
        }

        // Financial freeze guard: prohibit deletion if in use
        if (voucher.UsedCount > 0 || voucher.ReservedCount > 0)
        {
            throw new CommerceDomainException("VOUCHER_ALREADY_IN_USE", "Voucher that has been used or is currently reserved cannot be deleted.", 400);
        }

        var now = DateTimeOffset.UtcNow;
        voucher.IsDeleted = true;
        voucher.DeletedAt = now;

        foreach (var s in voucher.Scopes)
        {
            s.IsDeleted = true;
            s.DeletedAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<PagedData<BuyerAvailableVoucherDto>> GetAvailableVouchersAsync(
        Guid buyerId,
        Guid? sellerId,
        IReadOnlyList<Guid>? cartItemIds,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize is > 0 and <= 100 ? pageSize : 20;

        var now = DateTimeOffset.UtcNow;
        var query = _db.DiscountCodes
            .AsNoTracking()
            .Include(d => d.Scopes)
            .Where(d => !d.IsDeleted && d.Status == DiscountStatus.Active && d.IsVisible)
            .Where(d => (!d.StartsAt.HasValue || d.StartsAt.Value <= now) && (!d.EndsAt.HasValue || d.EndsAt.Value > now));

        if (sellerId.HasValue)
        {
            query = query.Where(d => d.ScopeType == DiscountScopeType.System || d.OwnerSellerId == sellerId.Value);
        }

        // Load cart items if provided
        List<CartItem> cartItems = new();
        if (cartItemIds != null && cartItemIds.Count > 0)
        {
            cartItems = await _db.CartItems
                .AsNoTracking()
                .Where(ci => cartItemIds.Contains(ci.Id) && ci.Cart != null && ci.Cart.BuyerId == buyerId && !ci.IsDeleted)
                .ToListAsync(ct);
        }

        var totalCount = await query.CountAsync(ct);
        var vouchers = await query
            .OrderBy(d => d.MinOrderAmount)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtoList = new List<BuyerAvailableVoucherDto>();
        foreach (var v in vouchers)
        {
            bool isEligible = true;
            string? ineligibleReason = null;
            decimal estimatedDiscount = 0m;

            if (cartItems.Count > 0)
            {
                var calcItems = cartItems.Select(ci => new CalculationItemInput(
                    ci.Id, ci.ProductId, ci.SkuId, ci.SellerId, ci.ProductNameSnapshot, ci.Quantity, ci.UnitPriceSnapshot
                )).ToList();

                var evalRes = _calculationEngine.EvaluateAndApplyVouchers(new EvaluateVouchersRequest(
                    Items: calcItems,
                    AvailableVouchers: new List<DiscountCode> { v }
                ));

                if (evalRes.AppliedVoucherCodes.Contains(v.Code))
                {
                    isEligible = true;
                    estimatedDiscount = evalRes.AllAllocatedUsages.Where(u => u.DiscountCodeId == v.Id).Sum(u => u.DiscountAmount);
                }
                else
                {
                    isEligible = false;
                    ineligibleReason = evalRes.Warnings.FirstOrDefault() ?? "Minimum order amount not met.";
                }
            }

            dtoList.Add(new BuyerAvailableVoucherDto(
                v.Id,
                v.Code,
                v.Name,
                v.ScopeType,
                v.OwnerSellerId,
                v.DiscountType,
                v.DiscountValue,
                v.MinOrderAmount,
                v.MaxDiscountAmount,
                v.StartsAt,
                v.EndsAt,
                isEligible,
                ineligibleReason,
                estimatedDiscount
            ));
        }

        return new PagedData<BuyerAvailableVoucherDto>(dtoList, PaginationMetadata.Create(page, pageSize, totalCount));
    }

    public async Task<ValidateVoucherResponse> ValidateVoucherAsync(
        Guid buyerId,
        ValidateVoucherRequest request,
        CancellationToken ct = default)
    {
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var voucher = await _db.DiscountCodes
            .AsNoTracking()
            .Include(d => d.Scopes)
            .FirstOrDefaultAsync(d => d.Code.ToUpper() == normalizedCode && !d.IsDeleted, ct);

        if (voucher == null || voucher.Status != DiscountStatus.Active)
        {
            throw new CommerceDomainException("VOUCHER_NOT_FOUND", "Voucher code was not found or is not active.", 404);
        }

        if (voucher.MaxUsesTotal.HasValue && (voucher.UsedCount + voucher.ReservedCount) >= voucher.MaxUsesTotal.Value)
        {
            return new ValidateVoucherResponse(false, "VOUCHER_OUT_OF_STOCK", "Voucher has reached its total usage limit.", 0m);
        }

        if (voucher.MaxUsesPerUser.HasValue)
        {
            var userUsageCount = await _db.DiscountCodeUsages
                .AsNoTracking()
                .CountAsync(u => u.DiscountCodeId == voucher.Id && u.UserId == buyerId && u.Status != DiscountUsageStatus.Released && !u.IsDeleted, ct);

            if (userUsageCount >= voucher.MaxUsesPerUser.Value)
            {
                return new ValidateVoucherResponse(false, "VOUCHER_USAGE_LIMIT_EXCEEDED", "You have reached the usage limit for this voucher.", 0m);
            }
        }

        var cartItems = await _db.CartItems
            .AsNoTracking()
            .Where(ci => request.CartItemIds.Contains(ci.Id) && ci.Cart != null && ci.Cart.BuyerId == buyerId && !ci.IsDeleted)
            .ToListAsync(ct);

        if (cartItems.Count == 0 || cartItems.Count != request.CartItemIds.Distinct().Count())
        {
            throw new CommerceDomainException("VAL_INVALID_INPUT", "No valid cart items provided for voucher validation.", 400);
        }

        var calcItems = cartItems.Select(ci => new CalculationItemInput(
            ci.Id, ci.ProductId, ci.SkuId, ci.SellerId, ci.ProductNameSnapshot, ci.Quantity, ci.UnitPriceSnapshot
        )).ToList();

        var evalResult = _calculationEngine.EvaluateAndApplyVouchers(new EvaluateVouchersRequest(
            Items: calcItems,
            AvailableVouchers: new List<DiscountCode> { voucher }
        ));

        if (!evalResult.AppliedVoucherCodes.Contains(voucher.Code))
        {
            var reason = evalResult.Warnings.FirstOrDefault() ?? "Voucher is not applicable to current cart items.";
            throw new CommerceDomainException("VOUCHER_NOT_APPLICABLE", reason, 400);
        }

        var estimatedDiscount = evalResult.AllAllocatedUsages
            .Where(u => u.DiscountCodeId == voucher.Id)
            .Sum(u => u.DiscountAmount);

        return new ValidateVoucherResponse(
            IsValid: true,
            DiscountId: voucher.Id,
            Code: voucher.Code,
            ScopeType: voucher.ScopeType,
            EstimatedDiscountAmount: estimatedDiscount
        );
    }

    public async Task<RecommendVouchersResponse> RecommendVouchersAsync(
        Guid buyerId,
        RecommendVouchersRequest request,
        CancellationToken ct = default)
    {
        var cartItems = await _db.CartItems
            .AsNoTracking()
            .Where(ci => request.CartItemIds.Contains(ci.Id) && ci.Cart != null && ci.Cart.BuyerId == buyerId && !ci.IsDeleted)
            .ToListAsync(ct);

        if (cartItems.Count == 0)
        {
            return new RecommendVouchersResponse(Array.Empty<string>(), new TierSavingsDto(0m, 0m, 0m, 0m));
        }

        var calcItems = cartItems.Select(ci => new CalculationItemInput(
            ci.Id, ci.ProductId, ci.SkuId, ci.SellerId, ci.ProductNameSnapshot, ci.Quantity, ci.UnitPriceSnapshot
        )).ToList();

        var sellerIds = cartItems.Select(i => i.SellerId).Distinct().ToList();
        var skuIds = cartItems.Select(i => i.SkuId).Distinct().ToList();
        var now = DateTimeOffset.UtcNow;

        var candidateVouchers = await _db.DiscountCodes
            .AsNoTracking()
            .Include(d => d.Scopes)
            .Where(d => !d.IsDeleted && d.Status == DiscountStatus.Active && d.IsVisible)
            .Where(d => (!d.StartsAt.HasValue || d.StartsAt.Value <= now) && (!d.EndsAt.HasValue || d.EndsAt.Value > now))
            .Where(d => d.ScopeType == DiscountScopeType.System || (d.OwnerSellerId.HasValue && sellerIds.Contains(d.OwnerSellerId.Value)))
            .ToListAsync(ct);

        // Pick optimal voucher per shop (Shopee 2-tier model: single best voucher per shop having max(ProductDiscount, ShopDiscount))
        var optimalCandidates = new List<DiscountCode>();

        foreach (var sId in sellerIds)
        {
            var sellerItems = cartItems.Where(i => i.SellerId == sId).ToList();
            var shopVouchers = candidateVouchers
                .Where(v => v.OwnerSellerId == sId && (v.ScopeType == DiscountScopeType.Shop || v.ScopeType == DiscountScopeType.Product))
                .ToList();

            DiscountCode? bestShopVoucher = null;
            decimal maxShopDiscount = 0m;

            foreach (var v in shopVouchers)
            {
                decimal candidateDiscount = 0m;
                if (v.ScopeType == DiscountScopeType.Product)
                {
                    var targetSkuIds = (v.Scopes ?? Enumerable.Empty<DiscountCodeScope>())
                        .Where(s => s.ScopeRefType == DiscountScopeRefType.Sku && s.ScopeRefId.HasValue)
                        .Select(s => s.ScopeRefId!.Value)
                        .ToHashSet();

                    var eligibleItems = targetSkuIds.Count > 0
                        ? sellerItems.Where(i => targetSkuIds.Contains(i.SkuId)).ToList()
                        : sellerItems;

                    if (eligibleItems.Count > 0)
                    {
                        var baseAmount = eligibleItems.Sum(i => i.Quantity * i.UnitPriceSnapshot);
                        if (baseAmount >= v.MinOrderAmount)
                        {
                            candidateDiscount = _calculationEngine.CalculateRawDiscount(baseAmount, v);
                        }
                    }
                }
                else if (v.ScopeType == DiscountScopeType.Shop)
                {
                    var baseAmount = sellerItems.Sum(i => i.Quantity * i.UnitPriceSnapshot);
                    if (baseAmount >= v.MinOrderAmount)
                    {
                        candidateDiscount = _calculationEngine.CalculateRawDiscount(baseAmount, v);
                    }
                }

                if (candidateDiscount > maxShopDiscount)
                {
                    maxShopDiscount = candidateDiscount;
                    bestShopVoucher = v;
                }
            }

            if (bestShopVoucher != null && maxShopDiscount > 0m)
            {
                optimalCandidates.Add(bestShopVoucher);
            }
        }

        // 3. Best System voucher
        var systemCandidates = candidateVouchers
            .Where(v => v.ScopeType == DiscountScopeType.System)
            .ToList();

        DiscountCode? bestSystemVoucher = null;
        decimal maxSystemDiscount = 0m;

        var totalItemsBase = cartItems.Sum(i => i.Quantity * i.UnitPriceSnapshot);
        var remainingSystemBase = Math.Max(0m, totalItemsBase - optimalCandidates.Sum(v =>
        {
            var sId = v.OwnerSellerId!.Value;
            var sItems = cartItems.Where(i => i.SellerId == sId);
            if (v.ScopeType == DiscountScopeType.Product)
            {
                var targetSkuIds = (v.Scopes ?? Enumerable.Empty<DiscountCodeScope>())
                    .Where(s => s.ScopeRefType == DiscountScopeRefType.Sku && s.ScopeRefId.HasValue)
                    .Select(s => s.ScopeRefId!.Value)
                    .ToHashSet();
                return sItems.Where(i => targetSkuIds.Contains(i.SkuId)).Sum(i => i.Quantity * i.UnitPriceSnapshot);
            }
            return sItems.Sum(i => i.Quantity * i.UnitPriceSnapshot);
        }));

        foreach (var sysV in systemCandidates)
        {
            if (remainingSystemBase >= sysV.MinOrderAmount)
            {
                var disc = _calculationEngine.CalculateRawDiscount(remainingSystemBase, sysV);
                if (disc > maxSystemDiscount)
                {
                    maxSystemDiscount = disc;
                    bestSystemVoucher = sysV;
                }
            }
        }

        if (bestSystemVoucher != null && maxSystemDiscount > 0m)
        {
            optimalCandidates.Add(bestSystemVoucher);
        }

        var evalResult = _calculationEngine.EvaluateAndApplyVouchers(new EvaluateVouchersRequest(
            Items: calcItems,
            AvailableVouchers: optimalCandidates
        ));

        decimal prodDisc = 0m;
        decimal shopDisc = 0m;
        decimal sysDisc = 0m;

        foreach (var usage in evalResult.AllAllocatedUsages)
        {
            if (usage.ScopeType == DiscountScopeType.Product) prodDisc += usage.DiscountAmount;
            else if (usage.ScopeType == DiscountScopeType.Shop) shopDisc += usage.DiscountAmount;
            else if (usage.ScopeType == DiscountScopeType.System) sysDisc += usage.DiscountAmount;
        }

        var savings = new TierSavingsDto(prodDisc, shopDisc, sysDisc, prodDisc + shopDisc + sysDisc);

        return new RecommendVouchersResponse(
            RecommendedVoucherCodes: evalResult.AppliedVoucherCodes,
            SavingsBreakdown: savings
        );
    }

    private static DiscountDetailResponse MapToDetailResponse(DiscountCode d)
    {
        var scopes = (d.Scopes ?? Enumerable.Empty<DiscountCodeScope>())
            .Where(s => !s.IsDeleted)
            .Select(s => new DiscountScopeDto(s.Id, s.ScopeRefType, s.ScopeRefId))
            .ToList();

        return new DiscountDetailResponse(
            d.Id,
            d.Code,
            d.Name,
            d.ScopeType,
            d.OwnerSellerId,
            d.DiscountType,
            d.DiscountValue,
            d.MinOrderAmount,
            d.MaxDiscountAmount,
            d.MaxUsesTotal,
            d.MaxUsesPerUser ?? 1,
            d.UsedCount,
            d.ReservedCount,
            d.IsVisible,
            d.Status,
            d.StartsAt,
            d.EndsAt,
            d.CreatedAt,
            scopes
        );
    }
}
