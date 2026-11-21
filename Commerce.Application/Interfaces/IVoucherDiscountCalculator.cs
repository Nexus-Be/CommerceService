namespace Commerce.Application.Interfaces;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Commerce.Application.Services.Discount;

public sealed record ShopDiscountBreakdown(
    decimal ProductDiscountAmount = 0m,
    decimal ShopDiscountAmount = 0m,
    decimal SystemDiscountAmount = 0m,
    decimal ShippingDiscountAmount = 0m
);

public sealed record VoucherCalculationRequest(
    Guid BuyerId,
    IReadOnlyList<string> VoucherCodes,
    IReadOnlyDictionary<Guid, decimal> SellerItemsAmounts,
    IReadOnlyDictionary<Guid, decimal> SellerShippingFees,
    IReadOnlyList<CalculationItemInput>? Items = null
);

public sealed record VoucherCalculationResult(
    IReadOnlyDictionary<Guid, ShopDiscountBreakdown> ShopDiscounts,
    IReadOnlyList<string> AppliedVouchers,
    IReadOnlyList<string> RejectedVouchers,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<AllocatedVoucherUsage>? AllocatedUsages = null,
    /// <summary>Why each rejected code was left out, as a stable reason code. Keyed by voucher code.</summary>
    IReadOnlyDictionary<string, string>? RejectedReasons = null
);

public interface IVoucherDiscountCalculator
{
    Task<VoucherCalculationResult> CalculateDiscountsAsync(
        VoucherCalculationRequest request,
        CancellationToken ct = default);
}
