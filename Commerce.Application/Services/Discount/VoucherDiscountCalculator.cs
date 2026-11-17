namespace Commerce.Application.Services.Discount;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Commerce.Application.Interfaces;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;

public sealed class VoucherDiscountCalculator : IVoucherDiscountCalculator
{
    private readonly ICommerceDbContext _db;
    private readonly IVoucherCalculationEngine _calculationEngine;

    public VoucherDiscountCalculator(
        ICommerceDbContext db,
        IVoucherCalculationEngine calculationEngine)
    {
        _db = db;
        _calculationEngine = calculationEngine;
    }

    public async Task<VoucherCalculationResult> CalculateDiscountsAsync(
        VoucherCalculationRequest request,
        CancellationToken ct = default)
    {
        var sellerAmounts = request.SellerItemsAmounts ?? new Dictionary<Guid, decimal>();
        var sellerShipping = request.SellerShippingFees ?? new Dictionary<Guid, decimal>();
        var codes = request.VoucherCodes ?? Array.Empty<string>();

        if (codes.Count == 0 || sellerAmounts.Count == 0)
        {
            var emptyBreakdowns = sellerAmounts.ToDictionary(
                kvp => kvp.Key,
                _ => new ShopDiscountBreakdown(0m, 0m, 0m, 0m)
            );

            return new VoucherCalculationResult(
                ShopDiscounts: emptyBreakdowns,
                AppliedVouchers: Array.Empty<string>(),
                RejectedVouchers: Array.Empty<string>(),
                Warnings: Array.Empty<string>(),
                AllocatedUsages: Array.Empty<AllocatedVoucherUsage>()
            );
        }

        // Query active or candidate vouchers matching codes
        var normalizedCodes = codes.Select(c => c.Trim().ToUpperInvariant()).ToList();
        var matchingVouchers = await _db.DiscountCodes
            .Include(d => d.Scopes)
            .Where(d => !d.IsDeleted && normalizedCodes.Contains(d.Code.ToUpper()))
            .ToListAsync(ct);

        var foundCodes = matchingVouchers.Select(v => v.Code.ToUpperInvariant()).ToHashSet();
        var warnings = new List<string>();
        var rejectedCodes = new List<string>();

        foreach (var code in codes)
        {
            if (!foundCodes.Contains(code.Trim().ToUpperInvariant()))
            {
                warnings.Add($"Voucher code '{code}' was not found.");
                rejectedCodes.Add(code);
            }
        }

        // Prepare calculation items: use provided items or synthesize from shop amounts
        IReadOnlyList<CalculationItemInput> calculationItems;
        if (request.Items != null && request.Items.Count > 0)
        {
            calculationItems = request.Items;
        }
        else
        {
            var synthList = new List<CalculationItemInput>();
            foreach (var (sellerId, amount) in sellerAmounts)
            {
                synthList.Add(new CalculationItemInput(
                    CartItemId: Guid.Empty,
                    ProductId: Guid.Empty,
                    SkuId: Guid.Empty,
                    SellerId: sellerId,
                    ProductName: $"Shop Items",
                    Quantity: 1,
                    UnitPrice: amount
                ));
            }
            calculationItems = synthList;
        }

        var evalRequest = new EvaluateVouchersRequest(
            Items: calculationItems,
            AvailableVouchers: matchingVouchers,
            PlatformMarkupPercent: 0m, // Evaluated pure discounts
            SellerShippingFees: sellerShipping
        );

        EvaluateVouchersResult evalResult;
        try
        {
            evalResult = _calculationEngine.EvaluateAndApplyVouchers(evalRequest);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to evaluate vouchers: {ex.Message}");
            evalResult = new EvaluateVouchersResult(
                ShopResults: new Dictionary<Guid, ShopCalculationResult>(),
                AllAllocatedUsages: Array.Empty<AllocatedVoucherUsage>(),
                AppliedVoucherCodes: Array.Empty<string>(),
                RejectedVoucherCodes: codes.ToList(),
                Warnings: warnings
            );
        }

        var shopDiscounts = new Dictionary<Guid, ShopDiscountBreakdown>();
        foreach (var (sellerId, _) in sellerAmounts)
        {
            if (evalResult.ShopResults.TryGetValue(sellerId, out var shopRes))
            {
                shopDiscounts[sellerId] = new ShopDiscountBreakdown(
                    ProductDiscountAmount: shopRes.ProductDiscountAmount,
                    ShopDiscountAmount: shopRes.ShopDiscountAmount,
                    SystemDiscountAmount: shopRes.SystemDiscountAmount,
                    ShippingDiscountAmount: 0m
                );
            }
            else
            {
                shopDiscounts[sellerId] = new ShopDiscountBreakdown(0m, 0m, 0m, 0m);
            }
        }

        var allWarnings = warnings.Concat(evalResult.Warnings).Distinct().ToList();
        var allRejected = rejectedCodes.Concat(evalResult.RejectedVoucherCodes).Distinct().ToList();

        return new VoucherCalculationResult(
            ShopDiscounts: shopDiscounts,
            AppliedVouchers: evalResult.AppliedVoucherCodes,
            RejectedVouchers: allRejected,
            Warnings: allWarnings,
            AllocatedUsages: evalResult.AllAllocatedUsages
        );
    }
}
