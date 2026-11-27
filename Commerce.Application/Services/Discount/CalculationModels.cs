namespace Commerce.Application.Services.Discount;

using System;
using System.Collections.Generic;
using Commerce.Domain.Entities;
using Commerce.Domain.Enums;

public sealed record CalculationItemInput(
    Guid CartItemId,
    Guid ProductId,
    Guid SkuId,
    Guid SellerId,
    string ProductName,
    int Quantity,
    decimal UnitPrice
);

public sealed record EvaluateVouchersRequest(
    IReadOnlyList<CalculationItemInput> Items,
    IReadOnlyList<DiscountCode> AvailableVouchers,
    decimal PlatformMarkupPercent = 0.0m,
    IReadOnlyDictionary<Guid, decimal>? SellerShippingFees = null
);

public sealed record AllocatedVoucherUsage(
    Guid DiscountCodeId,
    string Code,
    DiscountScopeType ScopeType,
    Guid? SubOrderSellerId,
    decimal DiscountAmount
);

public sealed record ShopCalculationResult(
    Guid SellerId,
    decimal ItemsAmount,
    decimal ProductDiscountAmount,
    decimal ShopDiscountAmount,
    decimal SystemDiscountAmount,
    decimal ShippingFeeAmount,
    decimal PlatformFeeAmount,
    decimal SellerReceiveAmount,
    decimal TotalAmount,
    IReadOnlyList<AllocatedVoucherUsage> AllocatedUsages,
    IReadOnlyDictionary<Guid, decimal>? ItemProductDiscounts = null
);

public sealed record EvaluateVouchersResult(
    IReadOnlyDictionary<Guid, ShopCalculationResult> ShopResults,
    IReadOnlyList<AllocatedVoucherUsage> AllAllocatedUsages,
    IReadOnlyList<string> AppliedVoucherCodes,
    IReadOnlyList<string> RejectedVoucherCodes,
    IReadOnlyList<string> Warnings,
    /// <summary>Why each rejected code was left out, as a stable reason code (for example MIN_ORDER_NOT_MET). Keyed by voucher code.</summary>
    IReadOnlyDictionary<string, string>? RejectedReasons = null
);
