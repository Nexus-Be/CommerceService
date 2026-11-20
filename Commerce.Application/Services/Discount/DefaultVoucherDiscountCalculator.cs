namespace Commerce.Application.Services.Discount;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Interfaces;

/// <summary>
/// Pass-through voucher discount calculator returning zero discounts.
/// Serves as the extensible foundation until Feature 024 (Voucher & Promotion Allocation Engine) is implemented.
/// </summary>
public sealed class DefaultVoucherDiscountCalculator : IVoucherDiscountCalculator
{
    public Task<VoucherCalculationResult> CalculateDiscountsAsync(
        VoucherCalculationRequest request,
        CancellationToken ct = default)
    {
        var discounts = request.SellerItemsAmounts.ToDictionary(
            kvp => kvp.Key,
            _ => new ShopDiscountBreakdown(0m, 0m, 0m)
        );

        var result = new VoucherCalculationResult(
            ShopDiscounts: discounts,
            AppliedVouchers: Array.Empty<string>(),
            RejectedVouchers: request.VoucherCodes?.ToList() ?? new List<string>()
        );

        return Task.FromResult(result);
    }
}
